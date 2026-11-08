# Design

## Context

The server implements tenant isolation, local sign-in that issues an access token (`src/Custode.Infrastructure/Authentication/JwtTokenIssuer.cs`, claims `sub`, `tenant_id`, `role`, lifetime from `Jwt:LifetimeMinutes`), folder and secret endpoints, groups and grants, and the audit trail. Three facts from the code shape this design:

- Sign-in looks the user up through `UserManager` by login, which is not tenant-scoped. `ApplicationUser` is `IdentityUser<Guid>` with a `TenantId` column, but it does not implement `ITenantOwned`, so the global query filter does **not** apply to Identity users. Every member query must filter by tenant explicitly.
- The audit chain starts from an empty previous hash (`AuditTrail` sets `LastHash = string.Empty`) and hashes with lowercase hexadecimal SHA-256 (`AuditHash`). Exposing these fields requires no change to the chain.
- Secrets are stored as one protected payload per row (`Secret.ProtectedPayload`), and an update overwrites it in place. Versioning therefore has to snapshot the previous state before every overwrite. Wire type names are `username-password`, `pat`, `api-key`, and `file` (`SecretValueCodec`).

Requirements are in `specs/`. See `proposal.md` for the motivation and the list of breaking changes.

## Goals / Non-Goals

**Goals:**
- Make every session revocable immediately, including the access tokens already issued for it.
- Keep secret plaintext out of storage that is not protected by the existing data protection key ring, except for the passphrase-encrypted backup, which is produced on request and never persisted.
- Keep every new behavior inside a vertical slice and reach shared behavior only through published contracts in `Custode.Infrastructure`.
- Make each new critical action audited in the same transaction as the change it records.

**Non-Goals:**
- Backup restore, streaming backup, and version pruning.
- Non-browser refresh transport (for the desktop client).
- Rate limiting, tenant-prefixed logins, and external identity providers.

## Decisions

### D1. Sessions and refresh tokens are one row per token

The model is two tables, both tenant-owned (`ITenantOwned`):

- `Session`: `Id`, `TenantId`, `UserId`, `StartedAtUtc`, `LastActivityAtUtc`, `RevokedAtUtc`, `RevocationReason`. One row per sign-in.
- `RefreshToken`: `Id`, `TenantId`, `SessionId`, `TokenHash`, `IssuedAtUtc`, `ReplacedAtUtc`, and a `ConcurrencyStamp` (a `Guid`, mapped as a concurrency token). One row for every refresh token the session has ever issued.

- Refresh tokens are 32 random bytes from `RandomNumberGenerator`, encoded as base64url. Only the SHA-256 hash is stored, so a database read cannot be replayed as a token.
- The access token's `sid` claim is the session identifier. Signature validation stays in the JWT bearer handler; the session check is a separate step (D4).
- Why one row per token: the first version kept only the current and the previous hash in the session row. That cannot detect reuse of a token replaced two or more rotations earlier, which the spec requires. Keeping every token hash makes any replaced token recognizable.
- Alternative: signed refresh tokens (JWT). Rejected: a signed token cannot be revoked without server state.

### D2. Rotation with a per-token reuse grace window

A refresh request resolves the token hash to its row (`SessionCredentialResolver`, D6) and then decides:

1. The token is not replaced and the session is active, not idle, and within its absolute lifetime: set `ReplacedAtUtc` on the presented row (its `ConcurrencyStamp` changes), insert the new token row, save, and return a new access token and a new cookie.
2. The token is replaced: if `now - ReplacedAtUtc` is within the grace window (default 10 seconds, `Sessions:ReuseGraceSeconds`), return unauthorized without changes. Otherwise write the denied audit entry `refresh token reuse detected`, revoke the session with reason `reuse-detected`, and return unauthorized.
3. Session revoked or token unknown: unauthorized, no changes.

Two refreshes of the same token that reach the save together: the second save fails on the token row's concurrency stamp, and the request is refused without revoking the session.
- Rationale: a browser's cookie is shared by all tabs, so two tabs can refresh at once. Without a grace window that race would end the session.
- Trade-off: within the window a genuinely stolen token is also refused, but the session survives. After the window the reuse ends the session.

