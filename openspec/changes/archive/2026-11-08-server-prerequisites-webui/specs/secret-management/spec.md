# Spec Delta

## MODIFIED Requirements

### Requirement: Secrets have a supported type
Every secret SHALL have exactly one type. The supported types are username/password, personal access token (PAT), API key, file-based secret, structured secret, and key-value secret. A secret MUST NOT be stored without a supported type.

#### Scenario: Secret created with a supported type
- **WHEN** a user creates a secret with type username/password, PAT, API key, file-based, structured, or key-value
- **THEN** the secret is stored with that type

#### Scenario: Secret created without a supported type
- **WHEN** a user creates a secret with a missing or unsupported type
- **THEN** the request is rejected and nothing is stored

## ADDED Requirements

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
