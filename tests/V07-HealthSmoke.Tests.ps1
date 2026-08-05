[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:5202",
    [string]$DashboardPrefix = "platform",
    [switch]$RequireDashboardApi,
    [string[]]$ExpectedClusters = @("iam", "mes", "iot", "wcs", "thinggateway")
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd('/')

function Assert-Status([string]$Path, [int[]]$Expected) {
    try {
        $response = Invoke-WebRequest -Uri "$BaseUrl$Path" -UseBasicParsing -SkipHttpErrorCheck
        if ($Expected -notcontains [int]$response.StatusCode) {
            throw "${Path}: expected HTTP $($Expected -join ', '), got $($response.StatusCode)"
        }
        Write-Host "PASS $Path -> $($response.StatusCode)"
        return $response
    }
    catch {
        throw "${Path}: $($_.Exception.Message)"
    }
}

Assert-Status "/healthz" @(200, 503) | Out-Null
Assert-Status "/health/live" @(200) | Out-Null
Assert-Status "/health/ready" @(200, 503) | Out-Null

if ($RequireDashboardApi) {
    $clustersResponse = Assert-Status "/$DashboardPrefix/api/clusters" @(200, 401, 403)
    $summary = Assert-Status "/$DashboardPrefix/api/operations/health-summary" @(200, 401, 403)
    $snapshot = Assert-Status "/$DashboardPrefix/api/operations/snapshot" @(200, 401, 403)
    $history = Assert-Status "/$DashboardPrefix/api/operations/health-history?limit=10" @(200, 401, 403)
    if ([int]$clustersResponse.StatusCode -eq 200) {
        $payload = $clustersResponse.Content | ConvertFrom-Json
        $items = if ($null -ne $payload.data) { $payload.data } else { $payload }
        $actual = @($items | ForEach-Object { $_.clusterId })
        foreach ($clusterId in $ExpectedClusters) {
            if ($actual -notcontains $clusterId) {
                throw "Expected cluster '$clusterId' was not returned by /api/clusters. Actual: $($actual -join ', ')"
            }
        }
        Write-Host "PASS expected clusters -> $($ExpectedClusters -join ', ')"
    }
    Write-Host "PASS dashboard health APIs reachable (authentication may be required)"
}

Write-Host "V0.7 health smoke checks completed."
