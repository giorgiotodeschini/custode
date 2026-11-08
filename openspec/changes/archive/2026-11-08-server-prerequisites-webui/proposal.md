# Proposal

## Why

The `web-ui-architecture` change fixed the web UI's structure and behavior, but several of its requirements depend on server contracts that do not exist yet: refresh tokens, secret version history with optimistic concurrency, structured and key-value secrets, bulk import and encrypted backup, member and policy management, ACL read endpoints with effective permissions, name search, and exposed audit hash links with cursor paging. The server-first order in `openspec/config.yaml` requires these contracts to be specified and applied before the dependent web UI tasks can start. This change specifies them.

## What Changes

- **Refresh sessions**: sign-in issues a refresh token as an HTTP-only, Secure, same-site cookie. Refresh rotates it, reuse of a rotated token ends the session, and sign-out revokes it. Sessions end on tenant idle timeout and absolute lifetime, and a disabled account loses access on its next request.
- **Secret version history**: every update creates a version. Versions can be listed, read with Read permission, and rolled back as a new version with Write permission. Updates require an `If-Match` version token; a stale token returns `412 Precondition Failed`.
- **Structured and key-value secret types**: two new types, `structured` (named string fields) and `key-value` (ordered unique pairs), added beside the four existing types.
- **Bulk import with dry run**: an atomic JSON import endpoint that validates every row and reports per-row results. A dry run writes nothing. A commit stores all rows or none.
- **Encrypted tenant backup**: a tenant administrator can export the tenant's vault state, protected by a passphrase-derived key (PBKDF2-HMAC-SHA256 and AES-256-GCM). Credentials are excluded.
- **Tenant member and policy management**: members are created with an initial password that must be changed at first sign-in, can be disabled and re-enabled, and can be granted or stripped of the tenant administrator role, with the last-administrator protected. Tenant-wide policies cover idle timeout, access token lifetime, refresh token lifetime, and clipboard clear delay.
- **ACL read endpoints**: administrators can read groups, group members, folder grants with their effective permissions and sources, and folder inheritance status. Any caller can read their own effective permissions on a folder.
- **Global search**: a name search over folders and secrets that returns only items the caller can see.
- **Audit log enhancements**: each entry exposes its hash and previous hash, and the audit query uses sequence-based cursor pagination.

### Breaking changes

- **BREAKING**: `PUT /api/secrets/{id}` requires an `If-Match` header carrying the secret's version token. Requests without it are rejected with `428 Precondition Required`.
- **BREAKING**: `GET /api/audit` returns an envelope `{ entries, nextAfterSequence }` instead of a bare array.
- **BREAKING**: access tokens issued before this change carry no session identifier and are rejected after the migration. Clients must sign in again once.

### Changed assumption in the web UI

The web UI design (`web-ui-architecture`, D9 and task 5.13) assumed `409 Conflict` for a stale version. This change uses `412 Precondition Failed`, which is the HTTP status for a failed `If-Match` condition. The web UI design must be updated when its prerequisite is applied.

## Capabilities

### New Capabilities

- `session-management`: refresh session issuance, rotation, reuse detection, idle and absolute lifetimes, sign-out revocation, and per-request session validation.
- `secret-versioning`: version history for secrets, version reads, rollback, and version tokens for optimistic concurrency.
- `secret-transfer`: atomic bulk import of secrets with dry-run validation and per-row reporting.
- `tenant-backup`: passphrase-encrypted export of a tenant's vault state.
- `tenant-administration`: tenant member lifecycle, tenant administrator role assignment, and tenant-wide security policies.

### Modified Capabilities

- `local-authentication`: the issued token gains a session identifier and its lifetime comes from tenant policy. Initial passwords must be changed at first sign-in, and users can change their own password.
- `secret-management`: the supported secret types are extended with `structured` and `key-value`. Secrets can be found by name across folders and secrets.
- `access-control`: read contracts for groups, memberships, folder grants with effective permissions, inheritance status, and caller effective permissions.
- `security-audit`: the list of critical actions grows to cover the new operations, audit entries expose their hash links, and the audit query is cursor-paged.
- `tenant-isolation`: credential resolution for unauthenticated sign-in and refresh is explicitly defined as the only tenant-free lookup, and it binds the tenant from the resolved credential.

## Out of Scope

- Web UI (Angular) implementation details. Changes to the web UI design are listed above as follow-ups.
- Desktop client (.NET 10) implementation. A non-browser client will need its own refresh transport decision.
- Production CI/CD, containerization, and deployment configuration.
- Backup restore. This change specifies an export format that is versioned for a future restore operation.
- Secret rotation, version pruning, and retention policies. All versions are kept.
- Per-secret ACLs, explicit deny entries, and external identity providers.
- Rate limiting and brute-force protection for sign-in and refresh.
- Changes to the key ring, key rotation, or the audit trail's chain algorithm. The chain format is unchanged; only its fields are exposed.

## Impact

- **Components**: Server Core (.NET 10) only. The web UI and desktop client are unchanged by this change. Their contracts are affected as described above.
- **Code**: new feature slices under `src/Custode.Server/Features/` for sessions, password change, tenant administration, tenant backup, secret versions, secret import, and secret search. Changes to the existing sign-in, secret update and read, audit query, and access-control slices. Shared contracts for session validation and the tenant policy are added to `src/Custode.Infrastructure/`.
- **Data**: EF Core migration `ServerPrerequisitesWebUi` adds the sessions and tenant policy tables, the secret version table, the `CurrentVersion` column on secrets, and identity columns for enabled state and password change. Existing secrets receive version 1 from their current payload in the migration. The audit tables are unchanged.
- **APIs**: new endpoints for sessions, password change, members, policies, search, import, backup, versions, and access reads. Two breaking changes (above).
- **Dependencies**: no new third-party packages. ASP.NET Core Identity, Data Protection, EF Core, JWT bearer, and the .NET cryptography APIs (PBKDF2 via `Rfc2898DeriveBytes`, `AesGcm`) are used. The .NET APIs and PBKDF2 parameters are verified in `design.md`.
- **Security**: security-critical. Refresh tokens are stored only as hashes. Backups are encrypted and exclude credentials. Every new critical action is audited, and denied attempts are recorded where the existing rules require it.
- **Testing**: every new behavior is tested on SQLite and PostgreSQL 18 (Testcontainers), as required by `AGENTS.md`.
