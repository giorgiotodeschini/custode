# secret-management Specification

## Purpose
Defines the types of secrets Custode can store and how secrets are organized into a hierarchy of folders within a tenant, so that users can find, group, and govern their secrets consistently.

## Requirements

### Requirement: Secrets have a supported type
Every secret SHALL have exactly one type. The supported types are username/password, personal access token (PAT), API key, file-based secret, structured secret, and key-value secret. A secret MUST NOT be stored without a supported type.

#### Scenario: Secret created with a supported type
- **WHEN** a user creates a secret with type username/password, PAT, API key, file-based, structured, or key-value
- **THEN** the secret is stored with that type

#### Scenario: Secret created without a supported type
- **WHEN** a user creates a secret with a missing or unsupported type
- **THEN** the request is rejected and nothing is stored

### Requirement: Username/password secrets hold a username and a password
A username/password secret SHALL contain a username and a password. Both values are required and both are protected as secret values.

#### Scenario: Username/password secret created with both values
- **WHEN** a user creates a username/password secret with a username and a password
- **THEN** the secret is stored with both values in encrypted form

#### Scenario: Username/password secret missing the password
- **WHEN** a user creates a username/password secret without a password
- **THEN** the request is rejected and nothing is stored

### Requirement: Token and API key secrets hold a single value
A PAT secret and an API key secret SHALL each contain one secret value, stored in encrypted form.

#### Scenario: API key secret created
- **WHEN** a user creates an API key secret with a value
- **THEN** the value is stored encrypted and the secret has type API key

#### Scenario: PAT secret created without a value
- **WHEN** a user creates a PAT secret without a value
- **THEN** the request is rejected and nothing is stored

### Requirement: File-based secrets accept certificate and key files
A file-based secret SHALL contain exactly one file. The file name MUST have one of the supported extensions: `.crt`, `.cer`, `.pem`, `.pfx`, `.p12`, or `.key`. The file content is stored in encrypted form, and the original file name is kept as metadata. The file size MUST NOT exceed a maximum configured by the operator.

#### Scenario: Certificate file stored
- **WHEN** a user creates a file-based secret from a `.pfx` file
- **THEN** the file content is stored encrypted and the original file name is kept

#### Scenario: Unsupported file extension
- **WHEN** a user creates a file-based secret from a file with an extension outside the supported set
- **THEN** the request is rejected and nothing is stored

#### Scenario: File exceeds the configured maximum size
- **WHEN** a user creates a file-based secret from a file larger than the configured maximum size
- **THEN** the request is rejected and nothing is stored

### Requirement: Secret listings never expose secret values
Listing or reading secret metadata SHALL return the name, type, folder path, and timestamps of each secret. Secret values, including file content, MUST NOT be included in listings. Values are returned only by an explicit value-read operation that is subject to access control.

#### Scenario: Secrets listed in a folder
- **WHEN** a user lists the secrets in a folder
- **THEN** each entry shows its metadata and no secret value or file content is returned

### Requirement: Folder paths form a normalized hierarchy
Each tenant SHALL have its own folder hierarchy rooted at `/`. A folder path MUST be absolute, use `/` as the separator, and be normalized so that the same folder always has the same path. Folder paths are unique within a tenant.

#### Scenario: Folder path normalized
- **WHEN** a user creates a folder with the path `production//aws/`
- **THEN** the folder is stored with the normalized path `/production/aws`

#### Scenario: Duplicate folder path
- **WHEN** a user creates a folder whose normalized path already exists in the tenant
- **THEN** the request is rejected and the existing folder is unchanged

#### Scenario: Relative folder path
- **WHEN** a user creates a folder with a path that is not absolute
- **THEN** the request is rejected and nothing is stored

### Requirement: Every secret belongs to exactly one folder
A secret SHALL belong to exactly one existing folder in its tenant. A secret MUST NOT be stored without a folder, and MUST NOT belong to a folder in another tenant.

#### Scenario: Secret created in a folder
- **WHEN** a user creates a secret in folder `/production/aws`
- **THEN** the secret is stored with that folder

#### Scenario: Secret created in a missing folder
- **WHEN** a user creates a secret in a folder path that does not exist
- **THEN** the request is rejected and nothing is stored

