# Spec Delta

## Purpose
Defines the full-page experience for creating and editing secrets of every supported type, including certificate files, and the version history with visual diffs and one-click rollback.

## ADDED Requirements

### Requirement: Secret creation and editing are full-page forms
Creating a secret SHALL use the full page at `/secrets/new`. Editing a secret SHALL use the full page at `/secrets/:id/edit`. Leaving either page with unsaved changes SHALL ask the user to confirm before discarding them.

#### Scenario: Unsaved changes on navigation
- **WHEN** the user has changed a field and attempts to leave the form
- **THEN** a confirmation asks whether to discard the changes

#### Scenario: Saved form
- **WHEN** the user saves the form successfully
- **THEN** the application shows the secret's folder and the saved secret

### Requirement: Creation requires a folder the user can create in
The folder selector on the creation form SHALL offer only folders where the caller holds Create permission, as reported by the server. A secret MUST belong to exactly one folder.

#### Scenario: Folder selector limited to permitted folders
- **WHEN** a user opens `/secrets/new`
- **THEN** only folders where the user holds Create permission are offered

#### Scenario: Save refused by the server
- **WHEN** the server refuses the create request with forbidden
- **THEN** the form shows that Create permission is required on the folder and nothing is saved

### Requirement: Username/password secrets require both values
The username/password form SHALL require a username and a password. Both fields SHALL be validated before the request is sent, and server validation messages SHALL be shown inline.

#### Scenario: Password missing
- **WHEN** the user saves a username/password secret without a password
- **THEN** the password field shows a required message and no request is sent

### Requirement: Token and API key secrets require one value
The personal access token and API key forms SHALL each require one value.

#### Scenario: API key saved
- **WHEN** the user saves an API key secret with a value
- **THEN** the secret is created with type API key

#### Scenario: PAT without value
- **WHEN** the user saves a PAT secret without a value
- **THEN** the form shows a required message and no request is sent

### Requirement: Certificate files are uploaded as file-based secrets
The file-based form SHALL accept exactly one file selected by a file picker or by drag and drop. The form SHALL reject files whose extension is not `.crt`, `.cer`, `.pem`, `.pfx`, `.p12`, or `.key`, and files larger than the limit reported by the server, before any upload. The server validation remains authoritative.

#### Scenario: Supported certificate selected
- **WHEN** the user selects a `.pfx` file within the size limit
- **THEN** the file name is shown and the form can be saved

#### Scenario: Unsupported extension selected
- **WHEN** the user selects a `.txt` file
- **THEN** the file is rejected with a message listing the supported extensions

#### Scenario: Two files selected
- **WHEN** the user selects more than one file
- **THEN** the form accepts none of them and asks for a single file

### Requirement: Certificate content is never displayed after upload
After a file-based secret is saved, the edit form SHALL show the original file name and size only. The file content MUST NOT be displayed. Replacing the file SHALL require selecting a new file.

#### Scenario: Edit form for a certificate
- **WHEN** a user opens the edit form of a file-based secret
- **THEN** the file name is shown and the content is not

### Requirement: Structured secrets hold named fields
A structured secret SHALL contain one or more named fields. Each field has a unique non-empty name and a value. Field values SHALL be masked by default.

#### Scenario: Structured secret saved
- **WHEN** the user saves a structured secret with two named fields
- **THEN** the secret is created with type structured and both fields are kept

#### Scenario: Duplicate field name
- **WHEN** the user adds a field whose name already exists in the secret
- **THEN** the form rejects the duplicate name and nothing is saved

### Requirement: Key-value secrets hold ordered pairs
A key-value secret SHALL contain one or more key-value pairs. Keys MUST be unique and non-empty within the secret. Values SHALL be masked by default.

#### Scenario: Key-value secret saved
- **WHEN** the user saves a key-value secret with three pairs
- **THEN** the secret is created with type key-value and the pairs keep their order

#### Scenario: Empty key
- **WHEN** a pair has an empty key
- **THEN** the form rejects the pair and nothing is saved

### Requirement: Secret values are masked and kept out of autofill
Every field that holds a secret value SHALL be masked by default. A reveal control MAY show the value for as long as the user holds it or until the form is left. Fields holding secret values MUST be excluded from browser autofill and suggestions.

#### Scenario: Reveal toggled
- **WHEN** the user activates the reveal control for a password field
- **THEN** the password is shown until the control is released or the user leaves the form

#### Scenario: Form left
- **WHEN** the user leaves the form after revealing a value
- **THEN** the value is no longer held in the page state

### Requirement: The secret type is fixed after creation
The type of an existing secret SHALL NOT change through the edit form. The edit form SHALL display the type as read-only.

#### Scenario: Type displayed on edit
- **WHEN** a user opens the edit form of an existing secret
- **THEN** the type is shown as read-only

### Requirement: Read-only users see the edit form without saving
A user who can read a secret but lacks Write permission on its folder SHALL see the edit form in read-only mode with no save control.

#### Scenario: Read without Write
- **WHEN** a user with Read but without Write opens the edit form
- **THEN** all fields are read-only and no save control is displayed

### Requirement: Version history lists previous versions
The edit form SHALL show the version history of the secret, with each version's sequence number, timestamp, and the user who created it. The list MUST NOT contain secret values.

#### Scenario: History shown
- **WHEN** the user opens the version history of a secret with three versions
- **THEN** three versions are listed with their sequence, timestamp, and author

### Requirement: Versions are compared with a visual diff
The user SHALL select two versions to compare them. The diff SHALL show, for each field, whether it is unchanged, changed, added, or removed. Field values MUST be hidden in the diff by default. A "reveal values" control MAY show the values of the compared fields, and the reveal SHALL be cleared when the user leaves the view.

#### Scenario: Changed field
- **WHEN** the user compares two versions in which the password differs
- **THEN** the password field is marked changed and its values are hidden

#### Scenario: Values revealed
- **WHEN** the user activates "reveal values" in the diff
- **THEN** the compared values are displayed until the view is closed

#### Scenario: Certificate content comparison
- **WHEN** the user compares two versions of a file-based secret with different content
- **THEN** the diff shows the file as changed and does not show its content

### Requirement: One-click rollback restores a previous version
A user with Write permission SHALL be able to roll back a secret to a previous version with one action after confirming. Rollback SHALL create a new current version with the content of the selected version, and the previous history SHALL be kept.

#### Scenario: Rollback confirmed
- **WHEN** the user confirms the rollback to version 2 of a secret that is currently at version 4
- **THEN** a new version 5 is created with the content of version 2 and versions 1 to 4 remain in the history

#### Scenario: Rollback without Write
- **WHEN** a user without Write permission views the history
- **THEN** the rollback action is not shown

### Requirement: Concurrent edits are detected before saving
When a secret has changed after the edit form was loaded, saving SHALL NOT overwrite the newer version silently. Each save sends the version token of the loaded secret in the `If-Match` header. A stale token is refused by the server with `412 Precondition Failed`, and the form SHALL show that the secret changed and offer to reload the latest version or to keep the user's edits in the form.

#### Scenario: Conflicting save
- **WHEN** a user saves an edit form loaded from an older version of the secret, and the server answers `412 Precondition Failed`
- **THEN** the save is not applied and the conflict message is displayed

#### Scenario: Save without a loaded version token
- **WHEN** the edit form has no version token for the secret, for example because loading failed
- **THEN** the save control is disabled and no request is sent
