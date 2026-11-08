# Tenancy

Custode stores all tenants in one shared database. Tenant-owned data is separated by a `TenantId` column, and the separation is enforced by the persistence layer rather than by individual features.

## Tenant-owned entities

Any entity that belongs to a tenant implements `ITenantOwned` (`src/Custode.Infrastructure/Tenancy/ITenantOwned.cs`) and exposes a `Guid TenantId`.

## Current tenant

`ITenantContext.TenantId` (`Guid?`) is the tenant bound to the current unit of work. A value of `null` means no tenant is bound.

## Read isolation: global query filter

`AppDbContext` applies a query filter to every `ITenantOwned` entity:

- Queries return only rows whose `TenantId` equals the current tenant.
- When no tenant is bound, the filter matches nothing, so queries return no rows (fail closed).
- Features do not add tenant conditions themselves.

## Write isolation: save-time check

Before `SaveChanges` / `SaveChangesAsync` writes anything, `AppDbContext` checks every added, modified, or deleted tenant-owned entity. The save fails with `TenantScopeViolationException` and nothing is written when:

- the entity's `TenantId` is empty;
- no tenant is bound, or the entity's `TenantId` differs from the current tenant;
- the row currently stored in the database belongs to a different tenant. This covers `Update()` or `Remove()` calls on an untracked entity that carries the current tenant's `TenantId` but targets another tenant's row by identifier.

## Tenant-independent operations (operators only)

Some operations act across tenants, for example provisioning a tenant. They must use `TenantIndependentQueries.QueryAllTenantsAsync<TEntity>(principal)`:

- The caller's `ClaimsPrincipal` must satisfy the `TenantPolicies.Operator` authorization policy. Otherwise the call throws `OperatorAccessDeniedException` and no query is built.
- Only this path disables the tenant filter (`IgnoreQueryFilters`). Regular feature code must not call it.
- The policy itself (which claims or roles count as an operator) is defined by the server host.

## Credential resolution (the only tenant-free read for unauthenticated requests)

Sign-in looks a user up by login, and refresh resolves the hash of a refresh token. Both run before a tenant is bound to the request, so they read without a tenant scope. They read only the identity or session columns needed to authenticate, and they bind the tenant from the resolved user or session. A client cannot supply a tenant to them. No other unauthenticated operation reads tenant data.

Identity users (`ApplicationUser`) are not tenant-owned entities, so the global query filter does not apply to them. Member queries filter by `TenantId` explicitly.

## Tests

The behavior above is covered by tests in `tests/Custode.Server.Tests/Persistence/` and `tests/Custode.Server.Tests/Tenancy/`, using SQLite in-memory databases with two tenants.