### D3. Cookie and request-header protections

The refresh cookie is `custode_refresh`, with `HttpOnly`, `Secure`, `SameSite=Strict`, and `Path=/api/auth`. The `__Host-` prefix is not used because it requires `Path=/`, which would send the cookie to every API request.

`POST /api/auth/refresh`, `POST /api/auth/sign-out`, and `POST /api/auth/activity` require the header `X-Custode-Request`. A cross-origin page cannot send it without a CORS preflight, and the server allows no cross-origin requests. `SameSite=Strict` already prevents cross-site cookie delivery; the header is a second barrier.

### D4. Every authenticated request checks its session

A check runs after JWT validation, for every request carrying a bearer token. It reads three things, each with an indexed or tenant-scoped query: the session named by `sid`, the user's `IsEnabled` and `MustChangePassword` flags (filtered by `TenantId`, because Identity users are not covered by the global filter), and the tenant policy (idle timeout and refresh lifetime). It rejects the request when the session is missing, revoked, idle beyond the timeout, past its absolute lifetime, or its user is disabled, and when the token has no `sid`.

- Activity: when the last recorded activity is more than 60 seconds old, the check writes the new value. This bounds writes to one per minute per session. Idle detection can therefore be up to a minute late; the spec accepts this.
- Cost: three queries per authenticated request (session, user, policy). The first version of this design counted two; the policy read was added so the lifetimes follow the current policy without a cache. No cache is used, so revocation takes effect on the next request.
- Restricted sessions (`MustChangePassword`): a middleware rejects every endpoint except change-password and sign-out with forbidden and the reason "password change required". The flag is read from the user row, not from the token claim, so the restriction ends the moment the password changes.

### D5. Token lifetime comes from tenant policy

`JwtTokenIssuer` takes the access token lifetime from the tenant policy (D9) and falls back to `Jwt:LifetimeMinutes` when no policy row exists. The issuer stays separate from the session store: the session is created by the sign-in handler, and the issuer receives the session identifier as a parameter.

### D6. Tenant-free credential resolution

Sign-in keeps its tenant-free lookup by login, which the existing code already performs through `UserManager`. The refresh path adds one explicit tenant-free read: `SessionCredentialResolver.CredentialQuery` selects `Id` (the token), `SessionId`, `TenantId`, and `ReplacedAtUtc` for the given hash, with `IgnoreQueryFilters()` and an unscoped context. It is not an operator operation, so it does not go through `TenantIndependentQueries`. A test checks the generated SQL: the select list contains only those columns and not the hash, the issue time, or the concurrency stamp.

### D7. Password change and first sign-in

`POST /api/auth/change-password` verifies the current password with `UserManager.CheckPasswordAsync`, applies `ChangePasswordAsync`, which enforces the identity password policy and stores only a hash, clears `MustChangePassword`, and revokes the user's other sessions (D8). Each revoked session writes an audit entry. The request runs in the session that is changing the password, so that session stays active.

### D8. Revocation is a published contract

`ISessionRevoker` in `Custode.Infrastructure/Sessions/` is the contract other slices use to end sessions. `SessionService` implements it, and it is registered under that interface. Each revocation writes its audit entry in the same unit of work. Member disablement, administrator withdrawal, and password change depend on the interface, never on each other's internals.

### D9. Tenant policy is a tenant-owned row with defaults

`TenantPolicy` is one row per tenant, implements `ITenantOwned`, and holds the four values with their ranges. A missing row means defaults: idle 15 minutes, access token 15 minutes (or `Jwt:LifetimeMinutes`), refresh token 7 days, clipboard clear 30 seconds. Validation returns the offending field name. Policy changes write `tenant policy changed` in the same transaction.

### D10. Members and roles

`ApplicationUser` gains `IsEnabled` (default true) and `MustChangePassword` (default false). Members are created with `UserManager.CreateAsync(user, initialPassword)`, so the identity password policy applies. The tenant administrator role is added and removed with `AddToRoleAsync` and `RemoveFromRoleAsync`.

Every member query filters by `TenantId` explicitly, because the global filter does not cover Identity users (see Context).

