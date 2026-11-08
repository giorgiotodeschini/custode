# Spec Delta

## Purpose
Defines bulk import of secrets as one atomic operation with a dry run that reports every row's outcome before anything is stored, so that a client can show a full preview and an import either completes entirely or leaves the vault unchanged.

## ADDED Requirements

### Requirement: A dry run validates every row without writing
The system SHALL provide `POST /api/secrets/imports` with the query parameter `dryRun`. When `dryRun` is true, the system SHALL validate every row and return a report, and it MUST NOT create, change, or delete any folder, secret, or audit entry. The report SHALL contain, for each row, its index, a status of `valid`, `invalid`, `conflict`, `folder-not-found`, or `forbidden`, a stable code, and a message that names the row by index and name only.

#### Scenario: Dry run with mixed rows
- **WHEN** a dry run contains one valid row, one row with an unsupported type, and one row whose name exists in the target folder
- **THEN** the report marks the rows `valid`, `invalid`, and `conflict` and nothing is stored

#### Scenario: Dry run writes nothing
- **WHEN** a dry run succeeds for every row
- **THEN** no secret, folder, or audit entry is created

### Requirement: A commit stores all rows or none
When `dryRun` is false, the import SHALL store every row in a single transaction, or store none of them. If any row is not `valid`, the import SHALL be rejected as unprocessable with the same report, and no folder, secret, or audit entry SHALL be stored.

#### Scenario: All rows valid
- **WHEN** a commit contains twelve valid rows
- **THEN** twelve secrets are stored and the response reports the count created

#### Scenario: One invalid row in a commit
- **WHEN** a commit contains ten rows and the last row is invalid
- **THEN** no secret is stored and the response is unprocessable with the report of all ten rows

### Requirement: Import follows folder permissions
A row SHALL be `folder-not-found` when the caller has no effective permission on its target folder, and the same rule applies to a folder that does not exist. A row SHALL be `forbidden` when the caller has some permission on the target folder but lacks Create. A commit containing a `forbidden` row SHALL be rejected as forbidden and the attempt SHALL be recorded in the audit log with the action "secrets imported" and the outcome "denied".

#### Scenario: Target folder without any permission
- **WHEN** a row targets a folder where the caller has no effective permission
- **THEN** the row is `folder-not-found`

#### Scenario: Target folder with Read but without Create
- **WHEN** a commit contains a row targeting a folder where the caller has Read but not Create
- **THEN** the commit is rejected as forbidden and the audit entry "secrets imported" with outcome "denied" is recorded

### Requirement: Missing folders are created only on request
When the import request sets `createMissingFolders` to true, a row whose folder does not exist SHALL create the missing folders on the path, provided the caller has Create permission on the nearest existing ancestor. When the flag is false, such a row SHALL be `folder-not-found`. Each created folder SHALL be recorded in the audit log with the action "folder created".

#### Scenario: Missing folder created
- **WHEN** a commit sets `createMissingFolders` to true and targets `/production/aws` where `/production` exists and the caller has Create on it
- **THEN** the folder `/production/aws` is created and the secrets are stored in it

#### Scenario: Missing folder without the flag
- **WHEN** a row targets a missing folder and `createMissingFolders` is false
- **THEN** the row is `folder-not-found` and the import is not committed

### Requirement: Duplicate names are conflicts
A row SHALL be `conflict` when its name already exists in its target folder, or when another row of the same batch has the same name and folder. A commit containing a conflict SHALL be rejected as unprocessable, and no existing secret is changed.

#### Scenario: Duplicate within the batch
- **WHEN** two rows of one batch name the same secret in the same folder
- **THEN** both rows are reported as `conflict`

### Requirement: Each imported secret is audited within the same transaction
Each secret created by a commit SHALL have an audit entry with the action "secret created", and the commit SHALL also record one summary entry with the action "secrets imported" that references a batch identifier. Every such entry SHALL be written in the same transaction as the change it records.

#### Scenario: Summary and per-secret entries
- **WHEN** a commit creates three secrets under one batch identifier
- **THEN** three "secret created" entries and one "secrets imported" entry that names the batch identifier are recorded

### Requirement: Import requests are bounded
A batch SHALL contain at most 500 rows. The request body SHALL NOT exceed the operator-configured maximum size, which defaults to 5 MiB. A batch that exceeds either limit SHALL be rejected before any row is validated.

#### Scenario: Too many rows
- **WHEN** a batch contains 501 rows
- **THEN** the request is rejected as bad request and no row is validated

#### Scenario: Body too large
- **WHEN** the request body exceeds the configured maximum size
- **THEN** the request is rejected as payload too large

### Requirement: Import reports and logs never contain values
The report, the error messages, and the logs of an import MUST NOT contain any secret value, password, key, or file content.

#### Scenario: Invalid row with a value
- **WHEN** a row with an invalid type also carries a password
- **THEN** the report for that row names its index and name and does not contain the password
