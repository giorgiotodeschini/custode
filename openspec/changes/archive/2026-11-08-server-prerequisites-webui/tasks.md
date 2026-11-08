# Tasks

Scope: server only (`src/Custode.Infrastructure`, `src/Custode.Server`, `tests/Custode.Server.Tests`). The web UI and the desktop client are out of scope. Every behavior test runs on SQLite and PostgreSQL 18 (Testcontainers) unless stated otherwise. Each task maps to the spec requirement it implements. The design decisions are referenced as `D<n>`.

## 0. Conventions and verification of external APIs

- [x] 0.1 Add the two-line copyright header from `AGENTS.md` (rule 5) to every new or modified `.cs` file under `src/` and `tests/`, excluding the generated migration and `*.Designer.cs` files. Verify with a search over the changed files. (project rule 5)
- [x] 0.2 Verify the .NET APIs used by this change against the official documentation before coding: `Rfc2898DeriveBytes.Pbkdf2`, `AesGcm` with an explicit tag size, `JsonDocument` property enumeration, and the `UserManager` password change methods. (design D11, D12, D17; project rule on external APIs)

## 1. Persistence model and migration

- [x] 1.1 Add `IsEnabled` (default true) and `MustChangePassword` (default false) to `ApplicationUser`. Verify that existing users stay enabled and unflagged. (tenant-administration, "Members can be disabled and enabled"; local-authentication, "Initial passwords require a change at first sign-in")
- [x] 1.2 Add the tenant-owned `Session` entity (`Id`, `TenantId`, `UserId`, start and activity times, revocation fields) and the tenant-owned `RefreshToken` entity (one row per issued token, with `TokenHash`, `ReplacedAtUtc`, and a concurrency stamp). Index the token hash uniquely and the session lookups by tenant and user. (session-management, "Sign-in starts a refresh session"; design D1)
- [x] 1.3 Add the tenant-owned `TenantPolicy` entity with one row per tenant. (tenant-administration, "Tenant security policies are validated and tenant-wide"; design D9)
- [x] 1.4 Add the tenant-owned `SecretVersion` entity with the unique `(SecretId, Number)` index. Add `CurrentVersion` and `ConcurrencyStamp` to `Secret`, and add `Structured` and `KeyValue` to `SecretType`. (secret-versioning, "Every secret update creates a version"; secret-management, "Secrets have a supported type"; design D11, D12)
- [x] 1.5 Map the new entities in `AppDbContext`, with cascade delete from `Secret` to `SecretVersion`. Add tenant-isolation tests for the new entities in `TenantQueryFilterTests`. (tenant-isolation, "Tenant-owned data carries a tenant identifier"; secret-versioning, "Deleting a secret removes its versions")
- [x] 1.6 Generate the EF Core migration `ServerPrerequisitesWebUi`. Add the SQL that inserts version 1 for each existing secret from its current payload. Verify with `dotnet ef migrations has-pending-model-changes`. (secret-versioning, "Existing secrets after migration")
- [x] 1.7 Verify that the migration applies to an empty PostgreSQL 18 database, and that it applied to a database with existing secrets gives each secret version 1 with the same payload. (secret-versioning, "Existing secrets after migration")
- [x] 1.8 Add the new tables and columns to the SQLite test harness schema created by `EnsureCreated`. (design, Migration Plan)

## 2. Session management

