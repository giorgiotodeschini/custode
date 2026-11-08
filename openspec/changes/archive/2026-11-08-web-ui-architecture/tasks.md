# Tasks

Scope: web UI only (`src/Custode.WebUi/`). Server prerequisites 1 to 8 (see `proposal.md`) are separate OpenSpec changes. Tasks marked **(blocked by N)** MUST NOT start until server prerequisite N is specified, implemented, and applied. Server work is ordered first, as required by `openspec/config.yaml`.

## 0. Prerequisites and conventions

- [x] 0.1 Confirm that a separate server OpenSpec change exists for each prerequisite 1 to 8 before any task marked blocked starts. Record the change identifiers in this file. (spec: webui-session-security, "Renewal happens before expiry and is shared")
  - Server prerequisites 1 to 8 are delivered by the single archived change `2026-11-08-server-prerequisites-webui` (`openspec/changes/archive/2026-11-08-server-prerequisites-webui`). Its tasks 1.1 to 12.6 are all complete. Mapping: 1 refresh tokens (session-management), 2 secret versioning, 3 structured and key-value types (secret-management), 4 import and backup (secret-transfer, tenant-backup), 5 tenant administration, 6 access-control read contracts, 7 search (secret-management), 8 audit hash links and cursor paging (security-audit).
- [x] 0.2 Add the two-line copyright header from `AGENTS.md` (rule 5) as the first lines of every new `.ts` file under `src/Custode.WebUi/`, excluding generated files, configuration, JSON, HTML, and CSS. Verify with a search over the changed files. (spec: webui-platform, "The web UI is a TypeScript Angular application")
- [x] 0.3 Verify the Angular, Angular CDK, and Tailwind CSS APIs used by this change against Context7 or the official documentation before writing code, and pin the versions in `package.json`. (spec: webui-platform, "Angular CDK provides the standard UI primitives")

## 1. Workspace and platform

- [x] 1.1 Scaffold the Angular workspace at `src/Custode.WebUi/` with strict TypeScript and standalone components. Verify that the production build reports zero TypeScript and template errors. (spec: webui-platform, "The web UI is a TypeScript Angular application")
- [x] 1.2 Configure Tailwind CSS with a class-based dark variant and one set of design tokens for colors, spacing, typography, and radii. Verify that no component defines colors outside the tokens. (spec: webui-platform, "Styling uses Tailwind CSS design tokens")
- [x] 1.3 Implement the theme service: explicit user choice stored in browser storage, fallback to the operating color scheme, and a pre-paint script in `index.html` that prevents a wrong-theme flash. Verify the choice persists across reloads. (spec: webui-platform, "Dark mode is supported")
- [x] 1.4 Verify text contrast of every token pair used for text in both themes is at least 4.5:1. (spec: webui-platform, "Text contrast in both themes")
- [x] 1.5 Implement the responsive layout with CDK layout breakpoints: persistent sidebar on wide viewports, drawer on narrow viewports, and no horizontal scroll at 360 pixels. (spec: webui-platform, "Layout adapts to desktop and mobile viewports")
- [x] 1.6 Implement the accessibility baseline: visible focus indicators, accessible names on all controls, and reduced motion support. Verify with an automated accessibility check on each screen in both themes. (spec: webui-platform, "The UI meets the WCAG 2.2 AA baseline")
- [x] 1.7 Add unit tests that fail if any service writes tokens or secret values to local storage, session storage, or a readable cookie. (spec: webui-platform, "Browser storage holds only UI preferences", "Secret value not stored")

## 2. Session security and API client

