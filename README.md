# Ledgerscope.OpenIddict.CosmosDb

Azure Cosmos DB (NoSQL API) stores for [OpenIddict](https://github.com/openiddict/openiddict-core) 7.x, built on the
official `Microsoft.Azure.Cosmos` SDK. Maintained by Ledgerscope; this is not an official OpenIddict package.

| Package | Contents |
| --- | --- |
| `Ledgerscope.OpenIddict.CosmosDb` | Application, authorization, scope and token stores, DI registration |
| `Ledgerscope.OpenIddict.CosmosDb.Models` | The document models (depends only on System.Text.Json) |

Supported: OpenIddict 7.5.0 and later 7.x versions, on .NET 10.

## Usage

```csharp
services.AddSingleton(new CosmosClient(connectionString));

services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseCosmosDb()
               .UseDatabase(cosmosClient.GetDatabase("auth"));
    });
```

If `UseDatabase()` isn't called, the stores resolve a `Microsoft.Azure.Cosmos.Database` from the DI container.

## Containers

The stores don't create containers. Create one container per entity type, **partitioned on `/id`**:

| Entity | Default container name | Rename with |
| --- | --- | --- |
| Applications | `openiddict.applications` | `SetApplicationsContainerName()` |
| Authorizations | `openiddict.authorizations` | `SetAuthorizationsContainerName()` |
| Scopes | `openiddict.scopes` | `SetScopesContainerName()` |
| Tokens | `openiddict.tokens` | `SetTokensContainerName()` |

The default indexing policy (index all paths) is sufficient.

## Migrating from Azure Table Storage

The [ATS migration script](tools/migrate-ats/README.md) copies applications, scopes, authorizations and tokens from [ledgerscope/openiddict-ats](https://github.com/ledgerscope/openiddict-ats) to the containers above. It validates without writing by default.

## Design notes

- **Serialization**: documents are always read and written through the SDK's stream APIs using System.Text.Json with
  fixed settings, so the stored format doesn't depend on the serializer configured on your `CosmosClient`.
  Properties are camel-cased (`clientId`, `applicationId`), which matches what Cosmos LINQ generates, so the
  `GetAsync`/`ListAsync`/`CountAsync` overloads that accept LINQ queries work.
- **Dates** are stored in a fixed-length UTC format (`yyyy-MM-ddTHH:mm:ss.fffffffZ`) so string comparisons in queries
  are chronological. Dates used inside your own LINQ queries are formatted by the SDK serializer instead, which may not
  compare correctly against stored values.
- **Concurrency**: `ConcurrencyToken` maps to the Cosmos `_etag`; updates and deletes are conditional on it and raise
  OpenIddict's `ConcurrencyException` on a mismatch.
- **Lookups by id** are point reads. Lookups by client id, subject or reference id are parameterized cross-partition
  queries.
- **Revocation** uses partial document updates (`/status` only).
- **Cascade deletes and pruning**: Cosmos has no joins and can't delete query results, so the stores resolve matching
  identifiers first and then delete documents individually, in concurrent batches of 100. These operations are not
  atomic. Enable `AllowBulkExecution` on the `CosmosClient` to let the SDK group the requests.

## Building and testing

```bash
dotnet test
```

The unit tests use mocked containers and need no database. Two integration tests run against a real Cosmos DB account
or emulator when `OPENIDDICT_COSMOSDB_CONNECTION_STRING` is set; each run creates and deletes a temporary database.

## Continuous integration and releases

The [build workflow](.github/workflows/build.yml) has two jobs:

1. **Test and pack** runs on every push to any branch and on pull requests to `main`: it builds, runs the
   tests and packs both packages, uploading the test results and packages as workflow artifacts.
2. **Publish to GitHub Packages** runs only after the tests pass, on pushes (not pull requests), and pushes
   the packages to `https://nuget.pkg.github.com/ledgerscope/index.json`.

| Trigger | Package version | Example |
| --- | --- | --- |
| Push to `main` | `{major}.{minor}.{run number}` | `1.0.42` |
| Push to another branch | `{major}.{minor}.{run number}-{branch}` (pre-release) | `1.0.43-feature-cosmos-ttl` |

The major and minor versions come from `VersionPrefix` in `Directory.Build.props`; change it to `1.1.0` to start
producing `1.1.x` versions. In branch versions, characters that NuGet doesn't allow (such as `/` and `_`) become `-`.
Run numbers are shared by all branches, so every build gets a unique version.

To run the integration tests in CI, add a `OPENIDDICT_COSMOSDB_CONNECTION_STRING` repository secret pointing
at an account dedicated to testing.

### Consuming the packages

GitHub Packages requires authentication, even to restore. Add the feed with a token that has the
`read:packages` scope (in GitHub Actions, the `GITHUB_TOKEN` works for repositories in the organization
once they're granted access to the package):

```bash
dotnet nuget add source https://nuget.pkg.github.com/ledgerscope/index.json --name ledgerscope --username <github user> --password <token> --store-password-in-clear-text
```

## License

Apache License 2.0. This project is derived from OpenIddict; see [NOTICE.md](NOTICE.md).
