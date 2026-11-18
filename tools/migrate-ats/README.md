# Migrate OpenIddict from Azure Table Storage to Cosmos DB for NoSQL

Use Python 3.10+ and run these commands from the repository root:

```sh
python3 -m venv .venv
. .venv/bin/activate
python -m pip install -r tools/migrate-ats/requirements.txt
python tools/migrate-ats/migrate.py --database auth
```

Set `OPENIDDICT_ATS_CONNECTION_STRING` and `OPENIDDICT_COSMOSDB_CONNECTION_STRING` in the environment from your secret store before running the script. Prefer read-only credentials for ATS and for the Cosmos dry run. The apply run needs permission to read container settings and documents and to create items in the target database. The tool does not write credentials or record contents to logs or files.

The default tables are `OpenIddictApplications`, `OpenIddictScopes`, `OpenIddictAuthorizations` and `OpenIddictTokens`. In a dedicated target database, provision four empty Cosmos DB for NoSQL containers named `openiddict.applications`, `openiddict.scopes`, `openiddict.authorizations` and `openiddict.tokens`, each partitioned on `/id` with TTL disabled. Use `--help` for overrides such as `--applications-table` and `--applications-container`.

1. Rehearse on non-production data. The dry run scans all four tables, checks document conversion and container configuration, and reports counts and dangling references. It does not compare existing Cosmos items. Investigate unexpected counts or dangling links before applying; the tool preserves these links rather than rewriting them.
2. Back up the source. Stop all writes to both stores, including authentication traffic and pruning jobs, before the final run. Keep writers off throughout the copy; the script reads ATS twice and does not take a snapshot or sync changes.
3. Run `python tools/migrate-ats/migrate.py --database auth --apply`. The script never updates or deletes any record. A failed run can leave a partial copy; rerun against the unchanged source and the same destination to accept identical items and create the rest. A conflicting item stops the run instead of being overwritten.
4. Compare the per-kind totals with the destination and verify client authentication and representative token flows before switching traffic. Keep ATS intact. If you have enabled Cosmos writes, switching back to ATS is not lossless: the script does not copy changes back.

The script uses the ATS `PartitionKey` as the Cosmos `id`, preserves references, payloads and the stored `ClientSecret`, and maps application `Type` to `clientType`. ATS concurrency tokens are not copied; Cosmos issues new `_etag` values. It copies only the four OpenIddict tables. It does not upgrade app configuration or migrate keys or external credential stores. Check that the upgraded application can still validate client credentials and decrypt legacy tokens before cutover.