- [x] 2.1 Implement the API client with a functional HTTP interceptor that attaches the in-memory access token. Verify that the token is never written to browser storage. (spec: webui-session-security, "The access token is held in memory only")
- [x] 2.2 Implement the sign-in form with the generic "Invalid login or password" message for both unknown login and wrong password, without echoing the password. (spec: webui-session-security, "Sign-in shows a generic error for failed credentials")
- [x] 2.3 Implement the return URL check so that only relative paths within the application are honored. (spec: webui-session-security, "Return URL is restricted to the application")
- [x] 2.4 Implement token renewal at 80 percent of lifetime, one retry after an unauthorized response, and a single shared renewal for concurrent requests. **(blocked by 1)** (spec: webui-session-security, "Renewal happens before expiry and is shared")
- [x] 2.5 Implement refresh token rotation handling: a replayed token ends the session and shows the sign-in form with a notice. **(blocked by 1)** (spec: webui-session-security, "Refresh token rotation ends reused sessions")
- [x] 2.6 Verify with a test that the refresh token is only sent in the HTTP-only cookie and is never read by script. **(blocked by 1)** (spec: webui-session-security, "Refresh token not visible to script")
- [x] 2.7 Implement sign-out that revokes the refresh token on the server and returns to the sign-in form. **(blocked by 1)** (spec: webui-session-security, "Sign-out revokes the refresh token")
- [x] 2.8 Implement the idle tracker from pointer, keyboard, and touch events, with the deadline stored as a timestamp. Verify that a background tab recomputes the deadline from the clock. (spec: webui-session-security, "Idle sessions show a countdown before they end", "Background tab counted")
- [x] 2.9 Implement the idle countdown as a CDK Dialog with `role="alertdialog"`, focus on "Stay signed in", and a live announcement every 10 seconds. (spec: webui-session-security, "The countdown modal is accessible and does not block focus")
- [x] 2.10 Implement the session end path that clears the access token, decrypted state, open detail panels, and any application-copied clipboard content. (spec: webui-session-security, "Ending a session clears sensitive client state")
- [x] 2.11 Broadcast session end through `BroadcastChannel` so that other tabs show the sign-in form. (spec: webui-session-security, "Session end is shared across browser tabs")
- [x] 2.12 Report user activity to the server: call `POST /api/auth/activity` during interactions at most once per minute and when "Stay signed in" is activated; send nothing while idle; run the session end path when a report is refused. Verify with a test that a twenty-minute form edit with interactions is not ended by the server. (spec: webui-session-security, "User interaction keeps the server session alive"; design D3)

## 3. Shell and navigation

- [x] 3.1 Implement the sidebar with Folders, Settings, Audit Logs, and Tenant Administration, showing the last two only to tenant administrators. (spec: webui-shell-navigation, "A persistent sidebar provides core navigation")
- [x] 3.2 Register the route map, including `/folders/**`, `/secrets/new`, and `/secrets/:id/edit` as full pages, and a not-found page with a link back to folders. (spec: webui-shell-navigation, "Routes address every screen by URL", "Unknown route")
- [x] 3.3 Implement the administrator guard that redirects non-administrators to folders with a notice, and make an edit route for an unreadable secret show the not-found page. (spec: webui-shell-navigation, "Unauthorized screens behave as unavailable")
- [x] 3.4 Implement the command palette as a CDK Overlay that opens on `Ctrl+K` or `Cmd+K` from any screen, including inside text fields, and returns focus on Escape. (spec: webui-shell-navigation, "The command palette opens from any signed-in screen")
- [x] 3.5 Implement palette results grouped by folders, secrets, and actions, with keyboard navigation, and the quick actions "New secret", "Audit log", "Settings", and "Toggle theme" shown only when permitted. (spec: webui-shell-navigation, "Quick action executed", "Keyboard navigation of results", "Palette results are grouped and tenant-scoped")
- [x] 3.6 Add secret name search to the palette through the server search endpoint, returning only readable secrets. **(blocked by 7)** (spec: webui-shell-navigation, "Secret found by name", "Inaccessible secret not revealed")

## 4. Secret browser

