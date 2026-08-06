# Industrial Platform Security and Gateway Rollout Plan

The rollout is intentionally incremental. Existing MES/IoT/WCS authorization remains available until each service passes Shadow verification.

## Phase 0 - Contract freeze

Deliverables:

- Stable `SystemCode` values.
- Permission code convention `{system}.{domain}.{action}`.
- Canonical identity claim names.
- Human vs machine identity rules.
- Canonical health endpoints.
- Canonical gateway paths.

Acceptance:

- A new service can determine its system code, permission naming, identity claims, health endpoints and gateway paths without creating a new convention.

## Phase 1 - Gateway consolidation

Tasks:

1. Normalize frontend routes under `/mes`, `/iot`, `/wcs`, `/thinggateway`.
2. Normalize backend routes under `/api/{system}`.
3. Keep SPA shells public; protect business APIs instead.
4. Propagate `X-Trace-Id` before YARP execution.
5. Verify WebSocket upgrade for FreeIM.
6. Use `/health/traffic` for YARP active health decisions.
7. Retain legacy MES `/api/**` only as temporary compatibility until MES migration.
8. Test 401/403/404/502, downstream outage/recovery, SPA deep links, static assets and WebSocket upgrades.

Acceptance:

- All platform traffic is reachable through the gateway without root-path static asset leakage.
- Login pages are reachable before authentication.
- API authorization remains enforced.
- One TraceId is visible through the whole request chain.

## Phase 2 - IAM production hardening

Tasks:

1. Verify/complete unique database constraints for users, roles, user-role membership, system access, shadow users and migration bindings.
2. Freeze role identity strategy: global role code or RoleId-based relationship before true multi-tenant use.
3. Ensure permission-affecting mutations increment `PermissionVersion`.
4. Rebuild/update Casbin after authoritative changes.
5. Add service-only scope for permission cache invalidation.
6. Register confidential machine clients and minimum required scopes.
7. Keep Password Grant migration-only; make Authorization Code + PKCE the human-client target.
8. Add failure tests for DB loss, signing certificate errors, lockout, disabled user/role, refresh expiry and duplicate writes.

Acceptance:

- IAM can issue/refresh tokens, manage user/role/system access, synchronize manifests and invalidate downstream authorization caches safely.

## Phase 3 - Security SDK v1.0

Tasks:

1. Keep `Industrial.Security.Abstractions` free of ASP.NET Core dependencies.
2. Standardize `AddIndustrialSecurity`, `AddIndustrialJwt`, and `UseIndustrialSecurity` integration.
3. Formalize `Local`, `Shadow`, and `Centralized` modes.
4. Secure cache invalidation endpoints to machine identities/scopes.
5. Standardize fail-closed behavior for sensitive human management operations.
6. Keep emergency/break-glass access explicit, time-bound, IP-restricted and audited.
7. Add unit tests for local/central/shadow decisions, IAM timeout, cache invalidation, token expiry and emergency access.
8. Replace fragile cross-repository project references with a reproducible shared-package or explicit multi-repository build strategy.

Acceptance:

- A new ASP.NET service integrates by referencing the SDK, adding configuration and publishing a permission manifest; it does not implement its own authentication framework.

## Phase 4 - MES pilot (MOL + vol.web)

Tasks:

1. Integrate SDK in `Local` mode without changing current behavior.
2. Implement `ILocalPermissionSource` against MOL's existing permission model.
3. Map stable MES permission codes to legacy menu/button/API permission keys.
4. Build and synchronize the MES permission manifest.
5. Migrate/bind local users to IAM global users.
6. Enable Shadow User mapping.
7. Switch authentication to IAM while keeping local authorization authoritative.
8. Enable Shadow central permission comparison and collect mismatch metrics.
9. Update vol.web login/token/logout/callback behavior to IAM.
10. Show/hide UI actions by stable PermissionCode while keeping API enforcement server-side.
11. Test rollback to local authentication/authorization.
12. Move to Centralized only after mismatch rate is zero for the agreed observation window.

Acceptance:

- MES is the reference implementation for every later business-system migration.

## Phase 5 - IoT + ThingsGateway

Tasks:

1. Integrate IoTSharp management APIs using the MES-proven pattern.
2. Build IoT permission manifest (`iot.device.*`, `iot.telemetry.view`, `iot.rule.*`, etc.).
3. Keep device/factory/line data scope inside IoTSharp.
4. Create ThingsGateway machine client(s) using Client Credentials.
5. Authenticate service/session boundaries only; never perform IAM calls per telemetry message.
6. Test token renewal and high-throughput telemetry with IAM temporarily unavailable.

Acceptance:

- Human IoT management uses IAM; realtime device data remains independent of IAM request latency.

## Phase 6 - WCS management-plane integration

Tasks:

1. Apply Security only to human management APIs/commands.
2. Define WCS permissions for task, device, dispatch, alarms and configuration.
3. Keep PLC polling, StateCenter, EventBus, TaskScheduler, TaskOrchestrator, resource locks, deadlock detection and dispatch off the IAM hot path.
4. Add high-value audit records for manual device control, force-cancel, force-recovery, resource unlock and configuration changes.
5. Restrict emergency permissions to a minimal break-glass set.
6. Test Gateway/IAM outage while WCS Runtime continues operating.

Acceptance:

- IAM/Gateway failure cannot stop autonomous WCS runtime execution.

## Phase 7 - FreeIM identity integration

Tasks:

1. Use IAM GlobalUserId as the stable external user identity.
2. Protect platform-level IM capabilities with IAM permissions.
3. Keep conversation membership, friend/group/channel ACLs in the IM/business layer.
4. Validate IAM identity before issuing/returning the FreeIM connection token/address.
5. Verify WebSocket upgrade through `/im/ws/**`.

Acceptance:

- Platform users enter IM without a separate platform account while IM conversation rules remain autonomous.

## Phase 8 - Centralized closeout and operations

Tasks:

1. Move local user-management UI through Enabled -> ReadOnly -> Hidden where appropriate.
2. Retire duplicated platform roles while retaining local data-scope models.
3. Build permission matrices for operators/engineers/admin roles without hard-coding role checks in APIs.
4. Centralize authentication/authorization metrics: login failures, 401/403, IAM latency, cache hit rate, Shadow mismatches and token refresh failures.
5. Keep IAM security audit separate from MES/IoT/WCS business audit.
6. Run disaster tests: IAM down, Gateway down, DB down, cache lost, signing certificate rotation, network partition and token refresh failure.

Acceptance:

- All business systems use the same identity/security contract and can demonstrate documented degraded/offline behavior.
