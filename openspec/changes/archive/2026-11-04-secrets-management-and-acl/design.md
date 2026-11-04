# Design

## Context

The server foundations are implemented: `AppDbContext` applies a global `TenantId` filter to every `ITenantOwned` entity and rejects saves that cross tenants; `ISecretProtector` encrypts values with a purpose bound to the tenant (`custode.secrets.v1|<tenantId>`); feature slices are registered explicitly through `FeatureSlices` and `IFeatureSlice`; and the `TenantOperator` policy shows how role-based authorization is expressed. Requirements for this change are in `specs/secret-management`, `specs/access-control`, and `specs/security-audit`. See `proposal.md` for motivation.

## Goals / Non-Goals

**Goals:**
- Store the four secret types with their values encrypted through `ISecretProtector`, never in plaintext.
- Resolve effective permissions for a user on a folder in one place, so every secret operation applies the same rule.
- Make audit entries atomic with the actions they record, ordered per tenant, and tamper-evident.

**Non-Goals:**
- Per-secret ACLs. Permissions live on folders only, as specified.
- Explicit deny entries, group nesting, and external group sources (OIDC group claims).
- Break-glass access for administrators. Administrators do not read secret values without a grant.
- Audit log export or retention. Only the query and verification operations described in the spec are built.

## Decisions

### D1. Shared domain model in infrastructure, use cases in slices
Entities for folders, secrets, groups, memberships, folder grants, and audit entries live in `Custode.Infrastructure` (for example under `Custode.Infrastructure/Vault/`) and are mapped in `AppDbContext`. They implement `ITenantOwned`, so the existing query filter and save guard apply without extra code.
- Rationale: `AppDbContext` must know these entities, and shared infrastructure must not depend on a feature slice. Keeping the entities in infrastructure satisfies the `server-architecture` dependency rule.
- Trade-off: the entity shape is shared across slices, so a change to an entity touches infrastructure. This is accepted because the entities are persistence contracts, not slice behavior.
- Alternative: entities inside each slice. Rejected: `AppDbContext` would depend on slices.

### D2. Three feature slices with use-case folders
Slices follow the existing `Features/<Feature>/<UseCase>/` layout (`Contracts`, `Handler`, `Endpoint`, `Validator`):
- `Features/SecretManagement/` — folders and secrets (create, read value, list, update, move, delete).
- `Features/AccessControl/` — groups, memberships, folder grants, inheritance changes.
- `Features/SecurityAudit/` — audit query and verification.

Slices communicate only through published contracts in shared infrastructure: the effective-permission evaluator (`IFolderAccess`) and the audit writer (`IAuditTrail`). `SecretManagement` never calls `AccessControl` handlers.

### D3. Permission model: bit flags on folder grants, evaluated along the ancestor path
A folder grant stores a principal (user or group), a folder, and a flags value combining Read, Write, Create, and Delete. A folder has an `InheritsPermissions` flag, default true, and a `Path` (normalized, unique per tenant).

Effective permissions for user U on folder F are computed as follows:
1. Collect the ancestors of F from its `Path` prefixes, plus F itself. Load the folders and the grants for U and for each group U belongs to, in one query each.
2. Walk from F upward. OR in the grants of each folder. Stop after a folder whose `InheritsPermissions` is false, and do not walk above it.
3. The result is the effective flags. An empty result means "no visibility" (see D7).

This implements the "break inheritance" semantics: breaking inheritance on F removes the ancestor grants from F and its subtree, and F's own grants still apply.
- Alternative: materialize the effective ACL per folder and update it on every change. Rejected for this phase: more write complexity and risk of stale data, and the tree is expected to be small. A materialized cache can be added later without changing the contract.
- Alternative: deny entries with precedence. Rejected: the spec defines allow-only permissions.

Indexes: unique `(TenantId, Path)` on folders; unique `(FolderId, PrincipalType, PrincipalId)` on grants; `(TenantId, GroupId)` and `(TenantId, UserId)` on memberships.

### D4. Tenant administrator policy and bootstrap grant
A new `TenantAdministrator` policy is added next to `TenantOperator` in `TenantPolicies`, requiring the `TenantAdministrator` role. Access-control changes are authorized by an endpoint filter that uses this policy and records each denial (D11). Secret endpoints do not check that role; they check folder permissions only (D7).

The root folder `/` and the bootstrap grant are created by the operator-only endpoint `POST /api/operator/tenants/{tenantId}/vault`, in the `TenantProvisioningSlice`. The operation is tenant-independent (tenant-isolation spec): the operator belongs to another tenant, so the write runs under the target tenant's scope. The endpoint checks that the administrator belongs to that tenant, creates the root and a grant with all four permissions for the administrator, and audits `vault initialized` and `grant set` under the target tenant with the operator as actor. A second call for the same tenant is rejected with 409.
- Trade-off: the initial administrator holds full data access through the bootstrap grant. The grant is explicit and audited, and the administrator may revoke it after creating the first groups and grants.

### D5. Secret storage: typed, encrypted payload
A secret row stores its name, type, folder, file name (for file-based secrets), and timestamps in plaintext metadata. The secret value is serialized to a single JSON document and protected once with `ISecretProtector.Protect(tenantId, json)`:
- username/password: `{ "username", "password" }`
- PAT and API key: `{ "value" }`
- file-based: `{ "contentBase64" }`

Serializing into one payload means one protection call per read or write, and the purpose binding covers the whole value. The maximum file size is configured by the operator, and the request is rejected before encryption when the limit is exceeded. Secret names are unique within a folder, and folder names are normalized by the same rule as paths.
- Alternative: one protected column per field. Rejected: more columns and calls with no security gain.

