"""Copy OpenIddict records from Azure Table Storage to Cosmos DB for NoSQL."""

import argparse
import json
import os
import re
import sys
from datetime import datetime, timezone
from decimal import Decimal

from azure.core.exceptions import AzureError
from azure.cosmos import CosmosClient
from azure.cosmos.exceptions import CosmosResourceExistsError
from azure.data.tables import TableServiceClient


TABLE_FIELDS = {
    "applications": (
        "ClientId ClientSecret ConsentType DisplayName DisplayNames Permissions "
        "PostLogoutRedirectUris Properties RedirectUris Requirements Type"
    ).split(),
    "scopes": "Description Descriptions DisplayName DisplayNames Name Properties Resources".split(),
    "authorizations": "ApplicationId CreationDate Properties Scopes Status Subject Type".split(),
    "tokens": (
        "ApplicationId AuthorizationId CreationDate ExpirationDate Payload Properties "
        "RedemptionDate ReferenceId Status Subject Type"
    ).split(),
}
ARRAYS = {
    "Permissions", "PostLogoutRedirectUris", "RedirectUris", "Requirements", "Resources", "Scopes"
}
STRING_MAPS = {"Descriptions", "DisplayNames"}
DATES = {"CreationDate", "ExpirationDate", "RedemptionDate"}
TABLE_METADATA = {"PartitionKey", "RowKey", "Timestamp", "ConcurrencyToken"}
COSMOS_METADATA = {"_rid", "_self", "_etag", "_attachments", "_ts"}


class MigrationError(Exception):
    """A migration failure with an operator-safe message."""


def json_float(text):
    value = float(text)
    if Decimal(str(value)) != Decimal(text):
        raise ValueError("JSON number cannot round-trip")
    return value


def json_int(text):
    value = int(text)
    if abs(value) > 2**53 - 1:
        raise ValueError("JSON integer outside the interoperable range")
    return value


def invalid_constant(_):
    raise ValueError("Non-standard JSON constant")


def unique_object(pairs):
    result = dict(pairs)
    if len(result) != len(pairs):
        raise ValueError("Duplicate JSON key")
    return result


def convert_value(field, value):
    if field in DATES:
        if not isinstance(value, datetime) or value.utcoffset() is None:
            raise MigrationError(f"{field} must be an Azure Table datetime")
        # The Table SDK keeps the original service value alongside Python's six-digit datetime.
        original = getattr(value, "tables_service_value", "")
        match = re.fullmatch(r".*\.([0-9]{7})Z", original)
        last_digit = match[1][-1] if match else "0"
        utc = value.astimezone(timezone.utc)
        return f"{utc.isoformat(timespec='microseconds').removesuffix('+00:00')}{last_digit}Z"

    if not isinstance(value, str):
        raise MigrationError(f"{field} must be a string")
    if field not in ARRAYS | STRING_MAPS | {"Properties"}:
        return value
    try:
        parsed = json.loads(value, parse_float=json_float, parse_int=json_int, parse_constant=invalid_constant,
                            object_pairs_hook=unique_object)
    except (ValueError, RecursionError):
        raise MigrationError(f"{field} contains invalid or unsupported JSON") from None
    if field in ARRAYS:
        if not isinstance(parsed, list) or not all(isinstance(item, str) for item in parsed):
            raise MigrationError(f"{field} must be a JSON string array")
    elif not isinstance(parsed, dict) or (
        field in STRING_MAPS and not all(isinstance(item, str) for item in parsed.values())
    ):
        raise MigrationError(f"{field} must be a compatible JSON object")
    return parsed


def convert(kind, row):
    identifier = row.get("PartitionKey")
    if (not isinstance(identifier, str) or not identifier or
            re.search(r'[/\\?#\x00-\x1f\x7f]', identifier) or len(identifier.encode("utf-8")) > 1023):
        raise MigrationError("PartitionKey is not a valid Cosmos id")

    fields = TABLE_FIELDS[kind]
    if row.keys() - TABLE_METADATA - set(fields):
        raise MigrationError("Unsupported source columns; check for a customized ATS schema")

    document = {"id": identifier}
    for field in fields:
        if row.get(field) is not None:
            name = "clientType" if kind == "applications" and field == "Type" else field[0].lower() + field[1:]
            document[name] = convert_value(field, row[field])
    return document


