# Migrate OpenIddict from Azure Table Storage to Cosmos DB for NoSQL

A .NET 10 console tool that copies applications, scopes, authorizations and tokens from [ledgerscope/openiddict-ats](https://github.com/ledgerscope/openiddict-ats) to the containers used by this library. It uses `Azure.Data.Tables`, `Microsoft.Azure.Cosmos` and `Azure.Identity`'s `DefaultAzureCredential`, so it works with a managed identity on an Azure host, or with your developer sign-in (Visual Studio, VS Code, etc.) when run locally. It never accepts connection strings or keys.

```sh
dotnet run --project tools/migrate-ats-csharp -- https://sourcestorage.table.core.windows.net https://destination.documents.azure.com/dbs/auth
```

Only the two endpoints are required. Append `--apply` to create documents after validation; without it the tool is read-only. To debug, open `MigrateAts.csproj` in your IDE and pass the same arguments in the debugger.

The Cosmos endpoint includes `/dbs/<database>` so the tool does not have to guess which database to write to. Use standard Azure public-cloud service addresses, even if they resolve through private networking.

## Permissions

Give the identity **Storage Table Data Reader** on the source storage account and **Cosmos DB Built-in Data Reader** on the target database for the dry run. To apply, use **Cosmos DB Built-in Data Contributor** (or an equivalent Cosmos NoSQL data-plane role with metadata read and item read/create permissions). Azure management-plane roles alone do not grant data access.

## Tables and containers

The tool reads the ATS provider's default tables: `OpenIddictApplications`, `OpenIddictScopes`, `OpenIddictAuthorizations` and `OpenIddictTokens`. In the target database, use Cosmos DB for NoSQL containers named `openiddictapplications`, `openiddictscopes`, `openiddictauthorizations` and `openiddicttokens`, each partitioned on `/id` with TTL disabled. The tool checks this before copying.

The Cosmos provider's default container names contain dots, so configure its `SetApplicationsContainerName`, `SetScopesContainerName`, `SetAuthorizationsContainerName` and `SetTokensContainerName` options to match these dotless names before using the migrated data.

## Cutover steps

1. Rehearse on non-production data. The dry run scans all four tables, checks document conversion and container configuration, and reports counts and dangling references. It does not compare existing Cosmos items. Investigate unexpected counts or dangling links before applying; the tool preserves these links rather than rewriting them.
2. Back up the source. Stop all writes to both stores, including authentication traffic and pruning jobs, before the final run. Keep writers off throughout the copy; the tool reads ATS twice and does not take a snapshot or sync changes.
3. Rerun the same command with `--apply`. The tool never updates or deletes any record. A failed run can leave a partial copy; rerun against the unchanged source and the same destination to accept identical items and create the rest. A conflicting item stops the run instead of being overwritten.
4. Compare the per-kind totals with the destination and verify client authentication and representative token flows before switching traffic. Keep ATS intact. If you have enabled Cosmos writes, switching back to ATS is not lossless: the tool does not copy changes back.

## Mapping

The ATS `PartitionKey` becomes the Cosmos `id`. References, payloads and the stored `ClientSecret` are preserved, JSON columns are stored as JSON, and application `Type` maps to `clientType`. ATS concurrency tokens are not copied; Cosmos issues new `_etag` values. Only the four OpenIddict tables are copied. The tool does not upgrade app configuration or migrate keys or external credential stores. Check that the upgraded application can still validate client credentials and decrypt legacy tokens before cutover.
