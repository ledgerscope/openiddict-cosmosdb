# Migrate OpenIddict from Azure Table Storage to Cosmos DB for NoSQL

For a debuggable C# version with the same migration behavior and authentication, see [the .NET tool](../migrate-ats-csharp/README.md).

Run on an Azure host with a system-assigned managed identity, PowerShell 7.6+, and Azure CLI 2.54.0 or later. The script uses `az login --identity`; it does not accept connection strings, keys, or login details. It creates a temporary private Azure CLI configuration directory, removes it on exit, and leaves your existing Azure CLI session unchanged. Ensure the temporary directory is private to the executing user, especially on Windows.

```powershell
./tools/migrate-ats/Migrate-OpenIddict.ps1 `
    -TableEndpoint https://sourcestorage.table.core.windows.net `
    -CosmosDatabaseEndpoint https://destination.documents.azure.com/dbs/auth
```

The Cosmos endpoint includes `/dbs/<database>` so the script does not have to guess which database to write to. Use standard Azure public-cloud service addresses, even if they resolve through private networking. Give the identity **Storage Table Data Reader** access to the source tables and **Cosmos DB Built-in Data Reader** on the target database for the dry run. To apply, use **Cosmos DB Built-in Data Contributor** on the target database, or an equivalent Cosmos NoSQL data-plane role with metadata read and item read/create permissions. Azure management-plane roles alone do not grant data access.

The script uses the providers' default names: `OpenIddictApplications`, `OpenIddictScopes`, `OpenIddictAuthorizations` and `OpenIddictTokens` in Table Storage. In a dedicated target database, provision four empty Cosmos DB for NoSQL containers named `openiddict.applications`, `openiddict.scopes`, `openiddict.authorizations` and `openiddict.tokens`, each partitioned on `/id` with TTL disabled.

1. Rehearse on non-production data. The dry run scans all four tables, checks document conversion and container configuration, and reports counts and dangling references. It does not compare existing Cosmos items. Investigate unexpected counts or dangling links before applying; the tool preserves these links rather than rewriting them.
2. Back up the source. Stop all writes to both stores, including authentication traffic and pruning jobs, before the final run. Keep writers off throughout the copy; the script reads ATS twice and does not take a snapshot or sync changes.
3. Rerun the same command with `-Apply`. The script never updates or deletes any record. A failed run can leave a partial copy; rerun against the unchanged source and the same destination to accept identical items and create the rest. A conflicting item stops the run instead of being overwritten.
4. Compare the per-kind totals with the destination and verify client authentication and representative token flows before switching traffic. Keep ATS intact. If you have enabled Cosmos writes, switching back to ATS is not lossless: the script does not copy changes back.

The script uses the ATS `PartitionKey` as the Cosmos `id`, preserves references, payloads and the stored `ClientSecret`, and maps application `Type` to `clientType`. ATS concurrency tokens are not copied; Cosmos issues new `_etag` values. It copies only the four OpenIddict tables. It does not upgrade app configuration or migrate keys or external credential stores. Check that the upgraded application can still validate client credentials and decrypt legacy tokens before cutover.
