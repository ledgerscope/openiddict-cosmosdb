/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Moq;

namespace Ledgerscope.OpenIddict.CosmosDb.Tests;

/// <summary>
/// Provides an in-memory test harness exposing mocked Azure Cosmos DB containers,
/// that records the requests sent by the stores and returns preconfigured responses.
/// </summary>
public sealed class CosmosDbTestHarness : IDisposable
{
    // Note: this is the well-known, publicly documented key of the Azure Cosmos DB emulator. It is only used to
    // instantiate a client used to translate LINQ queries: no request is ever sent using this client.
    private const string EmulatorConnectionString = "AccountEndpoint=https://localhost:8081/;AccountKey=" +
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private readonly Dictionary<string, Mock<Container>> _containers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<string[]>> _results = new(StringComparer.Ordinal);
    private readonly CosmosClient _client = new(EmulatorConnectionString);

    public CosmosDbTestHarness()
    {
        var database = new Mock<Database>();
        database.Setup(mock => mock.GetContainer(It.IsAny<string>()))
            .Returns((string name) => GetContainer(name).Object);

        Context = Mock.Of<IOpenIddictCosmosDbContext>(mock =>
            mock.GetDatabaseAsync(It.IsAny<CancellationToken>()) == new ValueTask<Database>(database.Object));

        Options = Mock.Of<IOptionsMonitor<OpenIddictCosmosDbOptions>>(
            mock => mock.CurrentValue == new OpenIddictCosmosDbOptions());
    }

    public IOpenIddictCosmosDbContext Context { get; }

    public IOptionsMonitor<OpenIddictCosmosDbOptions> Options { get; }

    public List<(string Container, QueryDefinition Query)> Queries { get; } = [];

    public List<(string Container, string Identifier, PartitionKey PartitionKey, string Body, string? IfMatch)> Writes { get; } = [];

    public List<(string Container, string Identifier, string? IfMatch)> Deletes { get; } = [];

    public List<(string Container, string Identifier, IReadOnlyList<PatchOperation> Operations)> Patches { get; } = [];

    /// <summary>
    /// Gets or sets the status code returned for replace and delete operations.
    /// </summary>
    public HttpStatusCode WriteStatusCode { get; set; } = HttpStatusCode.OK;

    /// <summary>
    /// Gets or sets the documents returned for point reads, indexed by identifier.
    /// </summary>
    public Dictionary<string, string> Documents { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Enqueues the results returned by the next query executed against the specified container.
    /// </summary>
    public void EnqueueResults(string container, params string[] documents)
    {
        if (!_results.TryGetValue(container, out var queue))
        {
            queue = _results[container] = new Queue<string[]>();
        }

        queue.Enqueue(documents);
    }

    public Mock<Container> GetContainer(string name)
    {
        if (_containers.TryGetValue(name, out var container))
        {
            return container;
        }

        container = new Mock<Container>();
        _containers[name] = container;

        var real = _client.GetContainer("database", name);

        container.Setup(mock => mock.GetItemQueryStreamIterator(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns((QueryDefinition query, string _, QueryRequestOptions _) =>
            {
                Queries.Add((name, query));

                var documents = _results.TryGetValue(name, out var queue) && queue.Count is not 0 ? queue.Dequeue() : [];
                return CreateIterator(documents);
            });

        container.Setup(mock => mock.CreateItemStreamAsync(
                It.IsAny<Stream>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Returns((Stream stream, PartitionKey key, ItemRequestOptions _, CancellationToken _) =>
            {
                var body = Read(stream);
                using var document = JsonDocument.Parse(body);
                Writes.Add((name, document.RootElement.GetProperty("id").GetString()!, key, body, null));

                return Task.FromResult(CreateResponse(HttpStatusCode.Created, etag: "\"etag-1\""));
            });

        container.Setup(mock => mock.ReplaceItemStreamAsync(It.IsAny<Stream>(), It.IsAny<string>(),
                It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Returns((Stream stream, string identifier, PartitionKey key, ItemRequestOptions options, CancellationToken _) =>
            {
                Writes.Add((name, identifier, key, Read(stream), options?.IfMatchEtag));

                return Task.FromResult(CreateResponse(WriteStatusCode, etag: "\"etag-2\""));
            });

        container.Setup(mock => mock.DeleteItemStreamAsync(It.IsAny<string>(),
                It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Returns((string identifier, PartitionKey _, ItemRequestOptions options, CancellationToken _) =>
            {
                Deletes.Add((name, identifier, options?.IfMatchEtag));

                return Task.FromResult(CreateResponse(WriteStatusCode is HttpStatusCode.OK ? HttpStatusCode.NoContent : WriteStatusCode));
            });

        container.Setup(mock => mock.PatchItemStreamAsync(It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<PatchItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Returns((string identifier, PartitionKey _, IReadOnlyList<PatchOperation> operations,
                PatchItemRequestOptions _, CancellationToken _) =>
            {
                Patches.Add((name, identifier, operations));

                return Task.FromResult(CreateResponse(HttpStatusCode.OK));
            });

        container.Setup(mock => mock.ReadItemStreamAsync(It.IsAny<string>(),
                It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Returns((string identifier, PartitionKey _, ItemRequestOptions _, CancellationToken _) =>
                Task.FromResult(Documents.TryGetValue(identifier, out var document)
                    ? CreateResponse(HttpStatusCode.OK, document)
                    : CreateResponse(HttpStatusCode.NotFound)));

        // Note: LINQ queries are translated using a real container instance, which doesn't require sending any request.
        SetupLinq<Models.OpenIddictCosmosDbApplication>(container, real);
        SetupLinq<Models.OpenIddictCosmosDbAuthorization>(container, real);
        SetupLinq<Models.OpenIddictCosmosDbScope>(container, real);
        SetupLinq<Models.OpenIddictCosmosDbToken>(container, real);

        return container;
    }

    public void Dispose() => _client.Dispose();

    private static void SetupLinq<T>(Mock<Container> container, Container real)
        => container.Setup(mock => mock.GetItemLinqQueryable<T>(It.IsAny<bool>(), It.IsAny<string>(),
                It.IsAny<QueryRequestOptions>(), It.IsAny<CosmosLinqSerializerOptions>()))
            .Returns((bool synchronous, string token, QueryRequestOptions options, CosmosLinqSerializerOptions serializer) =>
                real.GetItemLinqQueryable<T>(synchronous, token, options, serializer));

    private static FeedIterator CreateIterator(string[] documents)
    {
        var iterator = new Mock<FeedIterator>();
        iterator.SetupSequence(mock => mock.HasMoreResults).Returns(true).Returns(false);
        iterator.Setup(mock => mock.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var builder = new StringBuilder("{\"Documents\":[");

                for (var index = 0; index < documents.Length; index++)
                {
                    builder.Append(index is 0 ? string.Empty : ",").Append(documents[index]);
                }

                return CreateResponse(HttpStatusCode.OK, builder.Append("]}").ToString());
            });

        return iterator.Object;
    }

    private static ResponseMessage CreateResponse(HttpStatusCode code, string? content = null, string? etag = null)
    {
        var response = new ResponseMessage(code);

        if (content is not null)
        {
            response.Content = new MemoryStream(Encoding.UTF8.GetBytes(content));
        }

        if (etag is not null)
        {
            response.Headers.Add("etag", etag);
        }

        return response;
    }

    private static string Read(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        return reader.ReadToEnd();
    }
}
