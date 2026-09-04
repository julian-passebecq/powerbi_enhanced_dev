param(
    [string[]]$Servers = @('localhost:2383', 'localhost:20680', 'localhost:20681', 'localhost:20683'),
    [string]$AssemblyDirectory = (Join-Path $PSScriptRoot '../tests/PbiBench.Semantic.Tests/bin/Release/net48'),
    [string]$OutputFile = (Join-Path $PSScriptRoot '../artifacts/v9-query-live-evidence.json')
)
$ErrorActionPreference = 'Stop'
# Run with Windows PowerShell 5.1 for the retained .NET Framework TOM adapter.
$AssemblyDirectory = [IO.Path]::GetFullPath($AssemblyDirectory)
foreach ($name in @('Microsoft.AnalysisServices.Core', 'Microsoft.AnalysisServices.Tabular', 'PbiBench.Core', 'PbiBench.Semantic')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $AssemblyDirectory ($name + '.dll')))
}
$probes = New-Object System.Collections.Generic.List[object]
$verified = $false
foreach ($endpoint in $Servers) {
    $server = New-Object Microsoft.AnalysisServices.Tabular.Server
    $reader = $null
    $stage = 'Connect'
    try {
        $server.Connect(('Data Source=' + $endpoint + ';Connect Timeout=3;Timeout=5;Application Name=PbiBench read-only query verification'), $true)
        $xmla = '<Statement>SELECT [CATALOG_NAME] FROM $SYSTEM.DBSCHEMA_CATALOGS</Statement>'
        $stage = 'Discover catalog'
        $xmlaResults = $null
        $reader = $server.ExecuteReader($xmla, [ref]$xmlaResults, $null, $true)
        if ($null -ne $xmlaResults -and $xmlaResults.ContainsErrors) { throw 'Catalog discovery returned an XMLA error.' }
        if ($null -eq $reader -or -not $reader.Read()) {
            $probes.Add([pscustomobject]@{ endpoint = $endpoint; status = 'Connected; no visible catalog' })
            continue
        }
        $catalog = [string]$reader.GetValue(0)
        $reader.Dispose(); $reader = $null
        $server.Disconnect()
        $service = New-Object PbiBench.Semantic.TomDaxQueryService
        $stage = 'Execute constants'
        $query = 'EVALUATE FILTER(ROW("Value", 1), FALSE()) EVALUATE ROW("Value", IF(1 < 2 && 2 > 1, 42, 0), "Label", "PbiBench check")'
        $request = [PbiBench.Core.Queries.QueryRequest]::new($endpoint, $catalog, $query, 10, 10, 7)
        $result = $service.ExecuteAsync($request, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
        if ($result.Results.Count -ne 2 -or $result.Results[0].Rows.Count -ne 0 -or $result.Results[1].Rows.Count -ne 1 -or $result.Results[1].Rows[0][0] -ne 42) {
            throw 'Constant-only multi-result verification failed.'
        }
        $limitRequest = [PbiBench.Core.Queries.QueryRequest]::new($endpoint, $catalog, 'EVALUATE {1, 2, 3}', 2, 10, 8)
        $stage = 'Verify retained row limit'
        $limited = $service.ExecuteAsync($limitRequest, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
        if ($limited.Results[0].Rows.Count -ne 2 -or -not $limited.Results[0].IsTruncated) { throw 'Retained row limit verification failed.' }
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $catalogHash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($catalog))).Replace('-', '') }
        finally { $sha.Dispose() }
        $probes.Add([pscustomobject]@{
            endpoint = $endpoint; status = 'Verified'; catalogSha256 = $catalogHash
            executedOnlyConstants = $true; modelWrites = 0
            resultSets = $result.Results.Count; firstResultRows = $result.Results[0].Rows.Count
            secondResultRows = $result.Results[1].Rows.Count; constantValue = $result.Results[1].Rows[0][0]
            elapsedMilliseconds = $result.Elapsed.TotalMilliseconds; rowLimit = 2
            retainedRows = $limited.Results[0].Rows.Count; truncated = $limited.Results[0].IsTruncated
            checks = @('Independent TOM connection', 'Escaped XML statement', 'Multiple EVALUATE results', 'Empty first result preserved', 'Typed constant value', 'Client row cap')
        })
        $verified = $true
        break
    }
    catch {
        # Connection exception text may carry catalog/auth context. Evidence records only type.
        $failure = $_.Exception
        while ($null -ne $failure.InnerException) { $failure = $failure.InnerException }
        $probes.Add([pscustomobject]@{ endpoint = $endpoint; status = 'Unavailable or verification failed'; stage = $stage; exceptionType = $failure.GetType().FullName })
        Write-Output ($endpoint + ' failed during ' + $stage + ': ' + $failure.GetType().Name)
    }
    finally {
        if ($null -ne $reader) { $reader.Dispose() }
        $server.Dispose()
    }
}
$OutputFile = [IO.Path]::GetFullPath($OutputFile)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputFile)) | Out-Null
[pscustomobject]@{ success = $verified; utc = [DateTime]::UtcNow.ToString('O'); probes = $probes.ToArray() } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputFile -Encoding UTF8
Write-Output ('Read-only query verification: ' + $verified + '; evidence: ' + $OutputFile)
if (-not $verified) { exit 1 }
