# security-audit Specification

## Purpose
Defines the security audit trail that records who did what to secrets and access controls within a tenant, in chronological order, in a form that cannot be silently altered afterward, so that administrators can investigate and prove the history of critical actions.

## Requirements

### Requirement: Critical actions produce an audit entry
The system SHALL record an audit entry for every critical action. Critical actions are: reading a secret value, creating, updating, deleting, or moving a secret, creating or deleting a folder, changing a folder's permissions or inheritance, and changing group membership or group definitions. Denied attempts at these actions MUST also be recorded.

#### Scenario: Secret value read
- **WHEN** a user successfully reads a secret value
- **THEN** an audit entry is recorded with the action "secret value read"

#### Scenario: Denied secret access
- **WHEN** a user is denied access to a secret value
- **THEN** an audit entry is recorded with the action "secret value read" and the outcome "denied"

#### Scenario: ACL change recorded
- **WHEN** a tenant administrator breaks inheritance on a folder
- **THEN** an audit entry records the administrator, the folder, and the action "inheritance broken"

### Requirement: Audit entries record who, when, where, and what
Each audit entry SHALL contain the tenant identifier, the acting user identifier, a UTC timestamp, the action, the outcome (succeeded or denied), and the identifiers of the affected entities. An audit entry MUST NOT contain secret values, file content, or passwords.

#### Scenario: Entry content for a successful change
- **WHEN** a user creates a secret
- **THEN** the entry contains the tenant, the user, a UTC timestamp, the action "secret created", the outcome "succeeded", and the secret and folder identifiers, and no secret value

#### Scenario: Entry content never includes plaintext
- **WHEN** an audit entry is written for a secret create or update
- **THEN** no field of the entry contains the secret value or file content

### Requirement: Audit entries are chronological within a tenant
Audit entries of a tenant SHALL be ordered by a per-tenant sequence number assigned at write time. The sequence number MUST increase by exactly one for each entry and is the authoritative order of the log.

#### Scenario: Consecutive actions
- **WHEN** two critical actions are performed in a tenant one after the other
- **THEN** the second entry has a sequence number one greater than the first

### Requirement: Audit entries are written atomically with the action
An audit entry SHALL be written in the same transaction as the action it records. If the audit entry cannot be written, the action MUST NOT be applied.

#### Scenario: Audit write fails
- **WHEN** an action is performed and its audit entry cannot be stored
- **THEN** the action is rolled back and the request fails

#### Scenario: Action and audit both succeed
- **WHEN** an action completes successfully
- **THEN** the action and its audit entry are both stored

### Requirement: Audit entries are immutable through the application
The system MUST NOT provide any operation that updates or deletes an audit entry. An attempt to modify or delete an audit entry through the application SHALL be rejected, and such an attempt is itself recorded in the audit log.

#### Scenario: Modification attempt through the application
- **WHEN** an application operation attempts to update an existing audit entry
- **THEN** the operation is rejected and the entry is unchanged

#### Scenario: Deletion attempt through the application
- **WHEN** an application operation attempts to delete an audit entry
- **THEN** the operation is rejected and the entry remains in the log

### Requirement: Audit entries are tamper-evident
Each audit entry SHALL include a cryptographic hash computed over its own content and the hash of the previous entry of the same tenant. A tenant administrator SHALL be able to verify the integrity of a tenant's audit log, and verification MUST report the first entry whose content or chain link does not match.

#### Scenario: Intact log verified
- **WHEN** a tenant administrator verifies the audit log of their tenant and no entry has been altered
- **THEN** the verification succeeds and reports the number of verified entries

#### Scenario: Altered entry detected
- **WHEN** an audit entry has been altered directly in the database and the tenant administrator verifies the log
- **THEN** the verification fails and reports the sequence number of the first altered entry

### Requirement: Audit log is tenant-scoped and readable only by administrators
Audit entries SHALL belong to exactly one tenant and MUST only be visible within that tenant. Reading the audit log SHALL be restricted to tenant administrators.

#### Scenario: Administrator reads own tenant log
- **WHEN** a tenant administrator queries the audit log
- **THEN** only entries of their tenant are returned

#### Scenario: Non-administrator reads the audit log
- **WHEN** a user without the tenant administrator role queries the audit log
- **THEN** the request is rejected as forbidden

### Requirement: Audit log can be filtered for investigation
A tenant administrator SHALL be able to query the audit log by time range, acting user, action, and outcome. Results MUST be returned in sequence order.

#### Scenario: Filter by user and action
- **WHEN** a tenant administrator queries the audit log for a user and the action "secret value read"
- **THEN** only the matching entries of that tenant are returned, in sequence order

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
