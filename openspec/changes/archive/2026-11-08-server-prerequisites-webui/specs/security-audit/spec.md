# Spec Delta

## ADDED Requirements

### Requirement: Additional critical actions produce an audit entry
The system SHALL record an audit entry, with the outcome denied for a refused attempt, for each of these additional critical actions: reading the value of a secret version, rolling back a secret, importing secrets, creating a member, disabling or enabling a member, granting or withdrawing the tenant administrator role, changing a tenant policy, changing a password, revoking a session, detecting reuse of a refresh token, and exporting a tenant backup.

#### Scenario: Version value read
- **WHEN** a user successfully reads the value of a secret version
- **THEN** an audit entry is recorded with the action "secret value read" and the version identifier as secondary target

#### Scenario: Rollback recorded
- **WHEN** a user successfully rolls a secret back
- **THEN** an audit entry is recorded with the action "secret rolled back"

#### Scenario: Session revoked
- **WHEN** a session is revoked by sign-out, disablement, role withdrawal, or a password change
- **THEN** an audit entry is recorded with the action "session revoked"

#### Scenario: Denied backup export
- **WHEN** a tenant administrator without Read on every folder requests a backup
- **THEN** an audit entry is recorded with the action "tenant backup exported" and the outcome "denied"

### Requirement: Audit entries expose their hash chain links
Each audit entry returned by the audit query SHALL include its own hash and the hash of the preceding entry of the same tenant. Hashes SHALL be lowercase hexadecimal SHA-256 strings. The first entry of a tenant SHALL have an empty previous hash. These fields are read-only and MUST NOT include any secret value.

#### Scenario: Consecutive entries linked
- **WHEN** an administrator reads two consecutive entries of a tenant
- **THEN** the previous hash of the second entry equals the hash of the first entry

#### Scenario: First entry of a tenant
- **WHEN** an administrator reads the first entry of a tenant
- **THEN** its previous hash is empty

### Requirement: Audit query is paged by sequence cursor
The audit query SHALL accept `afterSequence` and `limit`, and SHALL return `entries` in ascending sequence order together with `nextAfterSequence`. Consecutive pages SHALL neither skip nor repeat an entry, and the existing filters SHALL combine with the cursor.

#### Scenario: First page
- **WHEN** an administrator queries the audit log with `limit` 2 and three matching entries exist
- **THEN** two entries are returned and `nextAfterSequence` is the sequence number of the second entry

#### Scenario: Following page
- **WHEN** an administrator queries with `afterSequence` set to that value
- **THEN** the remaining entry is returned and `nextAfterSequence` is null

#### Scenario: Default page size
- **WHEN** an administrator queries without `limit`
- **THEN** at most 100 entries are returned

#### Scenario: Limit out of range
- **WHEN** an administrator queries with `limit` 501
- **THEN** the request is rejected as bad request

#### Scenario: Negative cursor
- **WHEN** an administrator queries with `afterSequence` below 0
- **THEN** the request is rejected as bad request
