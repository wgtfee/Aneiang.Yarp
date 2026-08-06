# Industrial Platform Contract v1.0

This document is the cross-repository contract for the industrial platform. Repository names may differ from runtime system codes; integrations must use the stable codes and routes below.

## 1. Repository and system mapping

| Repository / module | SystemCode | Responsibility |
| --- | --- | --- |
| `wgtfee/Aneiang.Yarp` / `Industrial.Gateway.Host` | `PLATFORM` | Unified edge gateway |
| `wgtfee/Industrial.IAM.Api` | `IAM` | Identity, OIDC/OAuth2, users, roles, permissions, system access |
| `wgtfee/Industrial.Security.Abstractions` | shared SDK | Security contracts |
| `wgtfee/Industrial.Security.AspNetCore` | shared SDK | ASP.NET Core authentication/authorization adapter |
| `wgtfee/MOL` | `MES` | MES backend |
| `wgtfee/vol.web` | `MES` | MES web frontend |
| `wgtfee/IoTSharp` | `IOT` | IoT backend and ClientApp frontend |
| `wgtfee/WCS` | `WCS` | WCS/EMS/RGV scheduling and runtime |
| `wgtfee/FreeIM` | `IM` | Realtime communication transport |
| `wgtfee/ThingsGateway` | `THINGSGATEWAY` | Edge/device acquisition gateway |

## 2. Gateway route contract

Canonical public paths:

- `/mes/**` -> MES frontend
- `/iot/**` -> IoT frontend
- `/wcs/**` -> WCS management frontend when present
- `/thinggateway/**` -> ThingsGateway management frontend when present
- `/api/mes/**` -> MOL
- `/api/iot/**` -> IoTSharp
- `/api/wcs/**` -> WCS management API
- `/api/thinggateway/**` -> ThingsGateway management API
- `/api/iam/**` -> IAM management API
- `/connect/**`, `/account/**`, `/.well-known/**` -> IAM/OIDC endpoints
- `/im/ws/**` -> FreeIM WebSocket transport

Legacy compatibility routes may remain temporarily during migration but must not be used by new code.

### Frontend rule

SPA shells and their static resources are public so an unauthenticated browser can reach a login/callback page. Authorization is mandatory on business APIs and sensitive management endpoints. A hidden frontend button is never an authorization boundary.

### Trace rule

The gateway owns `X-Trace-Id`. It accepts a safe incoming value or creates one, forwards it downstream and returns it in the response.

## 3. Permission contract

New business permissions use:

`{system}.{domain}.{action}`

Examples:

- `mes.order.view`
- `mes.order.audit`
- `iot.device.edit`
- `wcs.task.cancel`
- `wcs.dispatch.manual`
- `im.channel.create`

Rules:

1. Codes are lower-case and stable.
2. Roles are never hard-coded into business endpoints.
3. IAM assigns permissions; each business system owns the definition of its permissions.
4. UI pages/buttons consume permissions, but server-side APIs/commands are the enforcement boundary.
5. Data scope (factory, warehouse, line, device, task ownership, conversation membership) remains inside the business system.

Permission manifest resource types are `System`, `Module`, `Page`, `Action`, and `Api`.

## 4. Identity contract

Canonical claims:

- `sub`: IAM stable subject identifier
- `global_user_id`: cross-system IAM user identifier
- `local_user_id`: optional local/shadow user identifier
- `name`: display/user name
- `tenant_id`: canonical tenant identifier
- `tenant`: legacy-compatible tenant identifier during migration
- `identity_source`: `Local` or `Platform`
- `permission_version`: permission cache/version marker
- `service`: `true` for machine identities
- `system_code`: optional machine/system identity context

## 5. Human and machine identity

Human clients use Authorization Code + PKCE as the target state:

- `industrial-web`
- `industrial-gateway-web`
- `industrial-desktop`
- `industrial-pda`

Machine clients use Client Credentials:

- gateway/service integration clients
- MES/IoT/WCS resource manifest synchronization
- ThingsGateway service identity
- future IM/service integrations

High-frequency industrial data must never call IAM for every telemetry/PLC message. IAM authenticates the service/session boundary, not the realtime data hot path.

## 6. Health contract

All backend services converge on:

- `/health/live`: process/application survival only
- `/health/ready`: startup/readiness of critical dependencies
- `/health/traffic`: YARP traffic decision endpoint

Only a critical failure should remove an instance from traffic. Degradable/optional dependency failures should normally produce `Degraded` while traffic remains allowed.

## 7. Runtime autonomy

WCS realtime/runtime components must not depend on IAM or Gateway availability. IAM/Security applies to human management operations such as manual device control, task cancellation, forced recovery, configuration changes and alarm acknowledgement. PLC polling, state propagation, scheduling, resource locks, deadlock handling and dispatch remain autonomous.

## 8. Migration modes

Each existing business system migrates in this order:

1. `Local`: existing authentication/authorization remains authoritative.
2. `Shadow`: IAM identity and central permission decisions are evaluated in parallel; local authorization remains authoritative and differences are audited.
3. `Centralized`: IAM becomes the authorization source after mismatch rate reaches zero for the agreed observation window and rollback is verified.

No system may skip directly from legacy local authorization to Centralized without a tested rollback path.
