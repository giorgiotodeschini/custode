# Spec Delta

## Purpose
Defines bulk import and export of secrets in JSON and CSV, and the encrypted tenant backup, so that secrets can move in and out of Custode without exposing values in plaintext by default and without weakening the tenant's protection.

## ADDED Requirements

### Requirement: JSON export of a folder
The web UI SHALL export the secrets of a selected folder and its subfolders that the caller can read to a JSON file. The export SHALL include the folder path, name, type, and metadata of each secret. Secret values SHALL be included only when the user explicitly confirms that the file will contain plaintext values.

#### Scenario: Export with values confirmed
- **WHEN** the user exports a folder and confirms that values are included
- **THEN** a JSON file is downloaded containing the metadata and values of each readable secret

#### Scenario: Export without values
- **WHEN** the user exports a folder without including values
- **THEN** the JSON file contains the metadata of each readable secret and no value

#### Scenario: Unreadable secrets excluded
- **WHEN** a folder contains secrets the caller cannot read
- **THEN** those secrets are not included in the export

### Requirement: CSV export neutralizes spreadsheet formulas
The web UI SHALL export secrets to CSV with one row per secret. Cells whose text begins with `=`, `+`, `-`, `@`, tab, or carriage return MUST be prefixed so that spreadsheet software does not evaluate them as formulas. CSV export SHALL exclude values by default and MUST require explicit confirmation to include them.

#### Scenario: Cell starts with a formula character
- **WHEN** a secret name begins with `=`
- **THEN** the exported cell is prefixed so that it is not evaluated as a formula

#### Scenario: Values excluded by default
- **WHEN** the user exports CSV without confirming plaintext values
- **THEN** the CSV has no value column

### Requirement: Import is validated before any secret is stored
The web UI SHALL parse a selected JSON or CSV file and show a preview before importing. The preview SHALL show the number of secrets to create, each row that fails validation with its reason, and any duplicate names. Validation SHALL apply the same rules as creating a secret through the form.

#### Scenario: Preview shows errors
- **WHEN** a CSV file contains a row with an unsupported secret type
- **THEN** the preview lists that row with the reason and the import cannot be confirmed

#### Scenario: Valid file previewed
- **WHEN** a JSON file contains five valid secrets
- **THEN** the preview shows that five secrets will be created

### Requirement: Import is all-or-nothing
An import SHALL either create every secret in the file or none of them. If any row fails validation or the server rejects any secret, the import SHALL NOT store any of the file's secrets and SHALL report the failing rows.

#### Scenario: One invalid row
- **WHEN** a file of ten secrets contains one row that the server rejects
- **THEN** none of the ten secrets is stored and the failing row is reported

### Requirement: Import requires Create permission on target folders
An import SHALL only create secrets in folders where the caller holds Create permission. An import that targets a folder without Create permission SHALL be refused before any secret is stored.

#### Scenario: Target folder without Create
- **WHEN** an import targets a folder where the caller lacks Create permission
- **THEN** the import is refused and nothing is stored

### Requirement: Duplicate names are reported and not overwritten
An import SHALL NOT overwrite an existing secret. A secret whose name already exists in the target folder MUST be reported as a conflict, and the import SHALL NOT be confirmed until the conflict is resolved by skipping that row or by renaming it.

#### Scenario: Name conflict
- **WHEN** an imported secret has the same name as an existing secret in the target folder
- **THEN** the preview marks the conflict and the existing secret is unchanged

### Requirement: Encrypted tenant backup export is restricted to administrators
Only a tenant administrator SHALL be able to export an encrypted backup of the tenant. The backup SHALL be encrypted with a key derived from a passphrase chosen by the administrator at export time. The passphrase MUST be entered twice and MUST meet a minimum length of 12 characters. The application MUST NOT store the passphrase.

#### Scenario: Backup exported
- **WHEN** a tenant administrator enters a matching passphrase of at least 12 characters and starts the export
- **THEN** an encrypted backup file is downloaded and the passphrase is not stored

#### Scenario: Passphrase mismatch
- **WHEN** the two passphrase entries differ
- **THEN** the export is not started

#### Scenario: Non-administrator
- **WHEN** a user without the tenant administrator role opens the backup export
- **THEN** the export is not available

### Requirement: The backup warning states the consequence of a lost passphrase
The backup export SHALL show a warning before the export starts that the backup cannot be opened without its passphrase and that the passphrase cannot be recovered.

#### Scenario: Warning shown
- **WHEN** the administrator opens the backup export
- **THEN** the unrecoverable-passphrase warning is displayed before the passphrase fields

### Requirement: Transfer operations report progress without leaking values
Long-running imports and exports SHALL show progress and SHALL allow the user to cancel before completion. Progress messages, error messages, and any on-screen log MUST NOT contain secret values or file content.

#### Scenario: Cancelled import
- **WHEN** the user cancels an import before it is confirmed
- **THEN** no secret is stored

#### Scenario: Error message content
- **WHEN** an import fails for a row containing a secret value
- **THEN** the error message identifies the row by name and folder and does not contain the value