- [x] 4.1 Implement the folder tree with the CDK tree and keyboard navigation, showing only the folders the server returns. (spec: webui-secret-browser, "Folder tree reflects the server hierarchy"; webui-platform, "Folder tree keyboard navigation")
- [x] 4.2 Implement the dense table and the card grid for the secret list, switching between them without a new request. (spec: webui-secret-browser, "Secrets are listed in a dense table or a card grid")
- [x] 4.3 Store the presentation choice per user identity in browser storage, with the viewport-based default when no choice exists. (spec: webui-secret-browser, "Presentation choice persists per user on this browser")
- [x] 4.4 Verify with a test that listings and the detail panel contain no secret value, password, or file content in the rendered page. (spec: webui-secret-browser, "Listings never show secret values")
- [x] 4.5 Implement the copy action: read the value on demand, place it on the clipboard through the CDK clipboard service, and drop it from state. Show the server refusal message and leave the clipboard unchanged on denial. (spec: webui-secret-browser, "One-click copy places a credential on the clipboard")
- [x] 4.6 Implement "Copied" feedback for two seconds with a live-region announcement that never includes the value. (spec: webui-secret-browser, "Copy shows visual and announced feedback")
- [ ] 4.7 Implement the clipboard clear timer with a generation counter, a countdown on the copy control, and the rule that a newer copy is never cleared by an older timer. Verify the clear path in each supported browser. (spec: webui-secret-browser, "The clipboard is cleared automatically after copy"; design D7)
- [x] 4.8 Implement the secret detail panel showing only the actions the server reports as permitted. **(blocked by 6)** (spec: webui-secret-browser, "Secret detail offers permitted actions only", "Read-only user")
- [x] 4.9 Implement delete and move with confirmation, and cancellation that changes nothing. (spec: webui-secret-browser, "Delete confirmed", "Delete cancelled")
- [x] 4.10 Implement folder create and delete, showing the server reason on a duplicate path or a non-empty folder. (spec: webui-secret-browser, "Folder operations report server refusals")

## 5. Secret editor

- [x] 5.1 Implement the full-page create route `/secrets/new` and edit route `/secrets/:id/edit` with a confirmation before discarding unsaved changes. (spec: webui-secret-editor, "Secret creation and editing are full-page forms")
- [x] 5.2 Limit the folder selector on creation to folders where the caller holds Create. **(blocked by 6)** (spec: webui-secret-editor, "Creation requires a folder the user can create in")
- [x] 5.3 Implement the username/password and PAT/API key forms with required-field validation, and show server messages inline. (spec: webui-secret-editor, "Username/password secrets require both values", "Token and API key secrets require one value")
- [x] 5.4 Implement the file-based form that accepts exactly one file by picker or drag and drop, and rejects unsupported extensions and oversize files before upload. (spec: webui-secret-editor, "Certificate files are uploaded as file-based secrets")
- [x] 5.5 Show only the file name and size after a file-based secret is saved, and require a new file to replace it. (spec: webui-secret-editor, "Certificate content is never displayed after upload")
- [x] 5.6 Mask all secret value fields by default with a reveal control, and set `autocomplete="off"`. (spec: webui-secret-editor, "Secret values are masked and kept out of autofill")
- [x] 5.7 Show the type as read-only on edit, and show the edit form read-only with no save control for users without Write. (spec: webui-secret-editor, "The secret type is fixed after creation", "Read-only users see the edit form without saving")
- [x] 5.8 Implement the structured secret form with named fields and unique-name validation. **(blocked by 3)** (spec: webui-secret-editor, "Structured secrets hold named fields")
- [x] 5.9 Implement the key-value secret form with ordered pairs and unique non-empty keys. **(blocked by 3)** (spec: webui-secret-editor, "Key-value secrets hold ordered pairs")
- [x] 5.10 Implement the version history list with sequence, timestamp, and author, without values. **(blocked by 2)** (spec: webui-secret-editor, "Version history lists previous versions")
- [x] 5.11 Implement the visual diff: changed, unchanged, added, and removed per field, with values hidden until the reveal control is used. Verify that the diff of a certificate shows change status only. **(blocked by 2)** (spec: webui-secret-editor, "Versions are compared with a visual diff", "Certificate content comparison")
- [x] 5.12 Implement one-click rollback with confirmation, shown only to users with Write. **(blocked by 2)** (spec: webui-secret-editor, "One-click rollback restores a previous version", "Rollback without Write")
- [x] 5.13 Send the version token with each save in `If-Match` and show the conflict dialog on a `412 Precondition Failed` response with options to reload or keep edits. **(blocked by 2)** (spec: webui-secret-editor, "Concurrent edits are detected before saving")

