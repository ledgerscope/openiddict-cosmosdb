import unittest
from unittest.mock import Mock, patch

from azure.cosmos.exceptions import CosmosResourceExistsError
from azure.data.tables._deserialize import _from_entity_datetime

from migrate import TABLE_FIELDS, MigrationError, convert, copy, preflight


class MigrationTests(unittest.TestCase):
    def test_all_four_document_types_preserve_ids_and_links(self):
        date = _from_entity_datetime("2026-01-02T03:04:05.1234567Z")
        cases = {
            "applications": (
                {"ClientId": "client", "ClientSecret": "stored-hash", "Type": "confidential",
                 "DisplayNames": '{"en":"Example"}', "RedirectUris": '["https://example.test/"]'},
                {"clientId": "client", "clientSecret": "stored-hash", "clientType": "confidential",
                 "displayNames": {"en": "Example"}, "redirectUris": ["https://example.test/"]},
            ),
            "scopes": (
                {"Name": "scope", "Descriptions": '{"en":"Example"}', "Resources": '["api"]'},
                {"name": "scope", "descriptions": {"en": "Example"}, "resources": ["api"]},
            ),
            "authorizations": (
                {"ApplicationId": "applications-id", "Scopes": '["scope"]', "Status": "valid"},
                {"applicationId": "applications-id", "scopes": ["scope"], "status": "valid"},
            ),
            "tokens": (
                {"ApplicationId": "applications-id", "AuthorizationId": "authorizations-id",
                 "ExpirationDate": date, "ReferenceId": "stored-reference", "Payload": "test-payload",
                 "Properties": '{"nested":[true,1]}'},
                {"applicationId": "applications-id", "authorizationId": "authorizations-id",
                 "expirationDate": "2026-01-02T03:04:05.1234567Z", "referenceId": "stored-reference",
                 "payload": "test-payload", "properties": {"nested": [True, 1]}},
            ),
        }
        for kind, (source, target) in cases.items():
            with self.subTest(kind=kind):
                identifier = f"{kind}-id"
                row = dict(source, PartitionKey=identifier, RowKey="not-the-id", ConcurrencyToken="obsolete")
                self.assertEqual(convert(kind, row), dict(target, id=identifier))

    def test_invalid_source_rows_fail_without_printing_values(self):
        for row, error in (
            ({"PartitionKey": "bad/id"}, "PartitionKey"),
            ({"PartitionKey": "id", "RedirectUris": '["example",42]'}, "RedirectUris"),
            ({"PartitionKey": "id", "Properties": "private malformed content"}, "^Properties"),
            ({"PartitionKey": "id", "CustomSecret": "secret"}, "Unsupported source columns"),
        ):
            with self.subTest(error=error), self.assertRaisesRegex(MigrationError, error) as result:
                convert("applications", row)
            self.assertNotIn("private malformed content", str(result.exception))

    def setUp(self):
        self.tables = {kind: Mock() for kind in TABLE_FIELDS}
        self.containers = {kind: Mock() for kind in TABLE_FIELDS}
        for kind in TABLE_FIELDS:
            self.tables[kind].list_entities.return_value = []
            self.containers[kind].read.return_value = {"partitionKey": {"paths": ["/id"]}}
        self.tables["applications"].list_entities.return_value = [
            {"PartitionKey": "app", "RowKey": "legacy", "Type": "public"}
        ]
        patcher = patch("builtins.print")
        patcher.start()
        self.addCleanup(patcher.stop)

    def test_preflight_validates_without_writing(self):
        ids = preflight(self.tables, self.containers)
        self.assertEqual(ids["applications"], {"app"})
        for container in self.containers.values():
            container.create_item.assert_not_called()
        self.containers["tokens"].read.return_value = {"partitionKey": {"paths": ["/clientId"]}}
        with self.assertRaisesRegex(MigrationError, "partitioned on /id"):
            preflight(self.tables, self.containers)

    def test_duplicate_partition_key_rejected_before_copy(self):
        self.tables["applications"].list_entities.return_value.append({"PartitionKey": "app", "RowKey": "other"})
        with self.assertRaisesRegex(MigrationError, "duplicate PartitionKey"):
            preflight(self.tables, self.containers)
        self.containers["applications"].create_item.assert_not_called()

    def test_retry_accepts_identical_item_but_refuses_a_different_item(self):
        app = self.containers["applications"]
        expected = {"id": "app", "clientType": "public"}
        copy(self.tables, self.containers, preflight(self.tables, self.containers))
        app.create_item.assert_called_once_with(body=expected)

        app.create_item.side_effect = CosmosResourceExistsError(status_code=409, message="test conflict")
        app.read_item.return_value = dict(expected, _etag="new")
        copy(self.tables, self.containers, preflight(self.tables, self.containers))
        app.read_item.assert_called_once_with(item="app", partition_key="app")

        app.read_item.return_value["clientType"] = "confidential"
        with self.assertRaisesRegex(MigrationError, "destination document differs"):
            copy(self.tables, self.containers, preflight(self.tables, self.containers))
        app.upsert_item.assert_not_called()
