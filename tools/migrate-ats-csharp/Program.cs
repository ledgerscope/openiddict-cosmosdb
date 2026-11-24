using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ledgerscope.OpenIddict.MigrateAts;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--apply"))
        {
            Console.Error.WriteLine("Usage: dotnet run -- <table endpoint> <Cosmos database endpoint> [--apply]");
            return 1;
        }

        try
        {
            var table = ValidateEndpoint(args[0], true);
            var cosmos = ValidateEndpoint(args[1], false);
            using var credentials = new AzureCliIdentity();
            await credentials.LoginAsync();
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            await new Migration(http, credentials.GetTokenAsync, table, cosmos).RunAsync(args.Length == 3);
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.Error.WriteLine("Migration stopped: a service request failed or timed out.");
            return 1;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Migration stopped: {ex.Message}");
            return 1;
        }
    }

    private static Uri ValidateEndpoint(string value, bool table)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !(table
                ? Regex.IsMatch(uri.Host, @"^[a-z0-9]+\.table\.core\.windows\.net$") && uri.AbsolutePath == "/"
                : Regex.IsMatch(uri.Host, @"^[a-z0-9-]+\.documents\.azure\.com$") &&
                  Regex.IsMatch(uri.AbsolutePath, @"^/dbs/[^/]+/?$")))
        {
            throw new InvalidOperationException(table
                ? "Table endpoint must be https://<account>.table.core.windows.net."
                : "Cosmos endpoint must be https://<account>.documents.azure.com/dbs/<database>.");
        }
        return uri;
    }
}

internal sealed class AzureCliIdentity : IDisposable
{
    private readonly string configDir;
    private readonly string executable;
    private readonly Dictionary<string, (string Token, DateTimeOffset Expires)> tokens = new();

