# C# OpenIddict ATS migration

This .NET 10 console project is the C# alternative to the [PowerShell migration](../migrate-ats/README.md). Run it on an Azure host with a system-assigned managed identity and Azure CLI 2.54.0 or later:

```sh
dotnet run --project tools/migrate-ats-csharp -- https://sourcestorage.table.core.windows.net https://destination.documents.azure.com/dbs/auth
```

Only the two endpoints are required. Append `--apply` to create documents after validation; without it the tool is read-only. For debugging, open `MigrateAts.csproj` in your IDE and pass the same arguments in the debugger. The tool uses `az login --identity` with an isolated temporary CLI configuration, leaving existing sessions unchanged. Ensure the temporary directory is private to the executing user, especially on Windows.

The [PowerShell runbook](../migrate-ats/README.md) describes the required Azure data-plane roles, default table and container names, backups, write freeze, validation and cutover precautions; they apply equally to this tool. No source records are modified, and conflicting destination records are never overwritten.
