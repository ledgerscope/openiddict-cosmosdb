#Requires -Version 7.6
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert($Condition, $Message) {
    if (!$Condition) { throw $Message }
}

$testState = @{
    Documents = @{}
    Requests = [Collections.Generic.List[string]]::new()
    Audiences = [Collections.Generic.List[string]]::new()
}

function Connect-AzAccount([switch] $Identity, [switch] $SkipContextPopulation, $Scope) {
    Assert ($Identity -and $SkipContextPopulation -and $Scope -eq 'Process') 'Expected managed identity sign-in.'
    @{ Context = 'test-context' }
}

function Get-AzAccessToken {
    [CmdletBinding()]
    param($ResourceUrl, $DefaultProfile, [switch] $AsSecureString)
    Assert ($DefaultProfile.Context -eq 'test-context' -and $AsSecureString) 'Expected a secure token from the managed identity profile.'
    $testState.Audiences.Add($ResourceUrl)
    @{ Token = (ConvertTo-SecureString 'fake-token' -AsPlainText -Force); ExpiresOn = [DateTimeOffset]::UtcNow.AddHours(1) }
}

function Invoke-WebRequest {
    [CmdletBinding()]
    param($Method, [uri] $Uri, $Headers, $ContentType, $Body, $Token, $Authentication,
        $MaximumRedirection, [switch] $SkipHttpErrorCheck, $ConnectionTimeoutSeconds, $OperationTimeoutSeconds)
    $testState.Requests.Add("$Method $Uri")
    $path = $Uri.AbsolutePath
    Assert ($MaximumRedirection -eq 0) 'Requests must not follow redirects.'
    if ($Uri.Host.EndsWith('.table.core.windows.net')) {
        Assert ($Method -eq 'GET' -and $Authentication -eq 'Bearer' -and $Token -is [securestring]) 'Expected an authenticated read.'
        switch ($path) {
            '/OpenIddictApplications()' {
                if (!$Uri.Query) {
                    return @{ StatusCode = 200; Headers = @{ 'x-ms-continuation-NextPartitionKey' = @('abc+==') }; Content = '{"value":[]}' }
                }
                Assert ($Uri.Query -eq '?NextPartitionKey=abc%2B%3D%3D') 'Continuation token was not encoded.'
                $row = '{"PartitionKey":"app","RowKey":"different","Type":"confidential","RedirectUris":"[\"https://example.test/\"]","ClientSecret":"fake-hash"}'
            }
            '/OpenIddictScopes()' { $row = '{"PartitionKey":"scope","Name":"test"}' }
            '/OpenIddictAuthorizations()' { $row = '{"PartitionKey":"auth","ApplicationId":"app","Scopes":"[\"test\"]"}' }
            '/OpenIddictTokens()' { $row = '{"PartitionKey":"token","ApplicationId":"app","AuthorizationId":"auth","ExpirationDate":"2026-01-02T03:04:05.1234567Z","ExpirationDate@odata.type":"Edm.DateTime","Properties":"{\"nested\":[true,1]}"}' }
            default { throw 'Unexpected table request.' }
        }
        return @{ StatusCode = 200; Headers = @{}; Content = "{`"value`":[$row]}" }
    }
    Assert ($Headers.Authorization -ceq 'type%3Daad%26ver%3D1.0%26sig%3Dfake-token') 'Expected Cosmos AAD authorization.'
    if ($Method -eq 'GET' -and $path -match '^/dbs/authdata/colls/openiddict\.[a-z]+$') {
        return @{ StatusCode = 200; Content = '{"partitionKey":{"paths":["/id"]}}' }
    }
    if ($Method -eq 'POST' -and $path.EndsWith('/docs')) {
        $doc = $Body | ConvertFrom-Json -AsHashtable -DateKind String
        $id = $doc['id']
        Assert ($Headers['x-ms-documentdb-partitionkey'] -ceq "[`"$id`"]") 'Invalid partition key header.'
        Assert (!$Headers.ContainsKey('x-ms-documentdb-is-upsert')) 'Must not upsert.'
        if ($testState.Documents.ContainsKey($id)) { return @{ StatusCode = 409 } }
        $testState.Documents[$id] = $doc
        return @{ StatusCode = 201 }
    }
    if ($Method -eq 'GET' -and $path -match '/docs/([^/]+)$') {
        $doc = $testState.Documents[$Matches[1]]
        $doc['_etag'] = 'test-etag'
        return @{ StatusCode = 200; Content = ($doc | ConvertTo-Json -Depth 10 -Compress) }
    }
    throw 'Unexpected Cosmos request.'
}

$migration = Join-Path $PSScriptRoot 'Migrate-OpenIddict.ps1'
$endpoints = @{
    TableEndpoint = 'https://testsource.table.core.windows.net'
    CosmosDatabaseEndpoint = 'https://testdest.documents.azure.com/dbs/authdata'
}

& $migration @endpoints
Assert (!($testState.Requests -match '^POST ')) 'Dry run must not write.'
Assert (($testState.Audiences -join ',') -ceq 'https://cosmos.azure.com/,https://storage.azure.com/') 'Expected separate managed identity audiences.'

& $migration @endpoints -Apply
$documents = $testState.Documents
Assert ($documents.Count -eq 4) 'Expected all four types of records.'
Assert ($documents.app.clientType -ceq 'confidential' -and $documents.app.redirectUris[0] -ceq 'https://example.test/') 'Incorrect application mapping.'
Assert ($documents.app.clientSecret -ceq 'fake-hash' -and !$documents.app.ContainsKey('rowKey')) 'Changed the secret or leaked table metadata.'
Assert ($documents.token.expirationDate -ceq '2026-01-02T03:04:05.1234567Z' -and
    $documents.token.applicationId -ceq 'app' -and $documents.token.authorizationId -ceq 'auth') 'Changed token date precision or relationships.'

& $migration @endpoints -Apply
Assert ($documents.Count -eq 4) 'Retry must not create duplicates.'
$documents.app.clientType = 'public'
try {
    & $migration @endpoints -Apply
    throw 'Expected a conflicting item to stop the migration.'
} catch {
    Assert ($_.Exception.Message -like '*destination document differs*') "Unexpected failure: $_"
}
Write-Host 'Migration checks passed.'