- [x] 2.1 Implement the refresh token generator (32 random bytes, base64url) and its SHA-256 hashing. Add a unit test that the stored value is the hash and never the token. (session-management, "Sign-in starts a refresh session"; design D1)
- [x] 2.2 Update sign-in to create the session, set the `custode_refresh` cookie with `HttpOnly`, `Secure`, `SameSite=Strict`, and `Path=/api/auth`, and keep the refresh token out of the body. Add the `sid` claim. Make `JwtTokenIssuer` take its lifetime from tenant policy, with a fallback to `Jwt:LifetimeMinutes`. Test the cookie attributes, the body, and the claim. (session-management, "Sign-in starts a refresh session"; local-authentication, "Tokens are issued as signed JWTs"; design D3, D5)
- [x] 2.3 Implement `SessionCredentialResolver`, which reads only `Id`, `TenantId`, `UserId`, and the two token hashes, with `IgnoreQueryFilters()` limited to the hash lookup. Add a test that the resolver selects only those columns. (tenant-isolation, "Credential resolution is the only tenant-free lookup"; design D6)
- [x] 2.4 Implement `POST /api/auth/refresh` with rotation. Reject a missing `X-Custode-Request` header as forbidden and a missing cookie as unauthorized. Test that the previous token is not accepted after rotation. (session-management, "Refresh rotates the refresh token", "Missing request header", "Missing refresh cookie"; design D2, D3)
- [x] 2.5 Implement reuse detection with the grace window (default 10 seconds, configurable). Revoke the session with reason `reuse-detected` and record "refresh token reuse detected" only after the window. Map a concurrency conflict on save to the within-window result. Test the reuse after the window, the reuse within the window, and two concurrent refreshes. (session-management, "Reuse of a rotated refresh token ends the session"; design D2)
- [x] 2.6 Apply the absolute lifetime from the current tenant policy at every refresh. Test that a session passing its absolute lifetime is ended and that a shortened policy applies at the next refresh. (session-management, "Absolute session lifetime follows the tenant policy"; tenant-administration, "Policy changes take effect at the next use")
- [x] 2.7 Implement `POST /api/auth/activity`, which records activity at most once per minute per session. Verify that refresh does not count as activity and that an idle session is rejected on any endpoint. Test both. (session-management, "Idle sessions end after the tenant idle timeout"; design D4)
- [x] 2.8 Implement `POST /api/auth/sign-out`, which revokes the session, clears the cookie, writes "session revoked", and requires the request header. Test that the access token is rejected on the next request. (session-management, "Sign-out revokes the session"; design D3)
- [x] 2.9 Add the per-request session check after JWT validation, covering a revoked session, an idle session, an expired absolute lifetime, a disabled user, and a token without `sid`. Keep it separate from signature validation. Test each rejection. (session-management, "Every authenticated request checks its session"; design D4)
- [x] 2.10 Test that sign-in and refresh for a disabled account return the same generic failure as for an unknown login, and that an unexpired token of a disabled account is rejected. (session-management, "Disabled accounts lose access at once")
- [x] 2.11 Implement `ISessionRevoker` in `Custode.Infrastructure/Sessions/`, which revokes with a reason and writes the audit entries in the same unit of work. Test on both providers. (session-management, "Password change ends the user's other sessions"; design D8)
- [x] 2.12 Add the endpoint filter that restricts a session of a flagged user to change-password and sign-out, returning forbidden with the reason "password change required". Read the flag from the user row on each request. (local-authentication, "Initial passwords require a change at first sign-in"; design D4)

## 3. Password change

- [x] 3.1 Implement `POST /api/auth/change-password`: verify the current password, apply the new password through the identity framework, clear the flag, revoke the other sessions through `ISessionRevoker`, and write "password changed". Test success, a wrong current password (bad request), and a policy failure (bad request). (local-authentication, "Users change their own password"; design D7)
- [x] 3.2 Test that the other sessions are revoked and that the session that changed the password stays active. (session-management, "Password change ends the user's other sessions")
- [x] 3.3 Test that a flagged user's restriction ends at the password change without a new sign-in. (local-authentication, "Restriction removed after change")

## 4. Tenant policy and member management