## 6. Secret transfer

- [x] 6.1 Implement JSON export of a folder and its readable subfolders, with values included only after explicit confirmation. Uses existing endpoints. (spec: webui-secret-transfer, "JSON export of a folder", "Unreadable secrets excluded")
- [x] 6.2 Implement CSV export with one row per secret, values excluded by default, and formula-character neutralization. Uses existing endpoints. (spec: webui-secret-transfer, "CSV export neutralizes spreadsheet formulas", "Values excluded by default")
- [x] 6.3 Implement JSON and CSV import parsing and a preview from the server's dry run, listing rows to create, failing rows with reasons, and conflicts. **(blocked by 4)** (spec: webui-secret-transfer, "Import is validated before any secret is stored")
- [x] 6.4 Implement all-or-nothing import, and disable confirmation while the dry run has errors. **(blocked by 4)** (spec: webui-secret-transfer, "Import is all-or-nothing")
- [x] 6.5 Refuse an import that targets a folder without Create before any secret is stored. **(blocked by 4)** (spec: webui-secret-transfer, "Import requires Create permission on target folders")
- [x] 6.6 Report name conflicts in the preview and require skipping or renaming before confirmation. **(blocked by 4)** (spec: webui-secret-transfer, "Duplicate names are reported and not overwritten")
- [x] 6.7 Implement the encrypted backup export for tenant administrators, with a passphrase entered twice, a 12-character minimum, the unrecoverable-passphrase warning, and no passphrase storage. **(blocked by 4)** (spec: webui-secret-transfer, "Encrypted tenant backup export is restricted to administrators", "The backup warning states the consequence of a lost passphrase")
- [x] 6.8 Implement progress and cancellation for transfers, and verify that messages contain identifiers only. (spec: webui-secret-transfer, "Transfer operations report progress without leaking values")

## 7. Folder ACL management

- [x] 7.1 Restrict the ACL view to tenant administrators. **(blocked by 6)** (spec: webui-access-control-management, "The folder ACL view is limited to tenant administrators")
- [x] 7.2 Implement the ACL table showing each principal's four permission levels and whether each is direct or inherited with its ancestor path. **(blocked by 6)** (spec: webui-access-control-management, "The ACL view lists effective and direct grants with their source")
- [x] 7.3 Show inheritance status for each folder in the table and in the tree. **(blocked by 6)** (spec: webui-access-control-management, "Inheritance status is shown for every folder")
- [x] 7.4 Implement the break inheritance toggle with a preview of lost access and a confirmation. **(blocked by 6)** (spec: webui-access-control-management, "Breaking inheritance shows the impact before it is applied")
- [x] 7.5 Implement the restore inheritance toggle with a preview of returning grants. **(blocked by 6)** (spec: webui-access-control-management, "Restoring inheritance shows the grants that will return")
- [x] 7.6 Implement access level presets (Viewer, Editor, Full) and a custom combination. **(blocked by 6)** (spec: webui-access-control-management, "Access levels are set for users and groups")
- [x] 7.7 Implement grant revocation with confirmation naming the principal and folder. **(blocked by 6)** (spec: webui-access-control-management, "Revoking a grant requires confirmation")
- [x] 7.8 Update the displayed state only after the server confirms, and show the refusal reason on a forbidden response. **(blocked by 6)** (spec: webui-access-control-management, "Refused ACL changes do not change the displayed state")
- [x] 7.9 Limit the principal selector to users and groups of the current tenant. **(blocked by 6)** (spec: webui-access-control-management, "Group and member selection is limited to the tenant")

## 8. Audit log viewer