### Requirement: Folders are removed only when empty
A folder SHALL be deleted only when it contains no secrets and no subfolders. Deleting a non-empty folder MUST be rejected so that no secret is removed as a side effect.

#### Scenario: Empty folder deleted
- **WHEN** a user deletes a folder that contains no secrets and no subfolders
- **THEN** the folder is removed

#### Scenario: Non-empty folder deletion
- **WHEN** a user deletes a folder that contains at least one secret or subfolder
- **THEN** the request is rejected and the folder and its contents are unchanged

### Requirement: Secrets can be moved between folders
A user SHALL be able to move a secret to another existing folder in the same tenant. The move MUST be authorized by the permissions defined in `access-control` for both the source and the destination folder.

#### Scenario: Secret moved to a permitted folder
- **WHEN** a user moves a secret to a folder where the user has Create permission and from a folder where the user has Delete permission
- **THEN** the secret belongs to the destination folder

#### Scenario: Secret moved to a folder without Create permission
- **WHEN** a user moves a secret to a folder where the user has Read but lacks Create permission
- **THEN** the request is rejected as forbidden and the secret keeps its original folder

### Requirement: Structured secrets hold named string fields
A structured secret SHALL contain one or more fields. Each field SHALL have a name and a string value. A field name MUST be non-empty, MUST NOT exceed 128 characters, and MUST be unique within the secret, compared case-sensitively. A field value MAY be empty and MUST NOT exceed 4096 characters. A structured secret MUST NOT contain more than 100 fields. Field names and values SHALL be stored in encrypted form.

#### Scenario: Structured secret created
- **WHEN** a user creates a structured secret with the fields `host` and `password`
- **THEN** the secret is stored with type structured and both fields in encrypted form

#### Scenario: Duplicate field name
- **WHEN** a structured secret is submitted with two fields of the same name, including a JSON object with a repeated property
- **THEN** the request is rejected and nothing is stored

#### Scenario: Field value too long
- **WHEN** a field value exceeds 4096 characters
- **THEN** the request is rejected and nothing is stored

### Requirement: Key-value secrets hold ordered pairs
A key-value secret SHALL contain one or more pairs, stored in the order given. A key MUST be non-empty, MUST NOT exceed 128 characters, and MUST be unique within the secret, compared case-sensitively. A value MAY be empty and MUST NOT exceed 4096 characters. A key-value secret MUST NOT contain more than 200 pairs. Keys and values SHALL be stored in encrypted form.

#### Scenario: Key-value secret created
- **WHEN** a user creates a key-value secret with three pairs
- **THEN** the secret is stored with type key-value and the pairs keep the submitted order

#### Scenario: Empty key
- **WHEN** a pair has an empty key
- **THEN** the request is rejected and nothing is stored

#### Scenario: Too many pairs
- **WHEN** a key-value secret is submitted with 201 pairs
- **THEN** the request is rejected and nothing is stored

### Requirement: Secrets can be found by name
The system SHALL provide `GET /api/search`, which matches folder paths and secret names case-insensitively by substring and returns only the folders and secrets the caller can see, never a secret value.

#### Scenario: Secret found by name
- **WHEN** a user with Read on `/production` searches for `aws`
- **THEN** the secrets in `/production` whose names contain `aws` are returned with their folder paths and no values

#### Scenario: Secret the caller cannot read
- **WHEN** a user with no permission on the folder of a secret searches for the secret's name
- **THEN** the secret is not returned

#### Scenario: Wildcard matched literally
- **WHEN** a user searches for `%`
- **THEN** only names or paths that contain the literal `%` are returned

#### Scenario: Folder visible with any permission
- **WHEN** a user holds only Write on a folder and searches for part of its path
- **THEN** the folder is returned

#### Scenario: Limit above the maximum
- **WHEN** a user requests a limit of 51
- **THEN** the request is rejected as bad request

#### Scenario: Empty query
- **WHEN** a user searches with a query that is empty after trimming
- **THEN** the request is rejected as bad request

#### Scenario: Results truncated
- **WHEN** a search examines 5,000 candidates before reaching the limit
- **THEN** the response reports that the results are truncated