def documents(kind, table):
    print(f"Reading {kind}...", flush=True)
    for index, row in enumerate(table.list_entities(), start=1):
        try:
            yield index, convert(kind, row)
        except MigrationError as error:
            raise MigrationError(f"{kind}, source row {index}: {error}") from None


def preflight(tables, containers):
    ids = {}
    for kind in TABLE_FIELDS:
        properties = containers[kind].read()
        if properties.get("partitionKey", {}).get("paths") != ["/id"]:
            raise MigrationError(f"{kind} container must be partitioned on /id")
        if properties.get("defaultTtl") is not None:
            raise MigrationError(f"{kind} container must have TTL disabled")
        ids[kind] = set()
        missing_links = 0
        for index, document in documents(kind, tables[kind]):
            identifier = document["id"]
            if identifier in ids[kind]:
                raise MigrationError(f"{kind}, source row {index}: duplicate PartitionKey")
            ids[kind].add(identifier)
            for field, parent in (("applicationId", "applications"), ("authorizationId", "authorizations")):
                if document.get(field) and document[field] not in ids[parent]:
                    missing_links += 1
        print(f"{kind}: {len(ids[kind])} validated, {missing_links} links to missing source records")
    return ids


def copy(tables, containers, ids):
    for kind in TABLE_FIELDS:
        container = containers[kind]
        remaining = ids[kind]
        created = existing = 0
        for index, document in documents(kind, tables[kind]):
            identifier = document["id"]
            if identifier not in remaining:
                raise MigrationError(f"{kind}, source row {index}: source IDs changed after validation")
            remaining.remove(identifier)
            try:
                container.create_item(body=document)
                created += 1
            except CosmosResourceExistsError:
                stored = container.read_item(item=identifier, partition_key=identifier)
                stored = {key: value for key, value in stored.items() if key not in COSMOS_METADATA}
                if json.dumps(stored, sort_keys=True) != json.dumps(document, sort_keys=True):
                    raise MigrationError(f"{kind}, source row {index}: destination document differs") from None
                existing += 1
        if remaining:
            raise MigrationError(f"{kind}: source IDs changed after validation")
        print(f"{kind}: {created} created, {existing} already identical")


def required_env(name):
    value = os.environ.get(name)
    if not value:
        raise MigrationError(f"Set {name} in the environment")
    return value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--database", required=True, help="Existing Cosmos database name")
    parser.add_argument("--apply", action="store_true", help="Write after the read-only validation")
    for kind in TABLE_FIELDS:
        parser.add_argument(f"--{kind}-table", default=f"OpenIddict{kind.title()}")
        parser.add_argument(f"--{kind}-container", default=f"openiddict.{kind}")
    args = parser.parse_args()
    try:
        for option in ("table", "container"):
            if len({getattr(args, f"{kind}_{option}") for kind in TABLE_FIELDS}) != len(TABLE_FIELDS):
                raise MigrationError(f"Each entity kind needs a distinct {option} name")
        ats_connection = required_env("OPENIDDICT_ATS_CONNECTION_STRING")
        cosmos_connection = required_env("OPENIDDICT_COSMOSDB_CONNECTION_STRING")
        with (TableServiceClient.from_connection_string(ats_connection) as ats,
              CosmosClient.from_connection_string(cosmos_connection) as cosmos):
            database = cosmos.get_database_client(args.database)
            tables = {kind: ats.get_table_client(getattr(args, f"{kind}_table")) for kind in TABLE_FIELDS}
            containers = {
                kind: database.get_container_client(getattr(args, f"{kind}_container")) for kind in TABLE_FIELDS
            }
            ids = preflight(tables, containers)
            if args.apply:
                copy(tables, containers, ids)
            else:
                print("Dry run complete; nothing was written. Use --apply to copy.")
    except MigrationError as error:
        print(f"Migration stopped: {error}", file=sys.stderr)
        return 1
    except (AzureError, ValueError) as error:
        status = getattr(error, "status_code", None)
        detail = f", HTTP {status}" if isinstance(status, int) else ""
        print(f"Migration stopped: {type(error).__name__}{detail}. No service payload logged.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
