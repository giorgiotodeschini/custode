# Spec Delta

## Purpose
Defines the encrypted export of a tenant's vault state, protected by a passphrase chosen by a tenant administrator, so that the tenant can keep an offline copy of its secrets, access structure, and audit history without any plaintext value or credential leaving the server unencrypted.

## ADDED Requirements

### Requirement: Backup export requires administrator access to every folder
The system SHALL provide `POST /api/backups`. Only a tenant administrator MAY request a backup. The caller MUST hold Read permission on every folder of the tenant, so that every secret value in the backup is one the caller could read. A caller without that permission SHALL be refused as forbidden, and the attempt SHALL be recorded in the audit log with the action "tenant backup exported" and the outcome "denied".

#### Scenario: Administrator with Read on every folder
- **WHEN** a tenant administrator holding Read on every folder requests a backup
- **THEN** the backup is produced

#### Scenario: Administrator with a folder without Read
- **WHEN** a tenant administrator lacks Read on one folder of the tenant
- **THEN** the request is rejected as forbidden and the denied audit entry is recorded

#### Scenario: Non-administrator
- **WHEN** a user without the tenant administrator role requests a backup
- **THEN** the request is rejected as forbidden

### Requirement: Backups are encrypted with a passphrase-derived key
A backup SHALL be a single binary file. Its key SHALL be derived from the passphrase with PBKDF2-HMAC-SHA256 using 600,000 iterations and a random 32-byte salt, and the content SHALL be encrypted with AES-256-GCM using a random 12-byte nonce and a 16-byte authentication tag. The header of the file, including the format identifier, the key derivation parameters, the salt, and the nonce, SHALL be authenticated as associated data. The plaintext MUST NOT be written to disk or to any log.

#### Scenario: Backup decrypts only with the passphrase
- **WHEN** a backup is opened with the correct passphrase
- **THEN** its content is decrypted and its integrity is verified

#### Scenario: Backup with a wrong passphrase
- **WHEN** a backup is opened with a different passphrase
- **THEN** decryption fails and no content is returned

#### Scenario: Header tampered
- **WHEN** any byte of the header of a backup is changed
- **THEN** decryption fails

### Requirement: Passphrases are validated and never stored
The passphrase SHALL be at least 12 characters long and SHALL be normalized to Unicode Normalization Form C before key derivation. A passphrase shorter than 12 characters SHALL be rejected as bad request before any content is read. The system MUST NOT store the passphrase or any key derived from it.

#### Scenario: Short passphrase
- **WHEN** a backup is requested with an 11-character passphrase
- **THEN** the request is rejected as bad request

#### Scenario: Passphrase not retained
- **WHEN** a backup has been produced
- **THEN** no stored record contains the passphrase or a key derived from it

### Requirement: Backup content covers the tenant vault without credentials
The decrypted content SHALL describe the whole tenant vault, including its manifest, folders, secrets with all versions and values, groups, grants, policies, members, and audit entries. It MUST NOT contain credentials or sessions.

#### Scenario: Content of a tenant with versions
- **WHEN** a backup is produced for a tenant with one secret of three versions
- **THEN** the content contains that secret with all three versions and their values

#### Scenario: Credentials excluded
- **WHEN** a backup is produced
- **THEN** the content contains no password hash, token, or session record

### Requirement: Backup format is versioned
The header SHALL identify the format and its version. A reader SHALL reject a backup whose format version it does not support. The format version is 1.

#### Scenario: Unsupported format version
- **WHEN** a backup header declares a format version that the reader does not support
- **THEN** the backup is rejected without decryption

### Requirement: Backup export is bounded and audited
A backup SHALL NOT exceed the operator-configured maximum size, which defaults to 256 MiB. A backup that would exceed it SHALL be refused as payload too large. Every successful export SHALL be recorded in the audit log with the action "tenant backup exported", the tenant as target, and a backup identifier as secondary target.

#### Scenario: Successful export audited
- **WHEN** a tenant administrator completes a backup export
- **THEN** an audit entry "tenant backup exported" is recorded with the tenant as target and the backup identifier as secondary target

#### Scenario: Backup over the size limit
- **WHEN** the tenant content would exceed the configured maximum size
- **THEN** the request is rejected as payload too large and no file is produced
