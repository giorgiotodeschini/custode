# Proposal

## Why

The core server foundations (tenant isolation, local authentication, encryption at rest) are in place, but Custode still cannot store or organize the secrets it is meant to protect, nor restrict who may use them. The archived `core-server-architecture` change explicitly deferred concrete secret features to follow-up changes built on those foundations. This change delivers the first of those features: a typed, folder-organized secret model, folder-based access control for users and groups inside a tenant, and a tamper-evident security audit trail. It is the next step in the server-first order and blocks any webui or client work on secrets.

## What Changes

- Add a secret taxonomy: each secret has a type that is one of username/password, personal access token (PAT), API key, or file-based secret (for example `.crt`, `.pem`, `.pfx`). Secret values are stored through the existing `secret-encryption` requirements; no plaintext is persisted.
- Add a hierarchical folder model for secrets. Each secret belongs to exactly one folder identified by a folder path such as `/production/aws/`. Folders are tenant-owned and are created and renamed inside the tenant scope.
- Add tenant-scoped groups and group membership. Users belong to zero or more groups within their tenant; groups are the unit to which permissions are granted.
- Add folder-based access control. Permissions are granted to a user or group on a folder and are inherited by child folders and secrets. A folder may break inheritance, replacing the inherited grants for its subtree with its own explicit grants.
- Add granular permission levels on folders: **Read**, **Write**, **Create**, and **Delete**. An effective permission is the result of the nearest explicit ACL on the path, combined across the user's direct and group grants.
- Add an append-only security audit log. It records every critical action: secret access (including denied attempts), secret create/update/delete, ACL changes, group membership changes, and inheritance overrides. Each entry records the user, UTC timestamp, tenant, action, and target identifiers, and is hash-chained per tenant so that tampering is detectable.

Assumptions recorded for review (minor details, not scope-changing):

- Changing ACLs, groups, and folder structure requires a tenant administrator role. This role is an ASP.NET Core Identity role and is carried in the existing `roles` JWT claim.
- Permissions are allow-only. There are no explicit deny entries; "break inheritance" replaces the inherited grants instead.
- Reading a secret's value requires **Read** on its folder. Listing secret metadata also requires **Read**.

## Capabilities

### New Capabilities

- `secret-management`: The secret taxonomy (username/password, PAT, API key, file-based), the hierarchical folder-path organization of secrets, and the rules for creating, updating, and deleting secrets and folders within a tenant.
- `access-control`: Tenant-scoped groups and memberships, folder-based ACLs with inheritance and break-inheritance, the Read/Write/Create/Delete permission levels, and the evaluation of effective permissions for users.
- `security-audit`: The append-only, hash-chained audit log of critical actions, including what is recorded per entry and the guarantee that entries cannot be modified or removed through the application.

### Modified Capabilities

None. Existing requirements in `tenant-isolation`, `local-authentication`, `secret-encryption`, and `server-architecture` are not changed. This change builds on them: all new data is tenant-owned under `tenant-isolation`, secret values use the `secret-encryption` requirements, and all new slices follow `server-architecture`.

## Out of Scope

- Secret rotation, sharing with external parties, and secret version history.
- Per-tenant OIDC or external identity provider integration for groups (groups are local to a tenant in this change).
- Explicit deny ACL entries and permission levels beyond Read, Write, Create, and Delete.
- Audit log export, retention policies, SIEM integration, and an audit viewer.
- Web UI (Angular) and desktop client (.NET 10) implementation. They will be proposed after the server contracts are stable.
- Changes to hosting, deployment, or key-encryption-key provisioning.

## Impact

- **Components**: Server (.NET 10) only. Affected: `src/Custode.Server` (new feature slices under `Features/`) and `src/Custode.Infrastructure` (new tenant-owned entities, EF Core migration, shared audit writer). Webui and client are not affected by this change.
- **Code**: New slices for secret management, group management, ACL management, and audit querying. Existing slices are not modified.
- **Dependencies**: No new third-party packages are expected. ASP.NET Core Identity, Data Protection, EF Core, and JWT bearer authentication are reused.
- **Data**: New tenant-owned tables for secrets, folders, groups, group memberships, folder ACL entries, and audit entries. Every table carries `TenantId` and uses the existing global query filter. Secret values are stored only encrypted through the shared data protection provider.
- **Security**: Security-critical. Access control and audit immutability must be covered by specification scenarios and tests that run on both SQLite and PostgreSQL (Testcontainers), as required by the project guidelines.