- [x] 4.1 Implement the policy values, their defaults, and the range validation, which names the offending field. Add unit tests at each boundary of the four ranges. (tenant-administration, "Tenant security policies are validated and tenant-wide"; design D9)
- [x] 4.2 Implement `GET /api/tenant/policies` for any authenticated member and `PUT /api/tenant/policies` for tenant administrators through `TenantAdministratorFilter`. Test that a regular member reads the values, that a regular member's save is denied and audited, that an out-of-range save changes nothing, and that a valid save writes "tenant policy changed". (tenant-administration, "Any member can read the effective policy", "Value out of range", "Valid policy saved")
- [x] 4.3 Implement `GET /api/tenant/members` with an explicit tenant filter, because the global filter does not cover Identity users. Test that another tenant's members are not returned and that no password data appears. (tenant-administration, "Members are listed with their status"; design, Context)
- [x] 4.4 Implement `POST /api/tenant/members`. The tenant comes from the administrator's token and not from the request. Test the creation, the conflict on an existing login, and the policy failure. Test that the initial password appears in neither the response nor the logs, and that "member created" is recorded. (tenant-administration, "Members are created with an initial password")
- [x] 4.5 Implement the disable and enable endpoints. Disabling revokes the member's sessions through `ISessionRevoker`. Test that a member of another tenant returns not found, that disabling the last enabled administrator is a conflict, and that the audit entries are written. (tenant-administration, "Members can be disabled and enabled")
- [x] 4.6 Implement `PUT /api/tenant/members/{id}/administrator`. Withdrawing the role revokes the member's sessions, and withdrawing it from the last enabled administrator is a conflict. Test both and the audit entries. (tenant-administration, "The tenant administrator role can be granted and withdrawn")
- [x] 4.7 Serialize the last-administrator check and the change within one unit of work that writes to the tenant's audit chain head, so that concurrent withdrawals cannot remove the last administrator. Test concurrent withdrawals on both providers. (tenant-administration, "Last administrator protected"; design D10)

## 5. Structured and key-value secret types

- [x] 5.1 Add the wire names `structured` and `key-value` to `SecretValueCodec.ParseType` and `FormatType`. Test that the existing four names still parse. (secret-management, "Secrets have a supported type")
- [x] 5.2 Implement the structured validator: a JSON object, duplicate property names rejected by scanning the parsed document, names 1 to 128 characters, values up to 4096 characters, at most 100 fields. Test each rule. (secret-management, "Structured secrets hold named string fields"; design D12)
- [x] 5.3 Implement the key-value validator: an ordered array of pairs, unique non-empty keys, the same length limits, at most 200 pairs. Test each rule and the preserved order. (secret-management, "Key-value secrets hold ordered pairs"; design D12)
- [x] 5.4 Extend the create and update requests to accept the structured and key-value bodies, and return them on value reads. Test that listings expose no field or pair value. (secret-management, "Structured secrets hold named string fields", "Key-value secrets hold ordered pairs"; secret-management, "Secret listings never expose secret values")

## 6. Secret versioning

- [x] 6.1 Make creation write version 1 with the change kind `created`. Test it. (secret-versioning, "Every secret update creates a version")
- [x] 6.2 Make updates check `If-Match` before any work: a missing header is precondition required (428), a mismatch is precondition failed (412), and a match creates the next version and returns `ETag`. Test each outcome and that a `*` or weak value is rejected as precondition failed. (secret-versioning, "Updates and rollbacks require a matching version token"; design D11)
- [x] 6.3 Apply the concurrency stamp on the secret so that two updates with the same `If-Match` value produce one success and one precondition failure on both providers. (secret-versioning, "Concurrent updates")
- [x] 6.4 Make a move keep the version number and the `ETag`. Test that a move creates no version. (secret-versioning, "Move creates no version", "Version token unchanged by move")
- [x] 6.5 Implement `GET /api/secrets/{id}/versions`, newest first, with metadata only. A caller with Read gets the list, a caller with Write but without Read gets forbidden, and a caller with no permission gets not found. Test each. (secret-versioning, "Version history lists metadata only", "Entry fields", "Version list without Read")
- [x] 6.6 Implement `GET /api/secrets/{id}/versions/{number}/value`, which requires Read, records "secret value read" with the version identifier as secondary target, and returns not found for an unknown number. Test each. (secret-versioning, "A version value is read with Read permission"; security-audit, "Additional critical actions produce an audit entry")
- [x] 6.7 Implement the rollback endpoint. It requires Write and not Read, copies the protected payload, creates a version with change kind `rolled-back`, and records "secret rolled back". Test the rollback, the conflict for the current version, the conflict for a name collision, the forbidden response without Write, and the Write-without-Read case. (secret-versioning, "Rollback restores a version as a new version"; design D11)
- [x] 6.8 Test that a value read of any secret response carries the `version` field and the `ETag` header. (secret-versioning, "Version token on value read")