    public AzureCliIdentity(string? executable = null)
    {
        this.executable = executable ?? (OperatingSystem.IsWindows() ? "az.cmd" : "az");
        configDir = Path.Combine(Path.GetTempPath(), "openiddict-migration-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(configDir);
        else
            Directory.CreateDirectory(configDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public async Task LoginAsync()
    {
        await RunAzAsync("login", "--identity", "--allow-no-subscriptions", "--output", "none", "--only-show-errors");
    }

    public async Task<string> GetTokenAsync(string resource)
    {
        if (tokens.TryGetValue(resource, out var cached) && cached.Expires > DateTimeOffset.UtcNow.AddMinutes(5))
            return cached.Token;

        var output = await RunAzAsync("account", "get-access-token", "--resource", resource, "--output", "json", "--only-show-errors");
        try
        {
            using var json = JsonDocument.Parse(output);
            var token = json.RootElement.GetProperty("accessToken").GetString();
            var expires = DateTimeOffset.FromUnixTimeSeconds(json.RootElement.GetProperty("expires_on").GetInt64());
            if (string.IsNullOrWhiteSpace(token) || expires <= DateTimeOffset.UtcNow.AddMinutes(5))
                throw new InvalidOperationException("Azure CLI returned a missing or expired token.");
            tokens[resource] = (token, expires);
            return token;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException("Azure CLI returned an invalid token response.", ex);
        }
    }

    private async Task<string> RunAzAsync(params string[] args)
    {
        var start = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : executable,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        start.Environment["AZURE_CONFIG_DIR"] = configDir;
        if (OperatingSystem.IsWindows())
        {
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add(executable);
        }
        foreach (var arg in args) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Azure CLI failed to start.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await error;
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Azure CLI managed identity authentication failed.");
            return await output;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("Azure CLI could not be started. Install it on an Azure host with a system-assigned managed identity.");
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(configDir)) Directory.Delete(configDir, true);
    }
}

internal sealed class Migration(HttpClient http, Func<string, Task<string>> getToken, Uri table, Uri cosmos)
{
    private static readonly Dictionary<string, string[]> Fields = new()
    {
        ["applications"] = "ClientId ClientSecret ConsentType DisplayName DisplayNames Permissions PostLogoutRedirectUris Properties RedirectUris Requirements Type".Split(' '),
        ["scopes"] = "Description Descriptions DisplayName DisplayNames Name Properties Resources".Split(' '),
        ["authorizations"] = "ApplicationId CreationDate Properties Scopes Status Subject Type".Split(' '),
        ["tokens"] = "ApplicationId AuthorizationId CreationDate ExpirationDate Payload Properties RedemptionDate ReferenceId Status Subject Type".Split(' ')
    };
    private static readonly HashSet<string> Arrays = ["Permissions", "PostLogoutRedirectUris", "RedirectUris", "Requirements", "Resources", "Scopes"];
    private static readonly HashSet<string> Maps = ["Descriptions", "DisplayNames"];
    private static readonly HashSet<string> Dates = ["CreationDate", "ExpirationDate", "RedemptionDate"];
    private static readonly HashSet<string> Metadata = ["PartitionKey", "RowKey", "Timestamp", "ConcurrencyToken"];
    private readonly string database = cosmos.AbsoluteUri.TrimEnd('/');

    public async Task RunAsync(bool apply)
    {
        var ids = new Dictionary<string, HashSet<string>>();
        foreach (var (kind, _) in Fields)
        {
            var settings = JsonNode.Parse((await SendAsync(HttpMethod.Get, $"{database}/colls/openiddict.{kind}", false, $"Read {kind} container")).Body)?.AsObject()
                ?? throw new InvalidOperationException($"Invalid {kind} container response.");
            if (settings["partitionKey"]?["paths"] is not JsonArray paths || paths.Count != 1 || paths[0]?.GetValue<string>() != "/id")
                throw new InvalidOperationException($"{kind} container must be partitioned on /id.");
            if (settings["defaultTtl"] is not null) throw new InvalidOperationException($"{kind} container must have TTL disabled.");
            ids[kind] = new HashSet<string>(StringComparer.Ordinal);
            var number = 0;
            var missing = 0;
            await foreach (var row in ReadRowsAsync(kind))
            {
                var doc = ConvertRow(kind, row, ++number);
                var id = doc["id"]!.GetValue<string>();
                if (!ids[kind].Add(id)) throw new InvalidOperationException($"{kind}, source row {number}: duplicate PartitionKey.");
                foreach (var (field, parent) in new[] { ("applicationId", "applications"), ("authorizationId", "authorizations") })
                    if (doc[field] is JsonNode link && !ids[parent].Contains(link.GetValue<string>())) missing++;
            }
            Console.WriteLine($"{kind}: {number} validated, {missing} links to missing source records");
        }
        if (!apply)
        {
            Console.WriteLine("Dry run complete; nothing was written. Use --apply to copy.");
            return;
        }

        foreach (var kind in Fields.Keys)
        {
            var docs = $"{database}/colls/openiddict.{kind}/docs";
            var number = 0;
            var created = 0;
            var existing = 0;
            await foreach (var row in ReadRowsAsync(kind))
            {
                var doc = ConvertRow(kind, row, ++number);
                var id = doc["id"]!.GetValue<string>();
                if (!ids[kind].Remove(id)) throw new InvalidOperationException($"{kind}, source row {number}: source IDs changed after validation.");
                var operation = $"{kind}, source row {number}";
                var result = await SendAsync(HttpMethod.Post, docs, false, operation, doc, id, true);
                if (result.Status == HttpStatusCode.Conflict)
                {
                    var stored = JsonNode.Parse((await SendAsync(HttpMethod.Get, $"{docs}/{Uri.EscapeDataString(id)}", false, operation, id: id)).Body)?.AsObject()
                        ?? throw new InvalidOperationException($"{operation}: invalid destination document.");
                    foreach (var key in new[] { "_rid", "_self", "_etag", "_attachments", "_ts" }) stored.Remove(key);
                    if (!JsonNode.DeepEquals(stored, doc)) throw new InvalidOperationException($"{operation}: destination document differs.");
                    existing++;
                }
                else created++;
            }
            if (ids[kind].Count != 0) throw new InvalidOperationException($"{kind}: source IDs changed after validation.");
            Console.WriteLine($"{kind}: {created} created, {existing} already identical");
        }
    }

    private async IAsyncEnumerable<JsonObject> ReadRowsAsync(string kind)
    {
        var name = char.ToUpperInvariant(kind[0]) + kind[1..];
        var query = "";
        do
        {
            var result = await SendAsync(HttpMethod.Get, $"{table.GetLeftPart(UriPartial.Authority)}/OpenIddict{name}(){query}", true, $"Read {kind} table");
            var page = JsonNode.Parse(result.Body)?["value"] as JsonArray ?? throw new InvalidOperationException($"Invalid {kind} table response.");
            foreach (var row in page) yield return row?.AsObject() ?? throw new InvalidOperationException($"Invalid {kind} row.");
            query = result.Continuation.Count == 0 ? "" : "?" + string.Join("&", result.Continuation.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}"));
        } while (query.Length > 0);
    }

    internal static JsonObject ConvertRow(string kind, JsonObject row, int number)
    {
        var operation = $"{kind}, source row {number}";
        try
        {
            var id = row["PartitionKey"]?.GetValue<string>();
            if (string.IsNullOrEmpty(id) || Regex.IsMatch(id, @"[/\\?#\x00-\x1f\x7f]") || Encoding.UTF8.GetByteCount(id) > 1023)
                throw new InvalidOperationException($"{operation}: invalid Cosmos id.");
            var known = Fields[kind];
            if (row.Any(x => !known.Contains(x.Key) && !Metadata.Contains(x.Key) &&
                             !x.Key.StartsWith("odata.", StringComparison.Ordinal) &&
                             !x.Key.StartsWith("@odata.", StringComparison.Ordinal) &&
                             !x.Key.EndsWith("@odata.type", StringComparison.Ordinal)))
                throw new InvalidOperationException($"{operation}: unsupported source columns.");
            var doc = new JsonObject { ["id"] = id };
            foreach (var field in known)
            {
                if (row[field] is not JsonNode node) continue;
                try
                {
                    var text = node.GetValue<string>();
                    JsonNode? value;
                    if (Dates.Contains(field))
                    {
                        if (!text.EndsWith('Z') || !DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                            throw new FormatException();
                        value = JsonValue.Create(date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
                    }
                    else if (Arrays.Contains(field) || Maps.Contains(field) || field == "Properties")
                    {
                        value = JsonNode.Parse(text);
                        if (Arrays.Contains(field))
                        {
                            if (value is not JsonArray array || array.Any(x => x is null || x.GetValueKind() != JsonValueKind.String)) throw new FormatException();
                        }
                        else if (value is not JsonObject obj || (Maps.Contains(field) && obj.Any(x => x.Value is null || x.Value.GetValueKind() != JsonValueKind.String)))
                            throw new FormatException();
                    }
                    else value = node.DeepClone();
                    var name = kind == "applications" && field == "Type" ? "clientType" : char.ToLowerInvariant(field[0]) + field[1..];
                    doc[name] = value;
                }
                catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException)
                {
                    throw new InvalidOperationException($"{operation}: invalid {field}.");
                }
            }
            return doc;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or JsonException)
        {
            if (ex is InvalidOperationException && ex.Message.StartsWith(operation, StringComparison.Ordinal)) throw;
            throw new InvalidOperationException($"{operation}: invalid source row.");
        }
    }

    private async Task<(HttpStatusCode Status, string Body, Dictionary<string, string> Continuation)> SendAsync(
        HttpMethod method, string uri, bool isTable, string operation, JsonObject? document = null, string? id = null, bool allowConflict = false)
    {
        var token = await getToken(isTable ? "https://storage.azure.com/" : "https://cosmos.azure.com/");
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.TryAddWithoutValidation("x-ms-date", DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture));
            request.Headers.TryAddWithoutValidation("x-ms-version", isTable ? "2019-02-02" : "2018-12-31");
            if (isTable)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.TryAddWithoutValidation("Accept", "application/json;odata=fullmetadata");
            }
            else
            {
                request.Headers.TryAddWithoutValidation("Authorization", Uri.EscapeDataString($"type=aad&ver=1.0&sig={token}"));
                if (id is not null) request.Headers.TryAddWithoutValidation("x-ms-documentdb-partitionkey", JsonSerializer.Serialize(new[] { id }));
                if (document is not null) request.Content = new StringContent(document.ToJsonString(), Encoding.UTF8, "application/json");
            }
            using var response = await http.SendAsync(request);
            if (response.IsSuccessStatusCode || (allowConflict && response.StatusCode == HttpStatusCode.Conflict))
            {
                var continuation = new Dictionary<string, string>();
                foreach (var key in new[] { "NextPartitionKey", "NextRowKey" })
                    if (response.Headers.TryGetValues($"x-ms-continuation-{key}", out var values)) continuation[key] = values.First();
                return (response.StatusCode, await response.Content.ReadAsStringAsync(), continuation);
            }
            if ((int)response.StatusCode != 429 || attempt >= 5)
                throw new InvalidOperationException($"{operation} failed (HTTP {(int)response.StatusCode}).");
            var delay = response.Headers.TryGetValues("x-ms-retry-after-ms", out var retries) &&
                        int.TryParse(retries.FirstOrDefault(), out var parsed) && parsed > 0 ? parsed : 1000 * (1 << attempt);
            await Task.Delay(delay);
        }
    }
}
