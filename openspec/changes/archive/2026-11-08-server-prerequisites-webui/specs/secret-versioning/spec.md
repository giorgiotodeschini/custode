# Spec Delta

## Purpose
Defines the version history of secrets: every change to a secret's name or value is kept as a numbered version that can be listed, read with the same permission as the current value, and restored by rollback, and defines the version token that makes concurrent edits detectable.

## ADDED Requirements

### Requirement: Every secret update creates a version
A secret SHALL have a version history. Creating a secret SHALL create version 1. Each later update of a secret's name or value SHALL create the next version with the next number, and the current version SHALL be the highest number. Moving a secret to another folder MUST NOT create a version.

#### Scenario: Update creates a version
- **WHEN** a user with Write permission updates the value of a secret at version 3
- **THEN** version 4 is created with the new value and the secret's current version becomes 4

#### Scenario: Move creates no version
- **WHEN** a user moves a secret to another folder
- **THEN** the secret's current version does not change

#### Scenario: Existing secrets after migration
- **WHEN** the migration that introduces versioning is applied to an existing secret
- **THEN** the secret has version 1 whose value equals its value before the migration

### Requirement: All versions are kept until the secret is deleted
Every version of a secret SHALL be retained. Versions SHALL NOT be pruned by the system. Deleting a secret SHALL delete all of its versions.

#### Scenario: Versions retained after update
- **WHEN** a secret has been updated three times
- **THEN** four versions exist and each keeps its own value

#### Scenario: Deleting a secret removes its versions
- **WHEN** a user deletes a secret that has three versions
- **THEN** none of its versions can be listed or read afterwards and the audit entry for the deletion remains

### Requirement: Version history lists metadata only
The system SHALL list the versions of a secret, newest first, through `GET /api/secrets/{id}/versions`. The list MUST NOT contain any value or file content. The caller MUST have Read permission on the secret's folder.

#### Scenario: Entry fields
- **WHEN** a user with Read permission lists the versions of a secret that was rolled back once
- **THEN** each entry contains its number, name, file name when applicable, change kind, creation time in UTC, acting user, and the source version of the rollback

#### Scenario: Versions listed
- **WHEN** a user with Read permission lists the versions of a secret with three versions
- **THEN** three entries are returned, newest first, with no value

#### Scenario: Version list without Read
- **WHEN** a user with Write but without Read lists the versions of a secret
- **THEN** the request is rejected as forbidden

### Requirement: A version value is read with Read permission
The system SHALL return the value of a specific version through `GET /api/secrets/{id}/versions/{number}/value`. The caller MUST have Read permission on the secret's folder. Each successful read SHALL be recorded in the audit log with the action "secret value read" and the version identifier as the secondary target. An unknown version number SHALL return not found.

#### Scenario: Old version value read
- **WHEN** a user with Read permission reads version 2 of a secret that is at version 4
- **THEN** the value of version 2 is returned and an audit entry "secret value read" is recorded with the version as secondary target

#### Scenario: Unknown version number
- **WHEN** a user requests version 9 of a secret with four versions
- **THEN** the response is not found

### Requirement: Rollback restores a version as a new version
The system SHALL roll a secret back through `POST /api/secrets/{id}/versions/{number}/rollback`. The caller MUST have Write permission on the secret's folder and MUST NOT need Read permission. A rollback SHALL create a new version whose name, file name, and value equal those of the selected version, with the change kind "rolled back" and the selected version as source. Existing versions MUST NOT change. The rollback SHALL be recorded in the audit log with the action "secret rolled back".

#### Scenario: Rollback creates a new version
- **WHEN** a user with Write permission rolls back a secret at version 4 to version 2
- **THEN** version 5 is created with the value of version 2, versions 1 to 4 are unchanged, and the audit entry "secret rolled back" is recorded

#### Scenario: Rollback to the current version
- **WHEN** a user rolls a secret back to its current version
- **THEN** the request is rejected as conflict and no version is created

#### Scenario: Rollback without Write
- **WHEN** a user without Write permission on the folder requests a rollback
- **THEN** the request is rejected as forbidden and no version is created

#### Scenario: Rollback that collides with another secret name
- **WHEN** the name of the selected version is now used by another secret in the same folder
- **THEN** the request is rejected as conflict and the secret is unchanged

### Requirement: Secret representations carry a version token
Every response that returns a secret SHALL carry its current version number in a `version` field, and the responses for value reads, updates, and rollbacks SHALL also carry it as an `ETag` header whose value is the version number in double quotes, for example `"4"`. A move MUST NOT change the version token.

#### Scenario: Version token on value read
- **WHEN** a user with Read permission reads the value of a secret at version 4
- **THEN** the response carries the header `ETag: "4"`

#### Scenario: Version token unchanged by move
- **WHEN** a secret at version 4 is moved to another folder
- **THEN** its version token is still 4

### Requirement: Updates and rollbacks require a matching version token
An update or a rollback SHALL include an `If-Match` header with the version token last read by the client. A request without the header SHALL be rejected as precondition required. A request whose `If-Match` value does not equal the current version token, including `*` and weak tags, SHALL be rejected as precondition failed and no change SHALL be made. A matching request SHALL proceed and return the new version token.

#### Scenario: Update without version token
- **WHEN** a client updates a secret without an `If-Match` header
- **THEN** the request is rejected as precondition required and the secret is unchanged

#### Scenario: Stale version token
- **WHEN** a client updates a secret at version 5 with `If-Match: "4"`
- **THEN** the request is rejected as precondition failed and the secret is unchanged

#### Scenario: Matching version token
- **WHEN** a client updates a secret at version 4 with `If-Match: "4"`
- **THEN** the update is applied, version 5 is created, and the response carries `ETag: "5"`

#### Scenario: Concurrent updates
- **WHEN** two clients send updates with the same `If-Match` value at the same time
- **THEN** exactly one update is applied and the other is rejected as precondition failed