## 7. Secret search

- [x] 7.1 Add the batch effective-permission method to `IFolderAccess`, which computes the permissions of many folders in one request, with unit tests for union and inheritance. (secret-management, "Secrets can be found by name"; design D13)
- [x] 7.2 Implement `GET /api/search` with in-application case-insensitive matching, literal wildcards, the 1 to 100 character query rule, default limit 20, maximum 50, visibility by effective permission, and truncation after 5,000 candidates. Test every scenario in the requirement. (secret-management, "Secrets can be found by name", "Secret the caller cannot read", "Wildcard matched literally", "Empty query", "Results truncated", "Folder visible with any permission", "Limit above the maximum")

## 8. Access control read contracts

- [x] 8.1 Implement `GET /api/access/groups` with member and grant counts and `GET /api/access/groups/{id}/members`, restricted to tenant administrators. A group of another tenant returns not found. Test each. (access-control, "Administrators can read groups and their members")
- [x] 8.2 Implement `GET /api/access/grants?folderPath=`, grouping the direct grants and the grants of the ancestors that apply, with each principal's sources and effective levels. An unknown folder returns not found. Test the inherited grant, the direct and inherited case, the broken inheritance case, and the unknown folder. (access-control, "Administrators can read folder grants with their effective permissions"; design D14)
- [x] 8.3 Implement `GET /api/access/folders` with each folder's path, parent, and inheritance flag. Test it. (access-control, "Administrators can read the inheritance status of folders")
- [x] 8.4 Implement `GET /api/access/me/permissions?folderPath=`, which returns only the caller's levels and returns not found when the caller has none. Test the caller's levels, the not-found case, and that no other principal's grants appear. (access-control, "Callers can read their own effective permissions on a folder")
- [x] 8.5 Test that a non-administrator receives forbidden on each administrator read, and that these reads write no audit entry. (access-control, "Administrators can read groups and their members")

## 9. Audit enhancements

- [x] 9.1 Add `hash` and `previousHash` to the audit entry response, with an empty previous hash for the first entry of a tenant. Test that consecutive entries link. (security-audit, "Audit entries expose their hash chain links")
- [x] 9.2 Implement the sequence cursor (`afterSequence`, `limit` default 100 and maximum 500) and the envelope `{ entries, nextAfterSequence }`. Test the first page, the following page, the default size, the out-of-range limit, and the negative cursor. Update the existing audit query tests for the envelope (breaking change). (security-audit, "Audit query is paged by sequence cursor"; proposal, breaking changes)
- [x] 9.3 Write the audit entries of the additional critical actions: version value read, rollback, import, member creation, disable and enable, administrator role change, policy change, password change, session revoked, reuse detected, and backup export. Test each action and its denied outcome where it applies. (security-audit, "Additional critical actions produce an audit entry")

## 10. Bulk import

