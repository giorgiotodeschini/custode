# secret-encryption Specification

## Purpose
Defines how secret values are protected at rest so that the stored data alone does not reveal any secret, and so that encryption keys are managed centrally and survive server restarts.

## Requirements
### Requirement: Secret values are encrypted at rest
The system SHALL store every secret value in encrypted form. A secret value MUST NOT be persisted in plaintext in any storage, including logs and backups produced by the system.

#### Scenario: Secret is stored
- **WHEN** a secret value is saved
- **THEN** the persisted value is not equal to the plaintext and cannot be read without the encryption keys

#### Scenario: Secret value in logs
- **WHEN** a secret is created, read, or updated
- **THEN** no log entry contains the plaintext secret value

### Requirement: Encryption keys are persisted and centrally managed
The encryption keys SHALL be managed by a single key ring shared by the whole server, and the key ring SHALL persist across restarts so that previously encrypted secrets remain readable.

#### Scenario: Server restarts
- **WHEN** the server restarts
- **THEN** previously stored secrets are decrypted successfully with the persisted keys

#### Scenario: Key ring is missing
- **WHEN** the server starts and the persisted key ring cannot be loaded
- **THEN** the server does not serve secret operations and reports the failure

### Requirement: Key rotation keeps existing secrets readable
The system SHALL support rotation of encryption keys such that secrets encrypted with earlier keys remain readable after rotation.

#### Scenario: New key becomes active
- **WHEN** a new encryption key is activated
- **THEN** new secrets are encrypted with the new key and existing secrets remain readable

### Requirement: Protected values are tied to their purpose
Encrypted values SHALL be bound to a purpose identifier that names what kind of data they protect. A value protected for one purpose MUST NOT be decrypted under a different purpose.

#### Scenario: Value decrypted with a different purpose
- **WHEN** a value protected for one purpose is presented for decryption under another purpose
- **THEN** decryption fails and no plaintext is returned

### Requirement: Secrets remain protected in the tenant scope
Encrypted secret values SHALL be stored together with their tenant identifier, and the purpose binding SHALL include the tenant so that a secret cannot be moved to another tenant and still decrypt.

#### Scenario: Secret record moved to another tenant
- **WHEN** an encrypted secret record is altered to belong to a different tenant and decryption is attempted
- **THEN** decryption fails and no plaintext is returned
