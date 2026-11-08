# Spec Delta

## Purpose
Defines the application shell shared by every signed-in screen: the persistent sidebar, the route map including the full-page secret routes, and the global command palette for fast search and navigation.

## ADDED Requirements

### Requirement: A persistent sidebar provides core navigation
The web UI SHALL show a sidebar with the entries Folders, Settings, and Audit Logs. Tenant Administration SHALL also appear in the sidebar. Audit Logs and Tenant Administration MUST only be shown to users with the tenant administrator role. The sidebar MUST stay visible across navigation on desktop viewports.

#### Scenario: Regular user opens the application
- **WHEN** a signed-in user without the tenant administrator role views the sidebar
- **THEN** the Folders and Settings entries are shown and the Audit Logs and Tenant Administration entries are not

#### Scenario: Tenant administrator opens the application
- **WHEN** a signed-in tenant administrator views the sidebar
- **THEN** the Folders, Settings, Audit Logs, and Tenant Administration entries are shown

#### Scenario: Navigation keeps the sidebar
- **WHEN** the user moves from one screen to another on a desktop viewport
- **THEN** the sidebar remains visible and shows the current entry as selected

### Requirement: Routes address every screen by URL
Every screen SHALL have a URL. Folder screens SHALL use the folder path in the URL, so that a folder can be shared by link. The secret creation and editing screens SHALL be full-page routes at `/secrets/new` and `/secrets/:id/edit`, and MUST NOT be implemented only as dialogs.

#### Scenario: Folder deep link
- **WHEN** a signed-in user opens a link to a folder path they can read
- **THEN** the folder screen for that path is displayed

#### Scenario: Create secret route
- **WHEN** a user with Create permission on a folder opens `/secrets/new`
- **THEN** the full-page secret creation form is displayed

#### Scenario: Edit secret route
- **WHEN** a user opens `/secrets/:id/edit` for a secret they can read
- **THEN** the full-page secret editing form is displayed

#### Scenario: Unknown route
- **WHEN** a user opens a URL that does not match any screen
- **THEN** a not-found page is displayed with a link back to the folders screen

### Requirement: Unauthorized screens behave as unavailable
A user who opens a tenant administration screen without the tenant administrator role SHALL be redirected to the folders screen with a notice that the page is not available. A user who opens the edit route of a secret they cannot read SHALL see the same not-found page as for a secret that does not exist.

#### Scenario: Regular user opens the audit log URL
- **WHEN** a user without the tenant administrator role opens the audit log URL
- **THEN** the user is redirected to the folders screen and a notice is displayed

#### Scenario: Edit route for an unreadable secret
- **WHEN** a user opens `/secrets/:id/edit` for a secret they have no permission on
- **THEN** the same not-found page is displayed as for a non-existent secret

### Requirement: The command palette opens from any signed-in screen
The web UI SHALL open a global command palette when the user presses `Ctrl+K` on Windows and Linux or `Cmd+K` on macOS. The palette SHALL also open from a visible search control. The shortcut MUST work while focus is inside a text field.

#### Scenario: Shortcut opens the palette
- **WHEN** a signed-in user presses `Ctrl+K` or `Cmd+K` on any screen
- **THEN** the command palette opens with focus in its search field

#### Scenario: Shortcut inside a text field
- **WHEN** focus is in a text input and the user presses the palette shortcut
- **THEN** the command palette opens and the text in the input is unchanged

#### Scenario: Palette closed
- **WHEN** the user presses Escape in the open palette
- **THEN** the palette closes and focus returns to the element that was focused before it opened

### Requirement: Palette search covers folders, secrets, and quick actions
The command palette SHALL return folders, secrets, and quick actions that match the typed text. Folder and secret results MUST include only entries the caller can read. Quick actions SHALL include creating a secret, opening the audit log, opening settings, and switching the theme. Quick actions MUST only be shown when the caller is permitted to perform them.

#### Scenario: Secret found by name
- **WHEN** the user types part of the name of a secret the user can read
- **THEN** that secret appears in the results with its folder path

#### Scenario: Inaccessible secret not revealed
- **WHEN** the user types the name of a secret the user cannot read
- **THEN** the secret does not appear in the results

#### Scenario: Quick action executed
- **WHEN** the user selects the "New secret" result and presses Enter
- **THEN** the application navigates to `/secrets/new`

#### Scenario: Keyboard navigation of results
- **WHEN** the user presses the Down Arrow key in the results list
- **THEN** the next result is highlighted and becomes the target of Enter

### Requirement: Palette results are grouped and tenant-scoped
Palette results SHALL be grouped by category (folders, secrets, actions) and SHALL only include data from the signed-in user's tenant.

#### Scenario: Results grouped
- **WHEN** the search matches a folder and a secret
- **THEN** the folder and the secret are listed under their own category headings
