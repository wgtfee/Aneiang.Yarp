[CmdletBinding()]
param(
    [string]$GatewayBaseUrl = 'http://localhost:5202',
    [Parameter(Mandatory = $true)][string]$UserName,
    [Parameter(Mandatory = $true)][string]$Password,
    [string]$Tenant = '',
    [string]$ClientId = 'industrial-web',
    [string]$RedirectUri = '',

    [string]$IamDirectUrl = 'http://localhost:5100',
    [string]$MesDirectUrl = 'http://localhost:9991',
    [string]$IotDirectUrl = 'http://localhost:5002',
    [string]$WcsDirectUrl = 'http://localhost:5003',
    [string]$ThingsGatewayDirectUrl = 'http://localhost:5000',
    [string]$FreeImDirectUrl = 'http://localhost:6001',

    [string]$IoTValidProbePath = '',
    [string]$WcsValidProbePath = '',
    [string]$ThingsGatewayValidProbePath = '',

    [string]$ForbiddenAccessToken = '',
    [string]$ForbiddenProbePath = '/api/im/online',
    [string]$ExpiredAccessToken = '',
    [string]$ExpiredProbePath = '/api/iam/users/me',

    [switch]$SkipDirectHealth,
    [switch]$SkipMesSignalR,
    [switch]$SkipFreeImWebSocket,
    [switch]$ExpectIamUnavailable,
    [string]$IamOutageProbePath = '/api/im/me',
    [ValidateSet('', 'iam', 'mes', 'iot', 'wcs', 'thinggateway', 'im')]
    [string]$ExpectedUnavailableService = '',
    [switch]$AllowMigrationMode
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Results = [System.Collections.Generic.List[object]]::new()

function Add-Result {
    param([string]$Name, [string]$Status, [string]$Detail)
    $script:Results.Add([pscustomobject]@{
        Name = $Name
        Status = $Status
        Detail = $Detail
    })
    $prefix = switch ($Status) {
        'PASS' { '[PASS]' }
        'SKIP' { '[SKIP]' }
        default { '[FAIL]' }
    }
    Write-Host "$prefix $Name - $Detail"
}

function Fail-Check {
    param([string]$Name, [string]$Detail)
    Add-Result $Name 'FAIL' $Detail
    throw "Acceptance check failed: $Name - $Detail"
}

function Normalize-BaseUrl {
    param([string]$Value)
    return $Value.TrimEnd('/')
}

$GatewayBaseUrl = Normalize-BaseUrl $GatewayBaseUrl
$IamDirectUrl = Normalize-BaseUrl $IamDirectUrl
$MesDirectUrl = Normalize-BaseUrl $MesDirectUrl
$IotDirectUrl = Normalize-BaseUrl $IotDirectUrl
$WcsDirectUrl = Normalize-BaseUrl $WcsDirectUrl
$ThingsGatewayDirectUrl = Normalize-BaseUrl $ThingsGatewayDirectUrl
$FreeImDirectUrl = Normalize-BaseUrl $FreeImDirectUrl
if ([string]::IsNullOrWhiteSpace($RedirectUri)) {
    $RedirectUri = "$GatewayBaseUrl/mes/"
}

$cookieContainer = [System.Net.CookieContainer]::new()
$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $true
$handler.CookieContainer = $cookieContainer
$http = [System.Net.Http.HttpClient]::new($handler)
$http.Timeout = [TimeSpan]::FromSeconds(20)

function New-Request {
    param(
        [string]$Method,
        [string]$Uri,
        [string]$Bearer = '',
        [string]$Body = '',
        [string]$ContentType = 'application/json'
    )
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), $Uri)
    if (-not [string]::IsNullOrWhiteSpace($Bearer)) {
        $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Bearer)
    }
    if (-not [string]::IsNullOrEmpty($Body)) {
        $request.Content = [System.Net.Http.StringContent]::new($Body, [System.Text.Encoding]::UTF8, $ContentType)
    }
    return $request
}

function Send-Request {
    param(
        [string]$Method,
        [string]$Uri,
        [string]$Bearer = '',
        [string]$Body = '',
        [string]$ContentType = 'application/json'
    )
    $request = New-Request -Method $Method -Uri $Uri -Bearer $Bearer -Body $Body -ContentType $ContentType
    try {
        return $http.SendAsync($request).GetAwaiter().GetResult()
    }
    finally {
        $request.Dispose()
    }
}

