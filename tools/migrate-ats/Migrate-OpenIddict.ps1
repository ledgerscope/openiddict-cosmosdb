#Requires -Version 7.6
[CmdletBinding()]
param(
    [Parameter(Mandatory)][uri] $TableEndpoint,
    [Parameter(Mandatory)][uri] $CosmosDatabaseEndpoint,
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (!$TableEndpoint.IsAbsoluteUri -or $TableEndpoint.Scheme -ne 'https' -or
    !$TableEndpoint.IsDefaultPort -or $TableEndpoint.UserInfo -or $TableEndpoint.Query -or
    $TableEndpoint.Fragment -or $TableEndpoint.Host -notmatch '^[a-z0-9]+\.table\.core\.windows\.net$' -or
    $TableEndpoint.AbsolutePath -ne '/') {
    throw 'TableEndpoint must be https://<account>.table.core.windows.net.'
}
if (!$CosmosDatabaseEndpoint.IsAbsoluteUri -or $CosmosDatabaseEndpoint.Scheme -ne 'https' -or
    !$CosmosDatabaseEndpoint.IsDefaultPort -or $CosmosDatabaseEndpoint.UserInfo -or
    $CosmosDatabaseEndpoint.Query -or $CosmosDatabaseEndpoint.Fragment -or
    $CosmosDatabaseEndpoint.Host -notmatch '^[a-z0-9-]+\.documents\.azure\.com$' -or
    $CosmosDatabaseEndpoint.AbsolutePath -cnotmatch '^/dbs/[^/]+/?$') {
    throw 'CosmosDatabaseEndpoint must be https://<account>.documents.azure.com/dbs/<database>.'
}

$fields = [ordered]@{
    applications = 'ClientId ClientSecret ConsentType DisplayName DisplayNames Permissions PostLogoutRedirectUris Properties RedirectUris Requirements Type'
    scopes = 'Description Descriptions DisplayName DisplayNames Name Properties Resources'
    authorizations = 'ApplicationId CreationDate Properties Scopes Status Subject Type'
    tokens = 'ApplicationId AuthorizationId CreationDate ExpirationDate Payload Properties RedemptionDate ReferenceId Status Subject Type'
}
$arrays = 'Permissions', 'PostLogoutRedirectUris', 'RedirectUris', 'Requirements', 'Resources', 'Scopes'
$maps = 'Descriptions', 'DisplayNames'
$dates = 'CreationDate', 'ExpirationDate', 'RedemptionDate'
$metadata = 'PartitionKey', 'RowKey', 'Timestamp', 'ConcurrencyToken'

function Invoke-DataRequest($Method, $Uri, $Service, $Operation, $Document, [string] $Id, [switch] $AllowConflict) {
    $resource = if ($Service -eq 'Table') { 'https://storage.azure.com/' } else { 'https://cosmos.azure.com/' }
    for ($attempt = 0; ; $attempt++) {
        if (!$tokens.ContainsKey($resource) -or $tokens[$resource].ExpiresOn -lt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
            try {
                $result = & az account get-access-token --resource $resource --output json --only-show-errors 2>$null
                if ($LASTEXITCODE -ne 0) { throw 'Azure CLI token request failed.' }
                $responseToken = $result | ConvertFrom-Json -AsHashtable
                if ([string]::IsNullOrWhiteSpace($responseToken['accessToken'])) { throw 'Missing access token.' }
                $expiration = [DateTimeOffset]::FromUnixTimeSeconds([long] $responseToken['expires_on'])
                if ($expiration -le [DateTimeOffset]::UtcNow.AddMinutes(5)) { throw 'Token is expired or near expiry.' }
                $tokens[$resource] = @{
                    Token = ConvertTo-SecureString $responseToken['accessToken'] -AsPlainText -Force
                    ExpiresOn = $expiration
                }
            } catch {
                throw "Could not obtain a managed identity token for $Service."
            }
        }
        $token = $tokens[$resource].Token
        $request = @{
            Method = $Method; Uri = $Uri; MaximumRedirection = 0; SkipHttpErrorCheck = $true
            ConnectionTimeoutSeconds = 60; OperationTimeoutSeconds = 60; Verbose = $false; Debug = $false
        }
        $headers = @{ 'x-ms-date' = [DateTime]::UtcNow.ToString('R') }
        if ($Service -eq 'Table') {
            $request.Authentication = 'Bearer'
            $request.Token = $token
            $headers['x-ms-version'] = '2019-02-02'
            $headers['Accept'] = 'application/json;odata=fullmetadata'
        } else {
            $jwt = [System.Net.NetworkCredential]::new('', $token).Password
            $headers['Authorization'] = [uri]::EscapeDataString("type=aad&ver=1.0&sig=$jwt")
            $headers['x-ms-version'] = '2018-12-31'
            if ($Id) {
                $key = [System.Text.Json.JsonSerializer]::Serialize[string]($Id)
                $headers['x-ms-documentdb-partitionkey'] = "[$key]"
            }
            if ($null -ne $Document) {
                $request.ContentType = 'application/json; charset=utf-8'
                $request.Body = $Document.ToJsonString()
            }
        }
        try {
            $response = Invoke-WebRequest @request -Headers $headers
        } catch {
            throw "$Operation failed before receiving a usable HTTP response."
        }
        $status = [int] $response.StatusCode
        if (($status -ge 200 -and $status -lt 300) -or ($AllowConflict -and $status -eq 409)) {
            return $response
        }
        if ($status -ne 429 -or $attempt -ge 5) { throw "$Operation failed (HTTP $status)." }
        $delay = 0
        if (![int]::TryParse([string] ($response.Headers['x-ms-retry-after-ms'] | Select-Object -First 1), [ref] $delay) -or $delay -lt 1) {
            $delay = [int] (1000 * [math]::Pow(2, $attempt))
        }
        Start-Sleep -Milliseconds $delay
    }
}

function Read-TableRows($Kind) {
    $table = 'OpenIddict' + [cultureinfo]::InvariantCulture.TextInfo.ToTitleCase($Kind)
    $endpoint = $TableEndpoint.GetLeftPart([UriPartial]::Authority)
    $query = ''
    do {
        $response = Invoke-DataRequest GET "$endpoint/$table()$query" Table "Read $Kind table"
        $page = [System.Text.Json.Nodes.JsonNode]::Parse($response.Content)
        if ($page['value'] -isnot [System.Text.Json.Nodes.JsonArray]) { throw "Invalid $Kind table response." }
        foreach ($row in $page['value']) { Write-Output -NoEnumerate $row }
        $parts = @(
            foreach ($key in 'NextPartitionKey', 'NextRowKey') {
                $value = $response.Headers["x-ms-continuation-$key"] | Select-Object -First 1
                if ($null -ne $value) { "$key=$([uri]::EscapeDataString($value))" }
            }
        )
        $query = if ($parts.Count) { '?' + ($parts -join '&') } else { '' }
    } while ($query)
}

function Convert-Row($Kind, [System.Text.Json.Nodes.JsonObject] $Row, $Number) {
    $key = $Row['PartitionKey']
    $id = if ($null -ne $key -and $key.GetValueKind() -eq 'String') { $key.GetValue[string]() }
    if (!$id -or $id -match '[/\\?#\x00-\x1f\x7f]' -or [Text.Encoding]::UTF8.GetByteCount($id) -gt 1023) {
        throw "${Kind}, source row ${Number}: invalid Cosmos id."
    }
    $known = $fields[$Kind] -split ' '
    foreach ($column in $Row.GetEnumerator()) {
        if ($column.Key -cnotin $known -and $column.Key -cnotin $metadata -and
            $column.Key -cnotmatch '^(odata\.|@odata\.)' -and $column.Key -cnotmatch '@odata\.type$') {
            throw "${Kind}, source row ${Number}: unsupported source columns."
        }
    }
    $doc = [System.Text.Json.Nodes.JsonObject]::new()
    $doc['id'] = $Row['PartitionKey'].DeepClone()
    foreach ($field in $known) {
        $value = $Row[$field]
        if ($null -eq $value) { continue }
        $name = if ($Kind -eq 'applications' -and $field -eq 'Type') {
            'clientType'
        } else {
            $field.Substring(0, 1).ToLowerInvariant() + $field.Substring(1)
        }
        try {
            $text = $value.GetValue[string]()
            if ($field -in $dates) {
                if ($text -cnotmatch 'Z$') { throw 'Expected a UTC date.' }
                $date = [DateTimeOffset]::ParseExact($text, "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
                    [cultureinfo]::InvariantCulture, [Globalization.DateTimeStyles]::None)
                $value = [System.Text.Json.Nodes.JsonValue]::Create[string]($date.UtcDateTime.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", [cultureinfo]::InvariantCulture))
            } elseif ($field -in $arrays -or $field -in $maps -or $field -eq 'Properties') {
                $value = [System.Text.Json.Nodes.JsonNode]::Parse($text)
                if ($field -in $arrays) {
                    if ($value -isnot [System.Text.Json.Nodes.JsonArray]) { throw 'Expected an array.' }
                    foreach ($element in $value) { [void] $element.GetValue[string]() }
                } else {
                    if ($value -isnot [System.Text.Json.Nodes.JsonObject]) { throw 'Expected an object.' }
                    if ($field -in $maps) {
                        foreach ($element in $value.GetEnumerator()) { [void] $element.Value.GetValue[string]() }
                    }
                }
            } else {
                $value = $value.DeepClone()
            }
        } catch {
            throw "${Kind}, source row ${Number}: invalid $field."
        }
        $doc[$name] = $value
    }
    Write-Output -NoEnumerate $doc
}

$originalConfigDir = $env:AZURE_CONFIG_DIR
$configDir = Join-Path ([IO.Path]::GetTempPath()) ("openiddict-migration-" + [guid]::NewGuid().ToString('N'))
try {
    if ($IsWindows) {
        [void] [IO.Directory]::CreateDirectory($configDir)
    } else {
        [void] [IO.Directory]::CreateDirectory($configDir, [IO.UnixFileMode]::UserRead -bor
            [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute)
    }
    $env:AZURE_CONFIG_DIR = $configDir
    try {
        & az login --identity --allow-no-subscriptions --output none --only-show-errors 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'Azure CLI login failed.' }
    } catch {
        throw 'Could not sign in with the system-assigned managed identity. Run on a supported Azure host with Azure CLI installed.'
    }
$tokens = @{}
$database = $CosmosDatabaseEndpoint.AbsoluteUri.TrimEnd('/')
$ids = @{}
$parents = @{ applicationId = 'applications'; authorizationId = 'authorizations' }

foreach ($kind in $fields.Keys) {
    $container = "$database/colls/openiddict.$kind"
    $response = Invoke-DataRequest GET $container Cosmos "Read $kind container"
    $settings = $response.Content | ConvertFrom-Json -AsHashtable
    $paths = $settings['partitionKey']['paths']
    if (@($paths).Count -ne 1 -or $paths[0] -cne '/id') { throw "$kind container must be partitioned on /id." }
    if ($null -ne $settings['defaultTtl']) { throw "$kind container must have TTL disabled." }
    $ids[$kind] = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $number = $missing = 0
    Read-TableRows $kind | ForEach-Object {
        $number++
        $doc = Convert-Row $kind $_ $number
        if (!$ids[$kind].Add($doc['id'].GetValue[string]())) { throw "${kind}, source row ${number}: duplicate PartitionKey." }
        foreach ($link in $parents.Keys) {
            $ref = $doc[$link]
            if ($null -ne $ref -and !$ids[$parents[$link]].Contains($ref.GetValue[string]())) { $missing++ }
        }
    }
    Write-Host "${kind}: $number validated, $missing links to missing source records"
}

if (!$Apply) {
    Write-Host 'Dry run complete; nothing was written. Use -Apply to copy.'
    return
}

foreach ($kind in $fields.Keys) {
    $docs = "$database/colls/openiddict.$kind/docs"
    $number = $created = $existing = 0
    Read-TableRows $kind | ForEach-Object {
        $number++
        $doc = Convert-Row $kind $_ $number
        $id = $doc['id'].GetValue[string]()
        if (!$ids[$kind].Remove($id)) { throw "${kind}, source row ${number}: source IDs changed after validation." }
        $operation = "${kind}, source row $number"
        $response = Invoke-DataRequest POST $docs Cosmos "$operation create" -Document $doc -Id $id -AllowConflict
        if ($response.StatusCode -eq 409) {
            $response = Invoke-DataRequest GET "$docs/$([uri]::EscapeDataString($id))" Cosmos "$operation read existing" -Id $id
            $stored = [System.Text.Json.Nodes.JsonNode]::Parse($response.Content)
            foreach ($key in '_rid', '_self', '_etag', '_attachments', '_ts') { [void] $stored.Remove($key) }
            if (![System.Text.Json.Nodes.JsonNode]::DeepEquals($stored, $doc)) {
                throw "${operation}: destination document differs."
            }
            $existing++
        } else {
            $created++
        }
    }
    if ($ids[$kind].Count) { throw "${kind}: source IDs changed after validation." }
    Write-Host "${kind}: $created created, $existing already identical"
}
} finally {
    $env:AZURE_CONFIG_DIR = $originalConfigDir
    if (Test-Path -LiteralPath $configDir) {
        Remove-Item -LiteralPath $configDir -Recurse -Force
    }
}
