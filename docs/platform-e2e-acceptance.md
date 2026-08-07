# Unified Platform E2E Acceptance

This document is the runtime acceptance gate for the centralized industrial platform.
It is intentionally separate from repository build/test gates: a repository can compile
successfully while an SSO route, WebSocket upgrade, dependency health rule, or fail-closed
behavior is still broken at deployment time.

## Branch baseline

The live acceptance harness belongs to `wgtfee/Aneiang.Yarp / Develop` and assumes the
platform repositories are deployed from their designated IAM integration branches.
The Gateway itself must run with:

```text
Gateway:Security:CutoverMode=Centralized
```

Migration mode is not a final unified-auth acceptance state.

## Harness

Run with PowerShell 7:

```powershell
pwsh ./scripts/validate-platform-e2e.ps1 `
  -GatewayBaseUrl http://localhost:5202 `
  -UserName <iam-user> `
  -Password <iam-password>
```

The default development topology is:

```text
Gateway       http://localhost:5202
IAM           http://localhost:5100
MES           http://localhost:9991
IoTSharp      http://localhost:5002
WCS           http://localhost:5003
ThingsGateway http://localhost:5000
FreeIM        http://localhost:6001
```

Override the corresponding `*DirectUrl` parameter at a site where ports or hosts differ.

## What the default run validates

1. Gateway is alive and reports `Centralized` cutover mode.
2. No-token requests are rejected with HTTP 401 at IAM, MES, IoT, WCS, ThingsGateway and FreeIM Gateway routes.
3. The harness performs a real Authorization Code + PKCE login against IAM through YARP.
4. The resulting token can call IAM, MES and FreeIM read-only identity endpoints.
5. MES SignalR executes negotiate + an authenticated WebSocket handshake through YARP.
6. FreeIM obtains a permission-protected connection ticket, opens the WebSocket, and verifies the same ticket cannot be replayed.
7. Every service's direct `/health/traffic` endpoint is checked.

IoT, WCS and ThingsGateway do not currently share one universal read-only `me` endpoint.
For a complete HTTP 200 matrix, pass a known safe GET endpoint for the deployed version:

```powershell
-IoTValidProbePath /api/iot/<safe-read-only-api> `
-WcsValidProbePath /api/wcs/<safe-read-only-api> `
-ThingsGatewayValidProbePath /api/thinggateway/<safe-read-only-api>
```

If these are omitted the harness marks only those three HTTP 200 probes as `SKIP`; their
no-token 401 Gateway gates and direct traffic health are still checked.

## 403 acceptance

A 403 test requires a valid IAM token for a user that is authenticated but intentionally
lacks the tested permission. Supply that token separately:

```powershell
-ForbiddenAccessToken <restricted-user-token> `
-ForbiddenProbePath /api/im/online
```

The default restricted endpoint requires `im.administration.manage`.
Do not use an invalid or expired token for this test; those belong to the 401 gate.

## Expired-token acceptance

Supply a genuinely expired IAM access token:

```powershell
-ExpiredAccessToken <expired-token>
```

The harness expects HTTP 401. It deliberately does not mutate a JWT because a broken
signature tests invalid-token handling, not token expiry.

## IAM outage / fail-closed acceptance

Obtain a valid token while IAM is available, then stop IAM and run the harness with:

```powershell
-ExpectIamUnavailable
```

The business probe must not return HTTP 200. Accepted fail-closed responses are 401, 403,
502 or 503 depending on whether Gateway metadata is cached and where the request is denied.

Permission snapshots can remain valid for their configured TTL. For a strict IAM-outage
check, wait for permission/system-access cache expiry or restart the target business service
before running the outage probe. The important invariant is: IAM outage must never create a
new authorization success.

## Backend/dependency outage acceptance

The harness never stops a production process automatically. Stop the selected backend or
critical dependency using the site's normal service manager, wait for the health interval,
and run:

```powershell
-ExpectedUnavailableService mes
```

Allowed values are:

```text
iam mes iot wcs thinggateway im
```

The selected service's direct `/health/traffic` must return HTTP 503. For YARP destination
removal, allow at least the configured active-health interval before checking live traffic.

## Required final result

A platform release is not fully accepted until the evidence set contains:

```text
Centralized cutover                    PASS
Valid-token 200 matrix                 PASS
No-token 401 matrix                    PASS
Insufficient-permission 403            PASS
Expired-token 401                      PASS
MES SignalR WebSocket                  PASS
FreeIM WebSocket + replay rejection    PASS
Service traffic health                 PASS
Critical dependency/backend outage     PASS
IAM outage fail-closed                 PASS
```

Build/test jobs blocked before execution by GitHub private-repository credentials are
`NOT VERIFIED`; they must not be recorded as project-code failures.