- [x] 10.1 Enforce the limits: at most 500 rows, and a body no larger than the configured maximum (default 5 MiB). Test the 501st row and an oversize body. (secret-transfer, "Import requests are bounded")
- [x] 10.2 Implement the row pipeline: folder resolution, the `folder-not-found` and `forbidden` statuses, `createMissingFolders`, duplicate and conflict detection, and per-row validation. Test each status. (secret-transfer, "A dry run validates every row without writing", "Import follows folder permissions", "Missing folders are created only on request", "Duplicate names are reported as conflicts"; design D16)
- [x] 10.3 Test that a dry run writes no folder, secret, or audit entry, by counting rows before and after on both providers. (secret-transfer, "Dry run writes nothing")
- [x] 10.4 Implement the commit in one unit of work: folders, then secrets, each with its audit entry, plus the summary entry with a batch identifier. Reject a batch with any non-valid row as unprocessable, and reject one with a forbidden row as forbidden with a denied entry. Test a failure injected at the last row, which must leave nothing stored. (secret-transfer, "A commit stores all rows or none", "Each imported secret is audited within the same transaction")
- [x] 10.5 Test that the report, the error messages, and the captured logs contain no secret value for a row that carries a password. (secret-transfer, "Import reports and logs never contain values")

## 11. Encrypted tenant backup

- [x] 11.1 Implement the backup container: header layout as in design D17, PBKDF2-HMAC-SHA256 with 600,000 iterations, AES-256-GCM with a 12-byte nonce and a 16-byte tag, and the header as associated data. Add unit tests for the round trip, a wrong passphrase, and a changed header byte. (tenant-backup, "Backups are encrypted with a passphrase-derived key")
- [x] 11.2 Validate the passphrase after NFC normalization, requiring at least 12 Unicode code points, and make sure it is never stored. Test the short passphrase, and check that no stored record contains it. (tenant-backup, "Passphrases are validated and never stored")
- [x] 11.3 Assemble the content: folders with their flags, secrets with all versions and decrypted values, groups, memberships, grants, policies, members without password hashes, audit entries with hashes, and the chain head. Test that a secret with three versions has all three, and that no password hash, token, or session appears. (tenant-backup, "Backup content covers the tenant vault without credentials")
- [x] 11.4 Authorize the export: the administrator filter first, then Read on every folder, with a denied entry on either refusal. Test the administrator with Read on every folder, the administrator with one folder without Read, and a non-administrator. (tenant-backup, "Backup export requires administrator access to every folder")
- [x] 11.5 Enforce the size cap (default 256 MiB, payload too large) and set `Cache-Control: no-store` and the attachment `Content-Disposition`. Test the cap and the headers. (tenant-backup, "Backup export is bounded and audited")
- [x] 11.6 Implement the format reader used by the tests, which rejects an unknown format version without decrypting. Test it. (tenant-backup, "Backup format is versioned")
- [x] 11.7 Record "tenant backup exported" with the tenant as target and the backup identifier as secondary target. Test the entry. (tenant-backup, "Successful export audited")
- [x] 11.8 Test that the export writes no plaintext to disk and that the captured logs contain no secret value. (tenant-backup, "Backups are encrypted with a passphrase-derived key"; secret-encryption, "Secret value in logs")

## 12. Documentation and verification

- [x] 12.1 Update `docs/architecture/authentication.md` for the session model, the refresh cookie, the `sid` claim, the activity endpoint, and the password change. Update `docs/architecture/tenancy.md` for the tenant-free credential resolution. (local-authentication; tenant-isolation)
- [x] 12.2 Add operator notes for the policy ranges, the backup passphrase, the backup size limit, and the grace window. (tenant-administration; tenant-backup; session-management)
- [x] 12.3 Run the full server test suite on SQLite and on PostgreSQL 18 (Testcontainers), with zero failures on both. (project rule AGENTS.md, section 3)
- [x] 12.4 Run `openspec validate server-prerequisites-webui --strict` and verify zero errors and zero warnings. (project rule AGENTS.md, section 3)
- [x] 12.5 Verify that every new or modified `.cs` file under `src/` and `tests/` starts with the copyright header, and that no generated file has it. (project rule 5)
- [x] 12.6 Record the web UI follow-ups in the `web-ui-architecture` change: the `412` status, the activity endpoint, and the policy endpoint. Do not change its specs in this change. (design, Consumers of this change)
