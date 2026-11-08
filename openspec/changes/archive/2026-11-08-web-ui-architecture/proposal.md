# Proposal

## Why

The server now provides tenant isolation, local authentication, typed secret storage, folder-based access control, and a tamper-evident audit log, all covered by archived specifications and tests. Custode has no user interface yet, so administrators and users cannot use these capabilities. The server-first order in `openspec/config.yaml` makes the web UI the next component. Its architecture, route structure, security model for tokens and sessions, and user-facing behavior must be specified before any Angular code is written, so that the implementation can be checked against requirements.

## What Changes

- Add a new Angular (TypeScript) web application, `src/Custode.WebUi/`, styled with Tailwind CSS (with dark mode) and built on Angular CDK primitives (Clipboard, Tree, Overlay/Dialog, A11y, Layout). The application adapts from desktop dashboards to mobile layouts.
- Add a persistent responsive shell with sidebar navigation (Folders, Settings, Audit Logs), a global command palette (`Ctrl+K` / `Cmd+K`), and dedicated full-page routes for secret creation and editing (`/secrets/new`, `/secrets/:id/edit`).
- Add a secret browser with a folder tree, a dual-mode view (dense table or card grid) whose choice is persisted per user, and one-click credential copy with visual feedback and an automatic clipboard clear timer.
- Add a secret editor for username/password, PAT, API key, structured secrets, plain key-value pairs, and file-based certificates (`.crt`, `.cer`, `.pem`, `.pfx`, `.p12`, `.key`). The editor shows version history with visual diffs and one-click rollback.
- Add bulk JSON/CSV import and export of secrets, and an encrypted tenant backup export.
- Add session security: the access token is held in memory only, the refresh token is managed as an HTTP-only cookie, and an idle timeout shows a visual countdown modal before the session ends.
- Add a folder ACL management view that shows inheritance status, group and user access levels, and an explicit "Break Inheritance" / "Restore Inheritance" toggle.
- Add an audit log viewer with filters for user, timeframe, action type, and outcome, and with hash chain integrity indicators.
- Add a tenant administration area for members, groups, and tenant-wide security policies.

### Server prerequisites (blocking)

The current server does not expose the contracts that several requirements need. Each item below must be specified and delivered by its own server OpenSpec change before the dependent web UI tasks can be applied. Per `openspec/config.yaml`, server changes come first. This change does not write those server specifications.

1. **Refresh tokens** (modifies `local-authentication`). Sign-in issues a refresh token. A refresh endpoint rotates it, and sign-out revokes it. Blocks `webui-session-security`.
2. **Secret version history** (modifies `secret-management`, which currently puts version history out of scope). Each update creates a version. Endpoints list versions, read a version's value, and roll back to a version. Each secret reports a version token so that saves can detect concurrent edits. Blocks version history, diff, and rollback in `webui-secret-editor`.
3. **Structured and key-value secret types** (modifies `secret-management`, which currently defines four types). Blocks the structured and key-value forms in `webui-secret-editor`.
4. **Bulk import and encrypted tenant backup** (new capability, for example `secret-transfer`). Bulk import must be atomic and offer a dry run that returns per-row errors. The backup must be encrypted with a key derived from an administrator passphrase. Blocks the import and backup parts of `webui-secret-transfer`. JSON and CSV export can use the existing secret listing and value endpoints.
5. **Tenant member and policy management** (new capability, for example `tenant-administration`). Member lifecycle, role assignment, and tenant-wide security policies such as idle timeout, refresh token lifetime, and clipboard clear delay. Policy changes are audited. Blocks `webui-tenant-administration`.
6. **Read endpoints for access control** (modifies `access-control`). The server has only mutation endpoints today. Endpoints must list groups, group members, and folder grants with their principal kind, must report each folder's inheritance status, and must report the caller's effective permissions on a folder so the UI can show only permitted actions. Blocks `webui-access-control-management` and the group views in `webui-tenant-administration`.
7. **Global search** (modifies `secret-management`). Listing is folder-scoped today. A search endpoint over folder and secret names, limited to what the caller can read, is needed for the command palette. Blocks full-scope search in `webui-shell-navigation`.
8. **Audit log for the viewer** (modifies `security-audit`). Entries must expose their hash and previous hash, and the query must support pagination by sequence. Today the API returns no per-entry hash and no pagination. Blocks per-entry integrity indicators in `webui-audit-log-viewer`. Whole-log verification already exists.

## Capabilities

### New Capabilities

- `webui-platform`: The Angular and Tailwind stack, the Angular CDK usage rules, dark mode, responsive breakpoints, and the accessibility baseline for the whole application.
- `webui-shell-navigation`: The persistent responsive sidebar, the route map (including `/secrets/new` and `/secrets/:id/edit`), and the global command palette.
- `webui-secret-browser`: The folder tree, the dual-mode table and card views with persisted preference, and one-click credential copy with clipboard cleanup.
- `webui-secret-editor`: Create and edit forms for each secret type, file-based certificate handling, version history with diff, and rollback.
- `webui-secret-transfer`: JSON and CSV import and export, and encrypted tenant backup export.
- `webui-session-security`: In-memory access token, refresh token handling through the server, and the idle timeout with a countdown modal.
- `webui-access-control-management`: The folder ACL view, inheritance status, group and user access levels, and break and restore inheritance.
- `webui-audit-log-viewer`: Multi-criteria audit filtering and hash chain integrity indicators.
- `webui-tenant-administration`: Member, group, and tenant security policy management for tenant administrators.

### Modified Capabilities

None. This change adds the web UI component and does not change any existing server requirement. The server prerequisites above are separate changes and will modify the capabilities they name.

## Out of Scope

- Backend server implementation, including the server prerequisites listed above. Those need their own OpenSpec changes.
- Desktop client (.NET 10) implementation.
- Production CI/CD build scripts and containerization setup.
- Mobile native applications. The web UI is responsive; it is not a native app.
- Changes to hosting, deployment, or key-encryption-key provisioning.

## Impact

- **Components**: Web UI (Angular) only, new application at `src/Custode.WebUi/`. The server and desktop client are not changed by this change. The server prerequisites are tracked as separate changes that must be applied first.
- **Code**: A new Angular workspace with Tailwind CSS, Angular CDK, and feature modules for each capability. Tests are required for each capability.
- **APIs**: The web UI depends on the server contracts described in `design.md`. Where a contract is missing, the dependent tasks are blocked until the prerequisite change is applied.
- **Dependencies**: Angular, Tailwind CSS, and Angular CDK. Versions and APIs must be verified against the official documentation (Context7) before implementation, as required by `openspec/config.yaml`.
- **Security**: Security-critical. Access tokens are never persisted to browser storage. Secret values are shown only after an explicit reveal or copy action, and are cleared from memory and from the clipboard on timeout. Audit and ACL views are visible only to tenant administrators, and the UI must not be the only enforcement point.
