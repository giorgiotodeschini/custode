# Spec Delta

## Purpose
Defines how users browse the folder hierarchy, find secrets in a dense table or a card grid, and copy credentials to the clipboard without displaying them, with automatic clipboard cleanup.

## ADDED Requirements

### Requirement: Folder tree reflects the server hierarchy
The web UI SHALL show a folder tree rooted at `/` that reflects the folders returned by the server for the caller. Folders the caller cannot see MUST NOT appear in the tree. Selecting a folder SHALL show the secrets in that folder.

#### Scenario: Folder selected
- **WHEN** the user selects a folder in the tree
- **THEN** the secrets of that folder are listed and the folder path is shown in the URL

#### Scenario: Hidden folder
- **WHEN** the server returns no entry for a folder the caller has no permission on
- **THEN** that folder does not appear in the tree

### Requirement: Secrets are listed in a dense table or a card grid
The secret list SHALL be shown either as a dense table (name, type, folder, last updated, actions) or as a card grid (name, type, folder, last updated, actions). The user SHALL switch between the two presentations without reloading the list.

#### Scenario: Switch to card grid
- **WHEN** the user selects the card grid presentation
- **THEN** the same secrets are shown as cards without a new request to the server

#### Scenario: Switch to dense table
- **WHEN** the user selects the dense table presentation
- **THEN** the same secrets are shown as table rows with the same columns

### Requirement: Presentation choice persists per user on this browser
The chosen presentation SHALL be stored in browser storage keyed by the signed-in user's identity, so that the choice is restored on the next visit and is not applied to a different account on the same browser. When no choice exists, the table SHALL be used on desktop viewports and the card grid on mobile viewports.

#### Scenario: Choice restored
- **WHEN** a user chose the card grid and returns to the application later on the same browser
- **THEN** the card grid is shown

#### Scenario: Different account on the same browser
- **WHEN** a second user signs in on the same browser
- **THEN** the second user's presentation is not changed by the first user's choice

#### Scenario: Default presentation
- **WHEN** no presentation has been chosen and the viewport is narrower than 768 pixels
- **THEN** the card grid is shown

### Requirement: Listings never show secret values
The secret list and the secret detail panel SHALL show only the name, type, folder path, file name when applicable, and timestamps. Secret values MUST NOT be displayed or present in the page until the user explicitly requests a copy action.

#### Scenario: List rendered
- **WHEN** a folder with secrets is displayed
- **THEN** no secret value, password, or file content is present in the rendered page

### Requirement: One-click copy places a credential on the clipboard
Each credential field of a secret (username, password, token or API key value) SHALL have a copy action. A copy action SHALL read the value from the server on demand, place it on the clipboard, and discard the value from the page state once copied. A copy action for a value the caller cannot read SHALL show the server's refusal and MUST NOT modify the clipboard.

#### Scenario: Password copied
- **WHEN** the user activates the copy action for the password of a username/password secret
- **THEN** the password is placed on the clipboard and the value is not displayed

#### Scenario: Copy refused
- **WHEN** the user activates a copy action and the server denies the read
- **THEN** the message "You do not have Read permission on this folder" is shown and the clipboard is unchanged

#### Scenario: Clipboard unavailable
- **WHEN** the browser refuses clipboard access
- **THEN** the user is told that the copy failed and no value is shown

### Requirement: Copy shows visual and announced feedback
A successful copy SHALL change the copy control to a "Copied" state for two seconds and SHALL announce the copy through a live region. The announcement MUST NOT contain the copied value.

#### Scenario: Feedback after copy
- **WHEN** a copy succeeds
- **THEN** the control shows "Copied" for two seconds and the live region announces that the value was copied

### Requirement: The clipboard is cleared automatically after copy
After a successful copy, the application SHALL clear the clipboard after the clipboard clear delay. The default delay is 30 seconds. The delay MUST be configurable by tenant policy. The application MUST NOT clear the clipboard if the user has since copied other content through the application.

#### Scenario: Automatic clear
- **WHEN** a credential has been copied and 30 seconds pass without another copy through the application
- **THEN** the clipboard is cleared

#### Scenario: Newer copy preserved
- **WHEN** the user copies a second credential before the first clear delay ends
- **THEN** the first clear is cancelled and the second clear is scheduled from the second copy

#### Scenario: Countdown visible
- **WHEN** a credential is on the clipboard
- **THEN** the copy control shows the remaining seconds until the clipboard is cleared

### Requirement: Secret detail offers permitted actions only
Selecting a secret SHALL open a detail panel with its metadata and actions for edit, move, delete, and version history. An action MUST be shown only when the server reports that the caller has the permission it requires. Delete and move actions MUST ask for confirmation before the request is sent.

#### Scenario: Read-only user
- **WHEN** a user with Read but without Write or Delete on the folder opens a secret
- **THEN** the edit, move, and delete actions are not shown

#### Scenario: Delete confirmed
- **WHEN** the user confirms the deletion of a secret
- **THEN** the secret is deleted and removed from the list

#### Scenario: Delete cancelled
- **WHEN** the user cancels the deletion confirmation
- **THEN** the secret is not deleted

### Requirement: Folder operations report server refusals
Creating and deleting folders SHALL show the server's reason when the operation is rejected, including a duplicate folder path and a non-empty folder.

#### Scenario: Non-empty folder deletion
- **WHEN** the user deletes a folder that still contains secrets or subfolders
- **THEN** the deletion is not applied and the message states that the folder is not empty