### D6. Audit trail: append-only, hash-chained, atomic
Audit entries have a per-tenant `Sequence`, `OccurredAtUtc`, actor, action, outcome, target type and id, `PreviousHash`, and `Hash`. `Hash` is SHA-256 over a canonical serialization of the entry fields plus `PreviousHash`.

- Atomicity: `IAuditTrail` adds the entry to the same `DbContext` as the action, so the single `SaveChanges` transaction commits or rolls back both. A failed audit write therefore fails the action.
- Sequencing: each tenant has one `AuditChainHead` row (last sequence and last hash) with an EF Core concurrency token. Audit writes update this row, which serializes writes per tenant and avoids gaps and forks. Concurrent writers retry on conflict.
- Denied attempts have no action to roll back. They are written and saved in their own transaction.
- Immutability is enforced in three layers: `AppDbContext` rejects modified or deleted audit entities; the migration creates database triggers that reject `UPDATE` and `DELETE` on the audit table on both PostgreSQL and SQLite; and the hash chain detects changes made by bypassing both.
- Trade-off: the triggers do not stop a database owner from dropping them. The hash chain makes that tampering detectable through verification, which is the guarantee the spec requires.

### D7. Visibility and error responses
Access is checked in this order:
1. If effective permissions are empty, return the same response as for a missing folder or secret (404).
2. If the required flag is missing, return 403 and record a denied audit entry.
3. Otherwise perform the operation.

Required flags: read value — Read; list metadata — Read; update — Write; create secret or subfolder — Create on the target folder; delete secret or folder — Delete on that folder; move — Delete on the source folder and Create on the destination folder.

### D8. Error and logging policy
Secret plaintext and file content never reach logs, exception messages, or audit entries. Request logs for secret slices record identifiers only, following D7 of the core change.

### D9. Database provider scope: PostgreSQL migrations, SQLite created by EnsureCreated
The EF Core migration `SecretsAndAcl` is generated for PostgreSQL (Npgsql), as the initial migration is. It contains the immutability triggers for PostgreSQL. SQLite does not run migrations: the test harness creates its schema with `EnsureCreated` and applies the SQLite immutability triggers itself (`AuditImmutabilityTriggers.SqliteUp`, using `CREATE TRIGGER IF NOT EXISTS` so that a second host on the same file can start).
- Rationale: PostgreSQL is the production and development database; SQLite exists for fast in-memory and file tests. Both providers are exercised by the same behavioral tests, so the immutability guarantee is verified on each provider where it applies.
- Consequence: a SQLite database created outside the test harness has no immutability triggers. This is accepted because SQLite is not a production target.

### D10. Tampering attempts are recorded before the save is rejected
`AppDbContext` rejects any save that modifies or deletes an audit entry. Before it throws, it records an audit entry with action `audit tampering attempt`, outcome `denied`, the target entry, and the operation (`update` or `delete`). The record is written by a separate context under the same tenant, so it is committed even though the original save fails. The check runs before EF Core interceptors, so it lives in the context itself; the synchronous `SaveChanges` path uses the same logic.
- Limitation: the record needs a tenant scope. A context without a tenant rejects the save without recording the attempt.
- Limitation: a raw SQL statement that bypasses the context is stopped by the database trigger (D6), and the transaction aborts, so no record survives. Such attempts are visible only to database logs.
- The actor is `Guid.Empty`: the attempt happens in the data layer, where no request user is available. No endpoint can modify audit entries.

### D11. Denied access-control changes are audited in the endpoint filter
Access-control endpoints are not protected by a policy. `TenantAdministratorFilter` evaluates the `TenantAdministrator` policy inside the endpoint. On a failed check it records a `denied` audit entry for the requested action, with the route `id` as target when present, and returns 403. Authentication still comes from the fallback policy.
- Rationale: a policy failure is decided by the framework before application code runs, so it cannot write an audit entry. The filter performs the same check and then records the denial.

## Risks / Trade-offs

- **[Permission evaluation cost grows with folder depth and group count]** → Ancestors are loaded in one path-prefix query with the indexes in D3. Measure on PostgreSQL with realistic depth before considering a materialized cache.
- **[Audit writes serialize per tenant]** → The chain head is a single row per tenant. Acceptable for a self-hosted, per-tenant audit volume; if it becomes a bottleneck, the chain can be split into batches with a hash of each batch.
- **[Database owner disables triggers and rewrites the chain]** → Verification detects rewritten entries only if the chain head is also protected. Mitigation is operational: keep periodic verification output outside the database. Recorded as a follow-up, not a spec change.
- **[Initial administrator holds full data access through the bootstrap grant]** → The grant is explicit and audited; administrators may revoke it after creating the first groups and grants.
- **[Secret values are returned in full to any user with Read on the folder]** → Intended by the spec. Per-secret ACLs are a possible later change.
- **[File secrets stored in the database increase row size]** → Bounded by the operator-configured maximum file size.

## Migration Plan

This change adds tables; there is no existing data to migrate.

Deployment:
1. Apply a new EF Core migration `SecretsAndAcl` that creates the folder, secret, group, membership, grant, audit entry, and chain head tables, with their indexes and the audit immutability triggers.
2. Start the server. The tenant provisioning path creates the root folder and the bootstrap grant for each tenant created after deployment. Existing tenants are provisioned by a one-time operation that is part of the tasks.

Rollback: before any secret or audit entry exists, the migration can be reverted by dropping the new tables and triggers. After data exists, rollback requires keeping the audit tables and the encryption key ring; they must not be deleted, following the core change's rollback rule.

## Open Questions

None that change the specs or the task breakdown. The default maximum file size is an operator setting; the implementation will ship a conservative default documented in the configuration tasks.
