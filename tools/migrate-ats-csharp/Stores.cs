using System.Net;
using System.Text.Json.Nodes;
using Azure.Data.Tables;
using Microsoft.Azure.Cosmos;

namespace Ledgerscope.OpenIddict.MigrateAts;

internal interface IRowSource
{
    IAsyncEnumerable<TableEntity> ReadAsync(string kind, CancellationToken cancellationToken);
}

internal interface IDocumentStore
{
    /// <summary>Throws if the destination container is missing or configured unsafely.</summary>
    Task ValidateContainerAsync(string kind, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the document. Returns false if an identical document already exists and
    /// throws if a different one does. Existing documents are never modified.
    /// </summary>
    Task<bool> CreateAsync(string kind, JsonObject document, CancellationToken cancellationToken);
}

internal sealed class TableRowSource(TableServiceClient service) : IRowSource
{
    public IAsyncEnumerable<TableEntity> ReadAsync(string kind, CancellationToken cancellationToken) =>
        service.GetTableClient(Migration.TableName(kind)).QueryAsync<TableEntity>(cancellationToken: cancellationToken);
}

internal sealed class CosmosDocumentStore(Database database) : IDocumentStore
{
    private static readonly string[] SystemProperties = ["_rid", "_self", "_etag", "_attachments", "_ts"];

    public async Task ValidateContainerAsync(string kind, CancellationToken cancellationToken)
    {
        var response = await database.GetContainer(Migration.ContainerName(kind)).ReadContainerAsync(cancellationToken: cancellationToken);
        var properties = response.Resource;
        if (properties.PartitionKeyPath != "/id")
            throw new InvalidOperationException($"{kind} container must be partitioned on /id.");
        if (properties.DefaultTimeToLive is not null)
            throw new InvalidOperationException($"{kind} container must have TTL disabled.");
    }

    public async Task<bool> CreateAsync(string kind, JsonObject document, CancellationToken cancellationToken)
    {
        var container = database.GetContainer(Migration.ContainerName(kind));
        var id = document["id"]!.GetValue<string>();
        try
        {
            await container.CreateItemAsync(document, new PartitionKey(id), cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            var stored = (await container.ReadItemAsync<JsonObject>(id, new PartitionKey(id), cancellationToken: cancellationToken)).Resource;
            foreach (var property in SystemProperties) stored.Remove(property);
            if (!JsonNode.DeepEquals(stored, document))
                throw new InvalidOperationException($"{kind}: destination document differs from the source.");
            return false;
        }
    }
}