function Read-Body {
    param([System.Net.Http.HttpResponseMessage]$Response)
    return $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
}

function Assert-Status {
    param(
        [string]$Name,
        [System.Net.Http.HttpResponseMessage]$Response,
        [int[]]$Expected
    )
    $status = [int]$Response.StatusCode
    $body = Read-Body $Response
    if ($Expected -notcontains $status) {
        $trimmed = if ($body.Length -gt 400) { $body.Substring(0, 400) + '...' } else { $body }
        Fail-Check $Name "expected HTTP $($Expected -join '/') but received $status. Body=$trimmed"
    }
    Add-Result $Name 'PASS' "HTTP $status"
    return $body
}

function Parse-Json {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    return $Text | ConvertFrom-Json -Depth 50
}

function New-Base64UrlRandom {
    param([int]$Length)
    $bytes = [byte[]]::new($Length)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Get-Sha256Base64Url {
    param([string]$Value)
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($Value)
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    return [Convert]::ToBase64String($hash).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Encode-Query {
    param([hashtable]$Values)
    $pairs = foreach ($key in $Values.Keys) {
        $encodedKey = [Uri]::EscapeDataString([string]$key)
        $encodedValue = [Uri]::EscapeDataString([string]$Values[$key])
        "$encodedKey=$encodedValue"
    }
    return $pairs -join '&'
}

function Parse-Query {
    param([string]$Query)
    $result = @{}
    foreach ($part in $Query.TrimStart('?').Split('&', [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $pair = $part.Split('=', 2)
        if ($pair.Count -eq 2) {
            $result[[Uri]::UnescapeDataString($pair[0])] = [Uri]::UnescapeDataString($pair[1].Replace('+', ' '))
        }
    }
    return $result
}

function Get-PlatformToken {
    $loginJson = @{
        userName = $UserName
        password = $Password
        tenant = if ([string]::IsNullOrWhiteSpace($Tenant)) { $null } else { $Tenant }
    } | ConvertTo-Json -Compress

    $login = Send-Request -Method 'POST' -Uri "$GatewayBaseUrl/account/login" -Body $loginJson
    try {
        [void](Assert-Status 'IAM account session' $login @(200))
    }
    finally { $login.Dispose() }

    $state = New-Base64UrlRandom 24
    $verifier = New-Base64UrlRandom 64
    $challenge = Get-Sha256Base64Url $verifier
    $authorizeQuery = Encode-Query @{
        client_id = $ClientId
        response_type = 'code'
        redirect_uri = $RedirectUri
        scope = 'openid profile industrial-platform'
        state = $state
        code_challenge = $challenge
        code_challenge_method = 'S256'
    }
    $authorize = Send-Request -Method 'GET' -Uri "$GatewayBaseUrl/connect/authorize?$authorizeQuery"
    try {
        $authorizeStatus = [int]$authorize.StatusCode
        if ($authorizeStatus -lt 300 -or $authorizeStatus -ge 400 -or $null -eq $authorize.Headers.Location) {
            Fail-Check 'IAM PKCE authorize' "expected redirect, received HTTP $authorizeStatus"
        }
        $location = $authorize.Headers.Location
        if (-not $location.IsAbsoluteUri) {
            $location = [Uri]::new([Uri]$GatewayBaseUrl, $location)
        }
        $callback = Parse-Query $location.Query
        if (-not $callback.ContainsKey('state') -or $callback['state'] -ne $state) {
            Fail-Check 'IAM PKCE state' 'callback state mismatch'
        }
        if (-not $callback.ContainsKey('code') -or [string]::IsNullOrWhiteSpace($callback['code'])) {
            $errorDescription = if ($callback.ContainsKey('error_description')) { $callback['error_description'] } else { 'authorization code missing' }
            Fail-Check 'IAM PKCE authorize' $errorDescription
        }
        Add-Result 'IAM PKCE authorize' 'PASS' "HTTP $authorizeStatus with authorization code"
        $code = $callback['code']
    }
    finally { $authorize.Dispose() }

    $tokenBody = Encode-Query @{
        grant_type = 'authorization_code'
        client_id = $ClientId
        code = $code
        redirect_uri = $RedirectUri
        code_verifier = $verifier
    }
    $tokenResponse = Send-Request -Method 'POST' -Uri "$GatewayBaseUrl/connect/token" -Body $tokenBody -ContentType 'application/x-www-form-urlencoded'
    try {
        $body = Assert-Status 'IAM token exchange' $tokenResponse @(200)
        $token = Parse-Json $body
        if ($null -eq $token -or [string]::IsNullOrWhiteSpace([string]$token.access_token)) {
            Fail-Check 'IAM token exchange' 'access_token missing'
        }
        return [string]$token.access_token
    }
    finally { $tokenResponse.Dispose() }
}

function Invoke-NoTokenGateChecks {
    $checks = @(
        @{ Name='IAM no-token gate'; Method='GET'; Path='/api/iam/users/me'; Body='' },
        @{ Name='MES no-token gate'; Method='POST'; Path='/api/mes/User/getCurrentUserInfo'; Body='{}' },
        @{ Name='IoT no-token gate'; Method='GET'; Path='/api/iot/__platform_acceptance_probe__'; Body='' },
        @{ Name='WCS no-token gate'; Method='GET'; Path='/api/wcs/__platform_acceptance_probe__'; Body='' },
        @{ Name='ThingsGateway no-token gate'; Method='GET'; Path='/api/thinggateway/__platform_acceptance_probe__'; Body='' },
        @{ Name='FreeIM no-token gate'; Method='GET'; Path='/api/im/me'; Body='' }
    )
    foreach ($check in $checks) {
        $response = Send-Request -Method $check.Method -Uri "$GatewayBaseUrl$($check.Path)" -Body $check.Body
        try { [void](Assert-Status $check.Name $response @(401)) }
        finally { $response.Dispose() }
    }
}

function Invoke-ValidTokenChecks {
    param([string]$AccessToken)

    $iam = Send-Request -Method 'GET' -Uri "$GatewayBaseUrl/api/iam/users/me" -Bearer $AccessToken
    try { [void](Assert-Status 'IAM valid-token API' $iam @(200)) }
    finally { $iam.Dispose() }

    $mes = Send-Request -Method 'POST' -Uri "$GatewayBaseUrl/api/mes/User/getCurrentUserInfo" -Bearer $AccessToken -Body '{}'
    try { [void](Assert-Status 'MES valid-token API' $mes @(200)) }
    finally { $mes.Dispose() }

    $im = Send-Request -Method 'GET' -Uri "$GatewayBaseUrl/api/im/me" -Bearer $AccessToken
    try { [void](Assert-Status 'FreeIM valid-token API' $im @(200)) }
    finally { $im.Dispose() }

    $optional = @(
        @{ Name='IoT valid-token API'; Path=$IoTValidProbePath },
        @{ Name='WCS valid-token API'; Path=$WcsValidProbePath },
        @{ Name='ThingsGateway valid-token API'; Path=$ThingsGatewayValidProbePath }
    )
    foreach ($probe in $optional) {
        if ([string]::IsNullOrWhiteSpace($probe.Path)) {
            Add-Result $probe.Name 'SKIP' 'no safe read-only probe path supplied'
            continue
        }
        $response = Send-Request -Method 'GET' -Uri "$GatewayBaseUrl$($probe.Path)" -Bearer $AccessToken
        try { [void](Assert-Status $probe.Name $response @(200)) }
        finally { $response.Dispose() }
    }
}

function Invoke-DirectHealthChecks {
    $services = @(
        @{ Name='IAM'; Url=$IamDirectUrl },
        @{ Name='MES'; Url=$MesDirectUrl },
        @{ Name='IoT'; Url=$IotDirectUrl },
        @{ Name='WCS'; Url=$WcsDirectUrl },
        @{ Name='ThingsGateway'; Url=$ThingsGatewayDirectUrl },
        @{ Name='FreeIM'; Url=$FreeImDirectUrl }
    )
    foreach ($service in $services) {
        $key = $service.Name.ToLowerInvariant()
        $expected = if ($ExpectedUnavailableService -eq $key) { @(503) } else { @(200) }
        $response = Send-Request -Method 'GET' -Uri "$($service.Url)/health/traffic"
        try { [void](Assert-Status "$($service.Name) traffic health" $response $expected) }
        finally { $response.Dispose() }
    }
}

function Convert-ToWebSocketUri {
    param([string]$HttpUri)
    $uri = [Uri]$HttpUri
    $builder = [UriBuilder]::new($uri)
    $builder.Scheme = if ($uri.Scheme -eq 'https') { 'wss' } else { 'ws' }
    $builder.Port = $uri.Port
    return $builder.Uri
}

function Test-MesSignalR {
    param([string]$AccessToken)
    $negotiate = Send-Request -Method 'POST' -Uri "$GatewayBaseUrl/message/negotiate?negotiateVersion=1" -Bearer $AccessToken -Body ''
    try {
        $body = Assert-Status 'MES SignalR negotiate' $negotiate @(200)
        $json = Parse-Json $body
        $connectionToken = [string]$json.connectionToken
        if ([string]::IsNullOrWhiteSpace($connectionToken)) {
            Fail-Check 'MES SignalR negotiate' 'connectionToken missing'
        }
    }
    finally { $negotiate.Dispose() }

    $query = Encode-Query @{ id=$connectionToken; access_token=$AccessToken }
    $wsUri = Convert-ToWebSocketUri "$GatewayBaseUrl/message?$query"
    $ws = [System.Net.WebSockets.ClientWebSocket]::new()
    $cts = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(12))
    try {
        $ws.ConnectAsync($wsUri, $cts.Token).GetAwaiter().GetResult()
        if ($ws.State -ne [System.Net.WebSockets.WebSocketState]::Open) {
            Fail-Check 'MES SignalR websocket' "state=$($ws.State)"
        }

        $handshakeBytes = [System.Text.Encoding]::UTF8.GetBytes("{`"protocol`":`"json`",`"version`":1}`u{001e}")
        $segment = [ArraySegment[byte]]::new($handshakeBytes)
        $ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()

        $buffer = [byte[]]::new(4096)
        $receiveSegment = [ArraySegment[byte]]::new($buffer)
        $received = $ws.ReceiveAsync($receiveSegment, $cts.Token).GetAwaiter().GetResult()
        if ($received.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
            Fail-Check 'MES SignalR websocket' 'server closed during SignalR handshake'
        }
        Add-Result 'MES SignalR websocket' 'PASS' 'authenticated websocket handshake opened'
    }
    finally {
        try { $ws.Abort() } catch { }
        $ws.Dispose()
        $cts.Dispose()
    }
}

function Test-FreeImWebSocket {
    param([string]$AccessToken)
    $ticketResponse = Send-Request -Method 'POST' -Uri "$GatewayBaseUrl/api/im/connect" -Bearer $AccessToken
    try {
        $body = Assert-Status 'FreeIM connection ticket' $ticketResponse @(200)
        $ticket = Parse-Json $body
        $path = [string]$ticket.webSocketPath
        if ([string]::IsNullOrWhiteSpace($path)) {
            Fail-Check 'FreeIM connection ticket' 'webSocketPath missing'
        }
    }
    finally { $ticketResponse.Dispose() }

    $absolute = if ($path.StartsWith('http://') -or $path.StartsWith('https://') -or $path.StartsWith('ws://') -or $path.StartsWith('wss://')) {
        $path
    } else {
        "$GatewayBaseUrl$path"
    }
    if ($absolute.StartsWith('http')) { $wsUri = Convert-ToWebSocketUri $absolute } else { $wsUri = [Uri]$absolute }

    $ws = [System.Net.WebSockets.ClientWebSocket]::new()
    $cts = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(12))
    try {
        $ws.ConnectAsync($wsUri, $cts.Token).GetAwaiter().GetResult()
        if ($ws.State -ne [System.Net.WebSockets.WebSocketState]::Open) {
            Fail-Check 'FreeIM websocket' "state=$($ws.State)"
        }
        Add-Result 'FreeIM websocket' 'PASS' 'single-use ticket opened websocket'
    }
    finally {
        try { $ws.Abort() } catch { }
        $ws.Dispose()
        $cts.Dispose()
    }

    $replay = [System.Net.WebSockets.ClientWebSocket]::new()
    $replayCts = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(8))
    $replayRejected = $false
    try {
        try {
            $replay.ConnectAsync($wsUri, $replayCts.Token).GetAwaiter().GetResult()
            $replayRejected = $replay.State -ne [System.Net.WebSockets.WebSocketState]::Open
        }
        catch {
            $replayRejected = $true
        }
        if (-not $replayRejected) {
            Fail-Check 'FreeIM ticket replay' 'same websocket ticket was accepted twice'
        }
        Add-Result 'FreeIM ticket replay' 'PASS' 'reused ticket rejected'
    }
    finally {
        try { $replay.Abort() } catch { }
        $replay.Dispose()
        $replayCts.Dispose()
    }
}

try {
    Write-Host "Platform E2E acceptance target: $GatewayBaseUrl"

    $root = Send-Request -Method 'GET' -Uri "$GatewayBaseUrl/"
    try {
        $rootBody = Assert-Status 'Gateway root' $root @(200)
        $rootJson = Parse-Json $rootBody
        $mode = [string]$rootJson.securityMode
        if (-not $AllowMigrationMode -and $mode -ne 'Centralized') {
            Fail-Check 'Gateway cutover mode' "expected Centralized but received '$mode'"
        }
        Add-Result 'Gateway cutover mode' 'PASS' $mode
    }
    finally { $root.Dispose() }

    $gatewayHealth = Send-Request -Method 'GET' -Uri "$GatewayBaseUrl/healthz"
    try { [void](Assert-Status 'Gateway health' $gatewayHealth @(200)) }
    finally { $gatewayHealth.Dispose() }

    Invoke-NoTokenGateChecks
    $accessToken = Get-PlatformToken

    if ($ExpectIamUnavailable) {
        $outage = Send-Request -Method 'GET' -Uri "$GatewayBaseUrl$IamOutageProbePath" -Bearer $accessToken
        try {
            $status = [int]$outage.StatusCode
            if ($status -eq 200) {
                Fail-Check 'IAM outage fail-closed' 'business request still returned 200; clear permission/system-access caches or wait for their TTL before retrying'
            }
            if (@(401, 403, 502, 503) -notcontains $status) {
                Fail-Check 'IAM outage fail-closed' "unexpected HTTP $status"
            }
            Add-Result 'IAM outage fail-closed' 'PASS' "HTTP $status; no authorization bypass"
        }
        finally { $outage.Dispose() }
    }
    else {
        Invoke-ValidTokenChecks $accessToken

        if ([string]::IsNullOrWhiteSpace($ForbiddenAccessToken)) {
            Add-Result '403 permission gate' 'SKIP' 'ForbiddenAccessToken not supplied'
        }
        else {
            $forbidden = Send-Request -Method 'GET' -Uri "$GatewayBaseUrl$ForbiddenProbePath" -Bearer $ForbiddenAccessToken
            try { [void](Assert-Status '403 permission gate' $forbidden @(403)) }
            finally { $forbidden.Dispose() }
        }

        if ([string]::IsNullOrWhiteSpace($ExpiredAccessToken)) {
            Add-Result 'Expired-token gate' 'SKIP' 'ExpiredAccessToken not supplied'
        }
        else {
            $expired = Send-Request -Method 'GET' -Uri "$GatewayBaseUrl$ExpiredProbePath" -Bearer $ExpiredAccessToken
            try { [void](Assert-Status 'Expired-token gate' $expired @(401)) }
            finally { $expired.Dispose() }
        }

        if ($SkipMesSignalR) { Add-Result 'MES SignalR websocket' 'SKIP' 'SkipMesSignalR requested' }
        else { Test-MesSignalR $accessToken }

        if ($SkipFreeImWebSocket) { Add-Result 'FreeIM websocket' 'SKIP' 'SkipFreeImWebSocket requested' }
        else { Test-FreeImWebSocket $accessToken }
    }

    if ($SkipDirectHealth) {
        Add-Result 'Direct service traffic health' 'SKIP' 'SkipDirectHealth requested'
    }
    else {
        Invoke-DirectHealthChecks
    }

    Write-Host ''
    Write-Host 'Acceptance summary:'
    $script:Results | Format-Table -AutoSize
    $failed = @($script:Results | Where-Object Status -eq 'FAIL').Count
    if ($failed -gt 0) { exit 1 }
    exit 0
}
catch {
    Write-Error $_
    Write-Host ''
    Write-Host 'Acceptance summary before failure:'
    $script:Results | Format-Table -AutoSize
    exit 1
}
finally {
    $http.Dispose()
    $handler.Dispose()
}
