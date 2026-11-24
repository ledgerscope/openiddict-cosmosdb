using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Azure.Data.Tables;

namespace Ledgerscope.OpenIddict.MigrateAts;

internal sealed partial class Migration(IRowSource source, IDocumentStore destination, TextWriter output)
{
    private static readonly Dictionary<string, string[]> Fields = new()
    {
        ["applications"] = ["ClientId", "ClientSecret", "ConsentType", "DisplayName", "DisplayNames", "Permissions", "PostLogoutRedirectUris", "Properties", "RedirectUris", "Requirements", "Type"],
        ["scopes"] = ["Description", "Descriptions", "DisplayName", "DisplayNames", "Name", "Properties", "Resources"],
        ["authorizations"] = ["ApplicationId", "CreationDate", "Properties", "Scopes", "Status", "Subject", "Type"],
        ["tokens"] = ["ApplicationId", "AuthorizationId", "CreationDate", "ExpirationDate", "Payload", "Properties", "RedemptionDate", "ReferenceId", "Status", "Subject", "Type"]
    };

    private static readonly HashSet<string> Arrays = ["Permissions", "PostLogoutRedirectUris", "RedirectUris", "Requirements", "Resources", "Scopes"];
    private static readonly HashSet<string> Maps = ["Descriptions", "DisplayNames"];
    private static readonly HashSet<string> Dates = ["CreationDate", "ExpirationDate", "RedemptionDate"];
    private static readonly HashSet<string> Metadata = ["PartitionKey", "RowKey", "Timestamp", "ConcurrencyToken"];

    public static string TableName(string kind) => "OpenIddict" + char.ToUpperInvariant(kind[0]) + kind[1..];

    public static string ContainerName(string kind) => "openiddict" + kind;

    public async Task RunAsync(bool apply, CancellationToken cancellationToken)
    {
        var ids = new Dictionary<string, HashSet<string>>();
        foreach (var kind in Fields.Keys)
        {
            await destination.ValidateContainerAsync(kind, cancellationToken);
            var kindIds = ids[kind] = new HashSet<string>(StringComparer.Ordinal);
            var number = 0;
            var dangling = 0;
            await foreach (var row in source.ReadAsync(kind, cancellationToken))
            {
                var document = ConvertRow(kind, row, ++number);
                if (!kindIds.Add(Id(document)))
                    throw new InvalidOperationException($"{kind}, source row {number}: duplicate PartitionKey.");
                foreach (var (field, parent) in new[] { ("applicationId", "applications"), ("authorizationId", "authorizations") })
                {
                    if (document[field] is JsonNode link && !ids[parent].Contains(link.GetValue<string>())) dangling++;
                }
            }
            output.WriteLine($"{kind}: {number} validated, {dangling} links to missing source records");
        }

        if (!apply)
        {
            output.WriteLine("Dry run complete; nothing was written. Use --apply to copy.");
            return;
        }

        foreach (var kind in Fields.Keys)
        {
            var remaining = ids[kind];
            var number = 0;
            var created = 0;
            var identical = 0;
            await foreach (var row in source.ReadAsync(kind, cancellationToken))
            {
                var document = ConvertRow(kind, row, ++number);
                if (!remaining.Remove(Id(document)))
                    throw new InvalidOperationException($"{kind}, source row {number}: source changed after validation.");
                if (await destination.CreateAsync(kind, document, cancellationToken)) created++;
                else identical++;
            }
            if (remaining.Count != 0)
                throw new InvalidOperationException($"{kind}: source changed after validation.");
            output.WriteLine($"{kind}: {created} created, {identical} already identical");
        }
    }

    private static string Id(JsonObject document) => document["id"]!.GetValue<string>();

    /// <summary>Converts an ATS row to a Cosmos document. Messages never include row contents.</summary>
    internal static JsonObject ConvertRow(string kind, TableEntity row, int number)
    {
        var operation = $"{kind}, source row {number}";
        var id = row.PartitionKey;
        if (string.IsNullOrEmpty(id) || InvalidId().IsMatch(id) || Encoding.UTF8.GetByteCount(id) > 1023)
            throw new InvalidOperationException($"{operation}: invalid Cosmos id.");

        var known = Fields[kind];
        if (row.Keys.Any(k => !known.Contains(k) && !Metadata.Contains(k) &&
                              !k.StartsWith("odata.", StringComparison.Ordinal) &&
                              !k.StartsWith("@odata.", StringComparison.Ordinal) &&
                              !k.EndsWith("@odata.type", StringComparison.Ordinal)))
            throw new InvalidOperationException($"{operation}: unsupported source columns.");

        var document = new JsonObject { ["id"] = id };
        foreach (var field in known)
        {
            if (!row.TryGetValue(field, out var raw) || raw is null) continue;
            try
            {
                var name = kind == "applications" && field == "Type" ? "clientType" : char.ToLowerInvariant(field[0]) + field[1..];
                document[name] = ConvertValue(field, row.GetString(field));
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or JsonException)
            {
                throw new InvalidOperationException($"{operation}: invalid {field}.");
            }
        }
        return document;
    }

    private static JsonNode? ConvertValue(string field, string text)
    {
        if (Dates.Contains(field))
        {
            // Preserve the stored 7-digit precision; DateTimeOffset round-trips it exactly.
            if (!text.EndsWith('Z') || !DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new FormatException();
            return date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        }

        if (!Arrays.Contains(field) && !Maps.Contains(field) && field != "Properties") return text;

        var value = JsonNode.Parse(text);
        var valid = field switch
        {
            _ when Arrays.Contains(field) => value is JsonArray array && array.All(IsString),
            _ when Maps.Contains(field) => value is JsonObject map && map.All(p => IsString(p.Value)),
            _ => value is JsonObject,
        };
        return valid ? value : throw new FormatException();
    }

    private static bool IsString(JsonNode? node) => node?.GetValueKind() == JsonValueKind.String;

    [GeneratedRegex(@"[/\\?#\x00-\x1f\x7f]")]
    private static partial Regex InvalidId();
}
