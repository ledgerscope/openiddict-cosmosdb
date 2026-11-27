using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Azure.Cosmos;

namespace Ledgerscope.OpenIddict.MigrateAts;

internal static partial class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--apply"))
        {
            Console.Error.WriteLine("Usage: MigrateAts <table endpoint> <Cosmos database endpoint> [--apply]");
            return 1;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var tableEndpoint = ParseTableEndpoint(args[0]);
            var (cosmosEndpoint, databaseName) = ParseCosmosEndpoint(args[1]);

            TokenCredential credential = new DefaultAzureCredential();
            using var cosmos = new CosmosClient(cosmosEndpoint.AbsoluteUri, credential, new CosmosClientOptions
            {
                UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions()
            });

            var migration = new Migration(
                new TableRowSource(new TableServiceClient(tableEndpoint, credential)),
                new CosmosDocumentStore(cosmos.GetDatabase(databaseName)),
                Console.Out);
            await migration.RunAsync(apply: args.Length == 2, cancellation.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Migration cancelled.");
            return 1;
        }
        catch (Exception ex) when (ex is RequestFailedException or CosmosException or AuthenticationFailedException or HttpRequestException)
        {
            // Service exceptions can echo request details, so report only the type.
            Console.Error.WriteLine($"Migration stopped: a service request failed ({ex.GetType().Name}).");
            return 1;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"Migration stopped: {ex.Message}");
            return 1;
        }
    }

    internal static Uri ParseTableEndpoint(string value)
    {
        if (!TryParseHttps(value, out var uri) || !TableHost().IsMatch(uri.Host) || uri.AbsolutePath != "/")
            throw new ArgumentException("Table endpoint must be https://<account>.table.core.windows.net.");
        return uri;
    }

    internal static (Uri Account, string Database) ParseCosmosEndpoint(string value)
    {
        if (!TryParseHttps(value, out var uri) || !CosmosHost().IsMatch(uri.Host) ||
            CosmosPath().Match(uri.AbsolutePath) is not { Success: true } match)
            throw new ArgumentException("Cosmos endpoint must be https://<account>.documents.azure.com/dbs/<database>.");
        return (new Uri($"https://{uri.Host}/"), Uri.UnescapeDataString(match.Groups[1].Value));
    }

    private static bool TryParseHttps(string value, out Uri uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri!) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    [GeneratedRegex("^[a-z0-9]+\\.table\\.core\\.windows\\.net$")]
    private static partial Regex TableHost();

    [GeneratedRegex("^[a-z0-9-]+\\.documents\\.azure\\.com$")]
    private static partial Regex CosmosHost();

    [GeneratedRegex("^/dbs/([^/]+)/?$")]
    private static partial Regex CosmosPath();
}