- [x] 8.1 Restrict the audit viewer to tenant administrators. (spec: webui-audit-log-viewer, "The audit log viewer is limited to tenant administrators")
- [x] 8.2 Implement the filters for timeframe with presets, acting user, action type, and outcome, combined with AND and reflected in the route query. (spec: webui-audit-log-viewer, "Entries can be filtered by several criteria at once")
- [x] 8.3 Show entries in ascending sequence order with a descending option that changes only the display order, and with sequence, UTC and local time, actor, action, outcome, and target identifiers. (spec: webui-audit-log-viewer, "Entries are shown in sequence order")
- [x] 8.4 Label denied outcomes with text and an icon. (spec: webui-audit-log-viewer, "Outcomes are distinguishable without color alone")
- [x] 8.5 Label entries with the empty actor identifier as "Data layer". (spec: webui-audit-log-viewer, "Entries recorded by the data layer are labeled")
- [x] 8.6 Implement cursor-based paging on the sequence number with "Load more". **(blocked by 8)** (spec: webui-audit-log-viewer, "The viewer pages through long results")
- [x] 8.7 Implement "Verify integrity" through the server verification endpoint, showing intact with the entry count, altered with the first altered sequence, or "Not verified" before a run. (spec: webui-audit-log-viewer, "Hash chain integrity can be verified on demand")
- [x] 8.8 Show per-entry link status by comparing each entry's previous hash with the preceding loaded entry's hash, and mark the integrity status altered on a broken link. **(blocked by 8)** (spec: webui-audit-log-viewer, "Each entry shows its chain link status")
- [x] 8.9 Verify that the viewer renders only the fields the server returns and never requests secret values. (spec: webui-audit-log-viewer, "Audit entries never display secret values")

## 9. Tenant administration

- [x] 9.1 Restrict the Tenant Administration area to tenant administrators, with the Members, Groups, and Security Policies sections. (spec: webui-tenant-administration, "The tenant administration area is limited to tenant administrators")
- [x] 9.2 Implement the members list with display name, login, roles, group count, and status. **(blocked by 5)** (spec: webui-tenant-administration, "Members are listed with their status and roles")
- [x] 9.3 Implement member creation with an initial password, disable, and re-enable. **(blocked by 5)** (spec: webui-tenant-administration, "Members can be created, disabled, and re-enabled")
- [x] 9.4 Implement grant and withdrawal of the tenant administrator role, with the last-administrator action disabled. **(blocked by 5)** (spec: webui-tenant-administration, "Tenant administrator role can be granted and withdrawn")
- [x] 9.5 Implement the groups list with member and grant counts. **(blocked by 6)** (spec: webui-tenant-administration, "Groups are listed with membership and grant counts")
- [x] 9.6 Implement group create, rename, and delete, with the duplicate-name message and the deletion confirmation that states the member count. (spec: webui-tenant-administration, "Groups can be created, renamed, and deleted")
- [x] 9.7 Implement adding and removing members from a group, with the notice that changes apply on the next request. (spec: webui-tenant-administration, "Group membership is managed from the group")
- [x] 9.8 Implement the security policy form with ranges for idle timeout, refresh token lifetime, and clipboard clear delay, and the effect summary before saving. **(blocked by 5)** (spec: webui-tenant-administration, "Security policies are tenant-wide and validated", "Policy changes show their effect before saving")
- [x] 9.9 Show each administrative change as confirmed by the server, and keep the previous state with the reason on refusal. (spec: webui-tenant-administration, "Administrative changes are shown only after confirmation")

## 10. Verification

- [x] 10.1 Add unit tests for session renewal, the clipboard generation logic, the field diff, CSV formula neutralization, and form validation. (spec: webui-session-security; webui-secret-browser; webui-secret-editor; webui-secret-transfer)
- [ ] 10.2 Run the end-to-end suite of critical flows (sign-in, browse, copy, create, rollback, ACL change, audit filter) against the server on SQLite and against PostgreSQL 18 (Testcontainers), with zero failures on both. (spec: webui-shell-navigation, "Routes address every screen by URL"; webui-access-control-management; project rule AGENTS.md, section 3)
- [x] 10.3 Run the accessibility check on every screen in both themes. (spec: webui-platform, "The UI meets the WCAG 2.2 AA baseline")
- [x] 10.4 Run `openspec validate web-ui-architecture --strict` and verify zero errors and zero warnings. (project rule AGENTS.md, section 3)
- [x] 10.5 Verify that every new `.ts` file under `src/Custode.WebUi/` starts with the copyright header and that no generated file has it. (spec: webui-platform, "The web UI is a TypeScript Angular application"; project rule 5)
