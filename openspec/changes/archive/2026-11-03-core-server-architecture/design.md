# Design

## Context

The repository currently contains only OpenSpec configuration; there is no server code yet. The server is the first component in the workflow order, so these decisions set the structure every later feature will follow. Requirements are defined in `specs/` (server-architecture, tenant-isolation, local-authentication, secret-encryption); this document explains how they are realized and why.

## Goals / Non-Goals

**Goals:**
- Give every feature a single place to live (one slice) without duplicating persistence, identity, or data protection setup.
- Make tenant isolation enforced by the infrastructure, not by each feature remembering to filter.
- Keep secret encryption keys protected even if the database is leaked.

**Non-Goals:**
- Choosing hosting, container, or reverse-proxy topology.
- Per-tenant OIDC flows (only the token boundary is prepared for them).
- Multi-instance key coordination beyond what is needed for a single self-hosted deployment in this phase.
- Secret business features (CRUD, sharing, audit); they are separate changes.

## Decisions

### D1. Project layout: one host, feature folders, shared infrastructure project
Use a single ASP.NET Core host project plus a shared infrastructure library. Each slice is a folder under `Features/<Feature>/` containing its endpoint, request/response types, handler, and validation. Shared infrastructure (`Custode.Infrastructure`) holds the `DbContext`, Identity configuration, tenant scoping, and Data Protection setup.
- Alternative: one project per slice. Rejected for this phase: adds build and reference overhead without an isolation benefit, since slices are kept apart by the rule in `server-architecture`, not by assemblies.
- Alternative: classic layered architecture (Controllers/Services/Repositories). Rejected: it conflicts with the required vertical slice organization.

### D2. Mediator-free handlers, composed explicitly
Each slice exposes a minimal API endpoint that calls its handler directly. Slices register themselves through a single composition method per slice, called from `Program.cs`. No reflection-based scanning of slices.
- Alternative: a mediator library for dispatch. Rejected to avoid an extra dependency and implicit wiring; the explicit list satisfies the "composition is explicit" requirement.

### D3. Single `AppDbContext` with a tenant query filter
A single `DbContext` (EF Core) backs all data. Every tenant-owned entity implements a common `ITenantOwned` marker with a `TenantId` property. The context applies a global query filter on `TenantId` equal to the current tenant from a scoped `ITenantContext`. On save, new tenant-owned entities get their `TenantId` from `ITenantContext`, and an entity whose `TenantId` does not match the current tenant is rejected.
- Enforcement is in the infrastructure, so a slice cannot forget the filter.
- Tenant-independent operations (tenant provisioning) use a separate, explicitly named query path that bypasses the filter and requires an operator policy (satisfies the `tenant-isolation` requirement on tenant-free operations).
- Alternative: manual `Where(x => x.TenantId == ...)` in each query. Rejected: relies on discipline, violates the SOLID/"zero vibe coding" intent of making violations structurally impossible.
- Alternative: database-per-tenant. Rejected by the core spec (shared database).

### D4. Tenant resolution from the token only
`ITenantContext` is populated from the `tenant_id` claim of the validated JWT. The middleware never reads tenant values from headers, route, or body. A request whose token has no tenant claim is rejected before reaching a slice.

### D5. ASP.NET Core Identity with JWT bearer validation
Users, password hashes, and roles are stored through ASP.NET Core Identity on the same `AppDbContext`. On sign-in, a token service issues a signed JWT containing `sub`, `tenant_id`, roles, and a short expiration. Protected endpoints use the JWT bearer authentication scheme. Endpoints are secure by default; an explicit `AllowAnonymous` marker is required for public endpoints.
- Token issuance is isolated behind an `ITokenIssuer` interface, and token consumption depends only on the standard bearer validation and the claims principal. This is what keeps per-tenant OIDC addable later: a new issuer can be trusted in addition to the local one without changing slices.
- Alternative: cookie authentication. Rejected: the desktop client and future API consumers need bearer tokens.

### D6. Secret encryption via ASP.NET Core Data Protection with a persisted, wrapped key ring
Secret values are encrypted through `IDataProtectionProvider` using a purpose string that combines a fixed prefix with the tenant identifier (e.g., `custode.secrets.v1|<tenantId>`). This binds ciphertext to both its purpose and its tenant, satisfying the purpose-binding and tenant-scope requirements.

Key ring persistence: the key ring is persisted to the database through `PersistKeysToDbContext`, so the key ring lives with the data it protects, and it is encrypted at rest with a key-encryption key (KEK) that is **not** stored in the database. The KEK is an X.509 certificate loaded from the host's certificate store or a file path supplied by the operator at deployment. If the KEK is unavailable, the server does not start secret operations (satisfies the "key ring is missing" scenario).
- Rationale: a database backup alone must not reveal secrets; the key ring is useless without the KEK.
- Alternative: key ring on the local filesystem. Rejected as the default: it fragments across containers and is easy to lose on redeploy; it remains an option for a single-node setup via configuration.
- Alternative: no KEK (plain key ring in the DB). Rejected: a database leak would expose every secret.
- Key rotation uses Data Protection's native key lifetime and default key activation; old keys remain in the ring so existing secrets stay readable.

### D7. Logging and error policy
Secret plaintext never enters logging or exception messages. Request logging for secret slices must record only identifiers. This is enforced by a shared rule in the secret-handling code path and reviewed in the tasks for those slices.

## Risks / Trade-offs

- **[Global query filters can be bypassed]** → Only the explicitly named tenant-independent query path disables the filter, and it is gated by the operator policy. A test verifies that a regular query cannot return another tenant's rows.
- **[Key ring loss makes all secrets unreadable]** → Operators must back up the KEK certificate separately; the server logs a clear error at startup when the KEK or key ring is missing. Documented as an operational requirement in the tasks.
- **[Single shared database couples tenants at the infrastructure level]** → Accepted by the core spec. Mitigated by the query filter, the save-time check, and tests covering cross-tenant access.
- **[JWT revocation is not immediate]** → Short token lifetime bounds exposure. Revocation/refresh is deferred and noted as a follow-up.
- **[Explicit composition adds one edit per new slice]** → Small cost in exchange for visible wiring; acceptable at this scale.

## Migration Plan

This is a new system; there is no data to migrate. Deployment order:
1. Provision the KEK certificate on the host and configure its location.
2. Apply the initial EF Core migration (Identity tables, tenant table, key ring table, tenant-owned tables).
3. Start the server; it verifies the KEK and key ring before serving secret operations.

Rollback: the initial migration is reversible in an empty database. After secrets exist, rollback requires keeping the key ring and KEK; they must not be deleted.

## Open Questions

- Whether multi-node self-hosted deployments need a shared key ring beyond the database-persisted ring. This does not change the specs and can be decided with the first multi-instance requirement.
