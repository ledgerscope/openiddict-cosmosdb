/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * Derived from OpenIddict (https://github.com/openiddict/openiddict-core) and modified by Ledgerscope.
 */

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using static OpenIddict.Abstractions.OpenIddictExceptions;

namespace Ledgerscope.OpenIddict.CosmosDb;

/// <summary>
/// Exposes common helpers used by the Azure Cosmos DB stores.
/// </summary>
/// <remarks>
/// Note: to ensure the documents are always persisted using the same format independently of the
/// serializer attached to the <see cref="CosmosClient"/> by the application, the stores exclusively
/// use the stream-based APIs and handle the (de)serialization using System.Text.Json.
/// </remarks>
internal static class OpenIddictCosmosDbHelpers
{
    /// <summary>
    /// Gets the maximum number of identifiers included in a single query parameter.
    /// </summary>
    public const int MaximumChunkSize = 1_000;

    /// <summary>
    /// Gets the maximum number of documents removed by a single prune operation.
    /// </summary>
    public const int MaximumPrunedDocuments = 1_000_000;

    /// <summary>
    /// Gets the date format used to persist dates, that guarantees ordinal string comparisons
    /// produce the same results as chronological comparisons (as recommended by Azure Cosmos DB).
    /// </summary>
    public const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    /// <summary>
    /// Gets the JSON serializer options used to serialize and deserialize the documents.
    /// </summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        Converters = { new UtcDateTimeConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    /// <summary>
    /// Gets the LINQ serializer options used to translate LINQ queries. Note: the property names
    /// used by the OpenIddict models are always the camel-cased version of the CLR property names.
    /// </summary>
    public static CosmosLinqSerializerOptions LinqSerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
    };

    /// <summary>
    /// Ensures the specified entity identifier is not null or empty.
    /// </summary>
    /// <exception cref="ArgumentException">The identifier is null or empty.</exception>
    public static string EnsureIdentifier(string? identifier, string parameterName)
        => !string.IsNullOrEmpty(identifier) ? identifier :
            throw new ArgumentException(SR.GetResourceString(SR.ID0195), parameterName);

    /// <summary>
    /// Formats the specified date using the format used to persist dates.
    /// </summary>
    public static string FormatDate(DateTime date) => Normalize(date).ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets a LINQ queryable for the specified container.
    /// </summary>
    public static IQueryable<T> GetQueryable<T>(this Container container) => container.GetItemLinqQueryable<T>(
        allowSynchronousQueryExecution: false,
        continuationToken: null,
        requestOptions: null,
        linqSerializerOptions: LinqSerializerOptions);

    /// <summary>
    /// Creates a new query definition using the specified SQL query and parameters.
    /// </summary>
    public static QueryDefinition CreateQuery(string query, params ReadOnlySpan<(string Name, object? Value)> parameters)
    {
        var definition = new QueryDefinition(query);

        foreach (var (name, value) in parameters)
        {
            definition = definition.WithParameter(name, value);
        }

        return definition;
    }

    /// <summary>
    /// Creates a new query definition using the specified projection, filters and parameters.
    /// </summary>
    public static QueryDefinition CreateQuery(string projection,
        IReadOnlyList<string> filters, IReadOnlyList<(string Name, object? Value)> parameters)
    {
        var definition = new QueryDefinition(filters.Count is 0 ? projection :
            projection + " WHERE " + string.Join(" AND ", filters));

        foreach (var (name, value) in parameters)
        {
            definition = definition.WithParameter(name, value);
        }

        return definition;
    }

    /// <summary>
    /// Creates a new query definition returning the documents ordered by identifier,
    /// optionally skipping and limiting the number of documents returned.
    /// </summary>
    public static QueryDefinition CreatePagedQuery(int? count, int? offset)
    {
        if (count is null && offset is null)
        {
            return new QueryDefinition("SELECT * FROM c ORDER BY c.id");
        }

        // Note: Azure Cosmos DB requires both OFFSET and LIMIT to be specified.
        return new QueryDefinition("SELECT * FROM c ORDER BY c.id OFFSET @offset LIMIT @count")
            .WithParameter("@offset", offset ?? 0)
            .WithParameter("@count", count ?? int.MaxValue);
    }

    /// <summary>
    /// Creates a new document in the specified container.
    /// </summary>
    /// <returns>The entity tag associated with the new document.</returns>
    public static async ValueTask<string> CreateDocumentAsync<T>(
        this Container container, T document, string identifier, CancellationToken cancellationToken)
    {
        using var stream = Serialize(document);
        using var response = await container.CreateItemStreamAsync(stream,
            new PartitionKey(identifier), requestOptions: null, cancellationToken);

        response.EnsureSuccessStatusCode();

        return response.Headers.ETag;
    }

    /// <summary>
    /// Deletes the document corresponding to the specified identifier and entity tag.
    /// </summary>
    /// <exception cref="ConcurrencyException">The document was concurrently updated or removed.</exception>
    public static async ValueTask DeleteDocumentAsync(this Container container,
        string identifier, string? tag, string message, CancellationToken cancellationToken)
    {
        using var response = await container.DeleteItemStreamAsync(identifier, new PartitionKey(identifier),
            new ItemRequestOptions { IfMatchEtag = tag }, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed)
        {
            throw new ConcurrencyException(message);
        }

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Removes all the documents whose identifiers are returned by the specified query.
    /// </summary>
    /// <returns>The number of documents that were removed.</returns>
    public static async ValueTask<long> DeleteDocumentsAsync(this Container container,
        QueryDefinition query, CancellationToken cancellationToken)
    {
        var identifiers = new List<string>();

        await foreach (var identifier in container.QueryAsync<string>(query, cancellationToken))
        {
            identifiers.Add(identifier);
        }

        return await container.DeleteDocumentsAsync(identifiers, cancellationToken);
    }

    /// <summary>
    /// Removes all the documents corresponding to the specified identifiers.
    /// </summary>
    /// <returns>The number of documents that were removed.</returns>
    public static async ValueTask<long> DeleteDocumentsAsync(this Container container,
        IReadOnlyList<string> identifiers, CancellationToken cancellationToken)
    {
        var result = 0L;

        // Note: Azure Cosmos DB doesn't support deleting multiple documents spread across different logical
        // partitions in a single operation. To reduce the overall latency, the delete operations are executed
        // concurrently in small batches (which also allows the SDK to group them when bulk execution is enabled).
        foreach (var chunk in Chunk(identifiers, 100))
        {
            var results = await Task.WhenAll(chunk.Select(async identifier =>
            {
                using var response = await container.DeleteItemStreamAsync(identifier,
                    new PartitionKey(identifier), requestOptions: null, cancellationToken);

                // Note: documents concurrently removed by another operation are deliberately ignored.
                if (response.StatusCode is HttpStatusCode.NotFound)
                {
                    return false;
                }

                response.EnsureSuccessStatusCode();

                return true;
            }));

            result += results.Count(static result => result);
        }

        return result;
    }

    /// <summary>
    /// Updates the status of all the documents whose identifiers are returned by the specified query.
    /// </summary>
    /// <returns>The number of documents that were updated.</returns>
    public static async ValueTask<long> UpdateStatusAsync(this Container container,
        QueryDefinition query, string status, CancellationToken cancellationToken)
    {
        var identifiers = new List<string>();

        await foreach (var identifier in container.QueryAsync<string>(query, cancellationToken))
        {
            identifiers.Add(identifier);
        }

        var result = 0L;

        // Note: partial document updates are used to avoid having to retrieve and replace the
        // entire documents. Since these operations don't use optimistic concurrency checks,
        // they can be safely executed concurrently without risking concurrency exceptions.
        foreach (var chunk in Chunk(identifiers, 100))
        {
            var results = await Task.WhenAll(chunk.Select(async identifier =>
            {
                using var response = await container.PatchItemStreamAsync(identifier, new PartitionKey(identifier),
                    [PatchOperation.Set("/status", status)], requestOptions: null, cancellationToken);

                // Note: documents concurrently removed by another operation are deliberately ignored.
                if (response.StatusCode is HttpStatusCode.NotFound)
                {
                    return false;
                }

                response.EnsureSuccessStatusCode();

                return true;
            }));

            result += results.Count(static result => result);
        }

        return result;
    }

    /// <summary>
    /// Executes the specified query and returns the first result, if applicable.
    /// </summary>
    public static async ValueTask<T?> FirstOrDefaultAsync<T>(this Container container,
        QueryDefinition query, CancellationToken cancellationToken)
    {
        await foreach (var result in container.QueryAsync<T>(query, cancellationToken))
        {
            return result;
        }

        return default;
    }

    /// <summary>
    /// Executes the specified LINQ query and returns the results.
    /// </summary>
    public static IAsyncEnumerable<T> QueryAsync<T>(this Container container,
        IQueryable<T> query, CancellationToken cancellationToken)
        => container.QueryAsync<T>(query.ToQueryDefinition(), cancellationToken);

    /// <summary>
    /// Executes the specified query and returns the results.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The deserialized types are preserved by the stores.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The deserialized types are preserved by the stores.")]
    public static async IAsyncEnumerable<T> QueryAsync<T>(this Container container,
        QueryDefinition query, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var iterator = container.GetItemQueryStreamIterator(query);

        while (iterator.HasMoreResults)
        {
            using var response = await iterator.ReadNextAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            using var document = await JsonDocument.ParseAsync(response.Content, cancellationToken: cancellationToken);

            foreach (var element in document.RootElement.GetProperty("Documents").EnumerateArray())
            {
                yield return element.Deserialize<T>(SerializerOptions)!;
            }
        }
    }

    /// <summary>
    /// Retrieves the document corresponding to the specified identifier.
    /// </summary>
    /// <returns>The document, or <see langword="null"/> if it couldn't be found.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The deserialized types are preserved by the stores.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The deserialized types are preserved by the stores.")]
    public static async ValueTask<T?> ReadDocumentAsync<T>(this Container container,
        string identifier, CancellationToken cancellationToken) where T : class
    {
        using var response = await container.ReadItemStreamAsync(identifier,
            new PartitionKey(identifier), requestOptions: null, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await JsonSerializer.DeserializeAsync<T>(response.Content, SerializerOptions, cancellationToken);
    }

    /// <summary>
    /// Replaces the document corresponding to the specified identifier, if its entity tag matches the specified tag.
    /// </summary>
    /// <returns>The entity tag associated with the updated document.</returns>
    /// <exception cref="ConcurrencyException">The document was concurrently updated or removed.</exception>
    public static async ValueTask<string> ReplaceDocumentAsync<T>(this Container container,
        T document, string identifier, string? tag, string message, CancellationToken cancellationToken)
    {
        using var stream = Serialize(document);
        using var response = await container.ReplaceItemStreamAsync(stream, identifier, new PartitionKey(identifier),
            new ItemRequestOptions { IfMatchEtag = tag }, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed)
        {
            throw new ConcurrencyException(message);
        }

        response.EnsureSuccessStatusCode();

        return response.Headers.ETag;
    }

    /// <summary>
    /// Converts the specified JSON object to a dictionary of properties.
    /// </summary>
    public static ImmutableDictionary<string, JsonElement> GetProperties(JsonElement? properties)
    {
        if (properties is not { ValueKind: JsonValueKind.Object } element)
        {
            return ImmutableDictionary.Create<string, JsonElement>(StringComparer.Ordinal);
        }

        var builder = ImmutableDictionary.CreateBuilder<string, JsonElement>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            builder[property.Name] = property.Value.Clone();
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Converts the specified dictionary of properties to a JSON object.
    /// </summary>
    public static JsonElement? CreateProperties(ImmutableDictionary<string, JsonElement> properties)
    {
        if (properties is not { IsEmpty: false })
        {
            return null;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = false
        }))
        {
            writer.WriteStartObject();

            foreach (var property in properties)
            {
                writer.WritePropertyName(property.Key);
                property.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Splits the specified list into chunks of the specified size.
    /// </summary>
    public static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (var index = 0; index < source.Count; index += size)
        {
            var chunk = new List<T>(Math.Min(size, source.Count - index));

            for (var offset = index; offset < index + size && offset < source.Count; offset++)
            {
                chunk.Add(source[offset]);
            }

            yield return chunk;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The serialized types are preserved by the stores.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "The serialized types are preserved by the stores.")]
    private static MemoryStream Serialize<T>(T document)
    {
        var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, document, SerializerOptions);
        stream.Position = 0;

        return stream;
    }

    private static DateTime Normalize(DateTime date) => date.Kind switch
    {
        DateTimeKind.Utc         => date,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(date, DateTimeKind.Utc),
        _                        => date.ToUniversalTime()
    };

    /// <summary>
    /// Represents a JSON converter that serializes dates using a fixed-length UTC format.
    /// </summary>
    private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
            => writer.WriteStringValue(FormatDate(value));
    }
}