The last-administrator rule counts the enabled administrators of the tenant and applies the change in one unit of work. Concurrent administrative changes in the same tenant write to the per-tenant audit chain head, which has a concurrency stamp (`AuditChainHead`). That serializes them, so two changes cannot each see another administrator and both remove one. The loser retries.

### D11. Secret versions

A `SecretVersion` entity implements `ITenantOwned` and holds `Id`, `TenantId`, `SecretId`, `Number`, `Name`, `FileName`, `ProtectedPayload`, `ChangeKind` (`created`, `updated`, `rolled-back`), `SourceVersionNumber`, `CreatedAtUtc`, and `CreatedByUserId`. The pair `(SecretId, Number)` is unique.

`Secret` gains `CurrentVersion` (int) and `ConcurrencyStamp` (Guid).

- Update: check `If-Match` against `CurrentVersion` before doing any work (missing: 428; mismatch: 412). Then, in one unit of work with the audit entry, insert the new version with the new payload, set `CurrentVersion`, and change the stamp. A concurrent update fails on the stamp, which is mapped to 412.
- Rollback: copy the selected version's `ProtectedPayload` and name into a new version. Both payloads are protected for the same tenant purpose, so the rollback copies the protected bytes without decrypting them. Key rotation stays transparent, because the data protection key ring keeps older keys.
- Move: changes only `FolderId`, so it does not create a version and does not change `CurrentVersion`.
- Delete: cascades to versions.
- Migration: `ServerPrerequisitesWebUi` creates the version table and inserts version 1 for each existing secret with `SELECT ... FROM Secrets`, so the history starts from the current state.
- Storage: every version keeps a full payload. A large file secret is therefore stored once per version. Pruning is out of scope.

Why a `Guid` stamp rather than `rowversion`: EF Core's `rowversion` is not available on SQLite, and the tests run on SQLite.

### D12. Structured and key-value secrets

The wire types are `structured` and `key-value`. `SecretType` gains `Structured` and `KeyValue`. Both are stored in the protected payload as an ordered list of `{ key, value }` pairs. The validator works on the request before storing:

- `structured` takes a JSON object. Duplicate property names are rejected by scanning `JsonDocument` properties, because `System.Text.Json` deserialization into a dictionary would silently keep one of them.
- `key-value` takes an array of `{ key, value }`, keeps the order, and rejects duplicate keys.
- Limits: names 1 to 128 characters, values up to 4096 characters, 100 fields for structured and 200 pairs for key-value.

The request DTO takes the structured body as a raw `JsonElement`, so the validator can see duplicates before any binding happens.

### D13. Search runs in the application over metadata

`GET /api/search` (slice `Features/SecretManagement/Search/`) loads folder paths and secret names for the tenant and matches them in the application with `string.Contains(query, StringComparison.OrdinalIgnoreCase)`. Wildcards are literal because no SQL pattern is built, and this avoids dialect differences between SQLite and PostgreSQL in case-insensitive matching.

Visibility comes from `IFolderAccess` with a batch method added for this slice, so the effective permissions of all folders are computed once per request. Folders are returned for any effective permission; secrets need Read on their folder. The scan stops after 5,000 candidates and reports truncation.
- Trade-off: the scan is linear in the number of secrets of the tenant, and it returns names only. Trigram indexes can be added later without changing the contract.

### D14. Read contracts for access control

Read endpoints live in `Features/AccessControl/Reads/`:

- Groups and members: restricted to tenant administrators with the existing `TenantAdministrator` policy, through the same filter pattern as the existing mutations. They are not audited, because they are not critical actions.
- Folder grants: the grants of the folder and of each ancestor for which inheritance has not been broken, grouped by principal. Each principal has its sources, each with the holding folder path and levels, and the union of the sources as effective levels. The union is the same rule `IFolderAccess` already applies.
- Inheritance status: each folder's path, parent, and inheritance flag.
- Caller effective permissions: `IFolderAccess` for the caller. An empty result or a missing folder gives not found, which matches the existing rule for inaccessible folders.

### D15. Audit enhancements

