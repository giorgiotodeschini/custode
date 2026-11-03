# Proposal

## Why

Custode is a secure, self-hosted, multi-tenant secrets manager. Before any feature work, the server needs a defined foundation: how code is organized, how tenants are isolated, how users authenticate, and how secrets are protected at rest. Establishing these as specifications first ensures every later server feature is built on the same contracts. This is the first change in the workflow order (server → webui → client), so it blocks all other work.

## What Changes

- Define the server code organization: .NET 10 Vertical Slices on top of shared infrastructure (EF Core, ASP.NET Core Data Protection).
- Define the multi-tenancy model: a single shared database where every tenant-owned entity carries a `TenantId` discriminator, and no tenant-owned data is accessible without a tenant scope.
- Define local authentication: user credentials managed by ASP.NET Core Identity, with JWT issued on successful sign-in. Per-tenant OIDC is out of scope for this change and is only kept compatible with the design.
- Define data security: secret values are encrypted at rest using ASP.NET Core Data Protection, backed by its key-ring infrastructure.

This is a new system, so there are no existing requirements to modify and no **BREAKING** changes.

## Capabilities

### New Capabilities

- `server-architecture`: How the server is structured. Vertical Slices per feature, shared infrastructure modules, and the rules for what a slice may depend on.
- `tenant-isolation`: How tenants are modeled and kept apart in the shared database through the `TenantId` discriminator, including scoping of queries and writes.
- `local-authentication`: How users register and sign in with local credentials via ASP.NET Core Identity, and how JWTs are issued and validated.
- `secret-encryption`: How secret values are encrypted at rest with ASP.NET Core Data Protection, including key-ring management and the storage rules for protected values.

### Modified Capabilities

None. No existing capabilities.

## Out of Scope

- Web UI (Angular) implementation.
- Desktop client (.NET 10) implementation.
- Per-tenant OIDC configuration (reserved for future phases; this change must not block it).
- Concrete secret features (create, read, rotate, share, audit). They will be proposed as separate changes on top of these foundations.
- Deployment, hosting, and infrastructure choices (container images, reverse proxy, TLS termination).

## Impact

- **Components**: Server Core (.NET 10) only.
- **Code**: new server solution structure; no existing code is affected.
- **Dependencies**: ASP.NET Core Identity, ASP.NET Core Data Protection, EF Core, JWT bearer authentication.
- **Data**: a shared database schema with `TenantId` on tenant-owned tables; the key ring used by Data Protection must be persisted and not stored alongside the encrypted data in plaintext.
- **Security**: tenant isolation and secret encryption are security-critical and must be covered by specification scenarios that the implementation is tested against.