Audit entries expose `Hash` and `PreviousHash` as stored, which requires no change to the chain. The first entry's `PreviousHash` is the empty string, matching the existing genesis value.

Pagination is keyed on `Sequence`, which is unique per tenant. The query takes `Sequence > afterSequence` in ascending order, reads `limit + 1` rows to decide whether a next page exists, and returns `{ entries, nextAfterSequence }`. The filters combine with the cursor. This is the breaking envelope change named in the proposal.

### D16. Bulk import with a dry run

`POST /api/secrets/imports` (slice `Features/SecretManagement/Import/`) runs the same pipeline for both modes:

1. Reject the batch if it has more than 500 rows, or the body is larger than the configured maximum. This happens before any row is read.
2. Resolve each distinct target folder path: no effective permission gives `folder-not-found`, which is also the result for a missing folder; Read without Create gives `forbidden`. With `createMissingFolders`, a missing path is allowed when Create is held on its nearest existing ancestor.
3. Check names: duplicates within the batch and existing names in the folder give `conflict`.
4. Validate each row with `SecretValueCodec`, catching the validation exception per row. Validation messages are static strings that never contain a value.

A dry run returns the report. A commit proceeds only when every row is `valid`. The commit then runs in one unit of work (`AuditedWork`): folders, then secrets, each with its audit entry, and one `secrets imported` entry whose secondary target is a batch identifier generated for the commit. If a `forbidden` row exists, the commit is refused with forbidden, and one `denied` entry is recorded for the attempt.

### D17. Encrypted tenant backup

`POST /api/backups` (slice `Features/TenantBackup/`) runs in this order:

1. The `TenantAdministrator` filter runs first and records a denied entry if the caller lacks the role.
2. The caller must hold Read on every folder of the tenant. If not, the response is forbidden and a denied entry is recorded.
3. The passphrase is normalized with Unicode Normalization Form C and must have at least 12 Unicode code points. Otherwise the request is rejected before any content is read.
4. Content is collected and decrypted in memory: folders, secrets with versions, groups, memberships, grants, policies, members (login, roles, enabled state, and no password hash), and audit entries with their hashes and the chain head. The content is serialized as UTF-8 JSON and capped at the configured size (default 256 MiB), which returns payload too large if exceeded.
5. The key is derived with `Rfc2898DeriveBytes.Pbkdf2(passphraseBytes, salt, 600000, HashAlgorithmName.SHA256, 32)`, where the salt is 32 random bytes (OWASP recommends 600,000 iterations for PBKDF2-HMAC-SHA256). The content is encrypted with `AesGcm` using a 12-byte nonce and a 16-byte tag, and the header is passed as associated data.
6. The response is `application/octet-stream` with `Cache-Control: no-store` and a `Content-Disposition` attachment name that includes the UTC timestamp.

File layout (format version 1):

| Field | Size | Value |
|---|---|---|
| Magic | 8 bytes | ASCII `CUSTBAK` followed by a zero byte |
| Format version | 1 byte | `0x01` |
| KDF identifier | 1 byte | `0x01` (PBKDF2-HMAC-SHA256) |
| Iterations | 4 bytes | big-endian, `600000` |
| Salt | 32 bytes | random |
| Nonce | 12 bytes | random |
| Ciphertext | variable | AES-256-GCM output |
| Tag | 16 bytes | AES-GCM tag |

The header (the first 58 bytes) is the associated data. Any change to it makes decryption fail. A reader rejects an unknown format version without attempting decryption.

The passphrase is never stored or logged, and the request body is excluded from request logging (the existing rule in the core change, D8).
- Alternative: encrypt in the browser. Rejected: the browser would have to receive the whole tenant's plaintext first, which the web UI design rejects (D8 in `web-ui-architecture`).

### D18. Audit entries for every new critical action

Each new action is written in the same unit of work as the change it records, through `IAuditTrail`. Denied attempts of admin-only operations go through `TenantAdministratorFilter`, as existing access-control changes do. Operations whose denial comes from a folder permission write a `denied` entry from the slice, as secret reads do.

## Risks / Trade-offs

- **[Per-request session check adds three queries]** → Each query is indexed or tenant-scoped. The activity write is throttled. Measure on PostgreSQL before any cache is considered; a cache would delay revocation, which the spec does not allow.
- **[Cross-tab refresh races end sessions]** → The grace window (D2) absorbs them. The web UI must retry once after a grace-window 401 with the cookie the browser now holds. This is a follow-up for the web UI design.
- **[Logins are globally unique]** → A tenant administrator who creates a member learns that the login exists in another tenant (a conflict response). The existing sign-in has no tenant field, so logins must stay global in this change. **Risk accepted and recorded as an open question.**
- **[Backups are held in memory]** → The 256 MiB cap bounds the memory used. Streaming format version 2 is a later change.
- **[Versions multiply file storage]** → Each version keeps its payload. Pruning is out of scope and is recorded for the operations design.
- **[Stale access tokens after role withdrawal]** → Withdrawal revokes the member's sessions (D10 and the tenant-administration spec), so no stale token is accepted after the next request.
- **[Breaking changes]** → The audit envelope and the `If-Match` requirement break existing tests. They are updated in the tasks, and the web UI design must adopt 412 (see the proposal).
- **[Passphrase in memory]** → .NET strings cannot be wiped reliably. The passphrase lives only for the request, and it is not logged or stored.

## Migration Plan

Migration `ServerPrerequisitesWebUi` (PostgreSQL, generated and checked with `dotnet ef migrations has-pending-model-changes`):

1. Create `Sessions`, `TenantPolicies`, and `SecretVersions`, with their indexes.
2. Add `IsEnabled` and `MustChangePassword` to the identity users table with defaults that keep existing users enabled and not flagged.
3. Add `CurrentVersion` and `ConcurrencyStamp` to `Secrets`.
4. Insert version 1 for each existing secret from its current row.

SQLite does not run migrations; its schema is created by `EnsureCreated` in the test harness, as decided in the core change (D9).

Deployment order: apply the migration, then start the server. Existing access tokens lack `sid` and are rejected, so clients sign in once.

Rollback: before any secret version or session exists, the migration can be reverted by dropping the new tables and columns. After secret versions exist, rollback would delete history, so the table stays and the fix is forward. Session and policy tables may be dropped safely on rollback, because sessions are ephemeral and policies fall back to defaults.

## Deviations from the initial design

Recorded after implementation so the artifacts match the code:

- D1, D2, D4, D6, D8 are rewritten above. The token model changed from one session row with current and previous hashes to one row per refresh token.
- The administrator-role denial is recorded with the action `administrator role changed`. The spec does not fix a name for denied attempts, and the filter cannot know the direction of the request from the route.
- The backup endpoint relies on the endpoint filter alone for the administrator check, without a policy-level `RequireAuthorization`. A policy rejection runs before filters and would skip the denied-attempt audit entry.
- `TenantAdministratorFilter` moved from the access-control slice to `Custode.Server/Authorization/`, because more than one slice uses it and slices must not depend on each other's internals.
- Role changes (grant and withdrawal) write through the database context rather than `UserManager`, because `UserManager` saves on its own; the last-administrator check and the change must share one unit of work with the audit chain head.
- The migration was reviewed before use and corrected in three places: `IsEnabled` defaults to true (the first generated default was false, which would have disabled every existing user), `CurrentVersion` defaults to 1, and the version-1 backfill is an explicit SQL statement.
- Backup content is serialized with camelCase property names, consistent with the rest of the API.

## Consumers of this change

These follow-ups were applied to the `web-ui-architecture` change:

- Version mismatch is `412 Precondition Failed`, not `409`, in the web UI design and the secret editor spec.
- The web UI calls `POST /api/auth/activity` during user interaction, at most once per minute, and on "Stay signed in", so the server's idle timeout matches the client countdown (spec `webui-session-security`, design D3).
- Idle timeout, access token lifetime, and clipboard clear delay come from `GET /api/tenant/policies`.

## Open Questions

- Whether logins should be tenant-prefixed to remove the cross-tenant login existence signal. This changes the sign-in contract, so it needs its own change before member creation is used by more than one tenant.
- The non-browser refresh transport for the desktop client. This is decided in its own change.
