# Design

## Context

The server is implemented and tested for tenant isolation, local sign-in issuing a JWT access token, folder and secret endpoints, group and grant management, and the audit log with its verification endpoint. See `openspec/specs/` for the current behavior and `proposal.md` for the motivation and the list of server prerequisites.

Today the server returns only an access token at sign-in, has no refresh endpoint, exposes no secret version history, and offers no import, export, or member management. The web UI requirements in `specs/` depend on these contracts. This design fixes the UI architecture now and marks each dependent task as blocked until its server prerequisite is applied. Framework APIs were checked against Context7: Angular functional HTTP interceptors (`provideHttpClient` with `withInterceptors`), the CDK `Clipboard` service (`copy`, `beginCopy`), CDK Dialog focus restoration, the CDK `TreeKeyManager`, and Tailwind v4's `@custom-variant dark` for class-based dark mode.

## Goals / Non-Goals

**Goals:**
- Keep the access token in memory and the refresh token out of script reach, and make session renewal and idle expiry predictable.
- Keep the browser from holding more secret plaintext than the user asked for, and clear what it holds on every session end.
- Build each capability as a self-contained feature folder that depends only on shared UI primitives and a single API client.
- Use the CDK for every primitive named in the proposal, rather than reimplementing focus, clipboard, tree, overlay, or breakpoint behavior.

**Non-Goals:**
- Server-side rendering, offline mode, native mobile packaging, or a service worker cache of tenant data.
- Client-side cryptography. The browser never computes audit hashes or encrypts backups; the server does both.
- Localization. The first release is English only.
- Deployment, containerization, and CI/CD (see proposal Out of Scope).

## Decisions

### D1. Application structure and location

The Angular application lives in `src/Custode.WebUi/` as a standalone-component workspace. Code is organized by capability: `core/` (API client, session, preferences, guards), `shell/` (sidebar, palette, layout), `shared/ui/` (buttons, badges, confirmation dialogs, empty states, tables and cards), and one folder per capability (`features/secret-browser/`, `features/secret-editor/`, `features/secret-transfer/`, `features/access-control/`, `features/audit/`, `features/tenant-admin/`). State is held in signal-based services owned by a feature. There is no global store, so a feature cannot read another feature's internal state.
- Rationale: mirrors the server's vertical slices and keeps the spec-to-code mapping one-to-one.
- Alternative: a shared state store (NgRx). Rejected: adds a second place where secret data could be retained, and the features share only the session and the API client.

### D2. Session model: memory access token, cookie refresh token

- The access token is held in a `SessionService` signal. It is attached to API requests by a functional HTTP interceptor (`withInterceptors`). It is never written to storage.
- The refresh token is set by the server as an HTTP-only, Secure, `SameSite=Strict` cookie. The SPA calls the refresh endpoint with `withCredentials` and no script access to the value.
- The refresh endpoint is `POST` only and requires a custom request header, so that a cross-site form cannot trigger a renewal (the cookie is also `SameSite=Strict`).
- Renewal runs at 80 percent of the access token lifetime and once after a 401. Concurrent renewals share one in-flight observable, so only one refresh request is sent.
- Reload restores a session by calling the refresh endpoint. The access token's `role` and `tenant_id` claims are read from memory to choose navigation and guards. These claims only shape the UI; every decision is still made by the server.
- Alternative: keep the refresh token in `localStorage`. Rejected: any script injection could read it and mint sessions indefinitely.
- Requires server prerequisite 1.

Assumption: the SPA and the API are served from one origin through a reverse proxy in development (`proxy.conf.json`) and in production (deployment is out of scope). This keeps the cookie and CORS behavior simple.

### D3. Idle timeout and multi-tab behavior

- Activity is recorded from `pointerdown`, `keydown`, and `touchstart` events, throttled to one update per second. The idle deadline is stored as a timestamp, not a counter, so a suspended or background tab recomputes the remaining time from the clock. This matches the spec's background-tab scenario.
- A countdown `Dialog` (CDK) is opened 60 seconds before the deadline with `disableClose` set, `role="alertdialog"`, and focus on the "Stay signed in" button. The remaining seconds are announced through `LiveAnnouncer` every 10 seconds.
- Session end (sign-out, idle, or failed refresh) is broadcast over `BroadcastChannel` so that other tabs end their sessions too. A tab that receives the message clears its memory state and shows the sign-in form.
- The idle timeout and clipboard clear delay come from tenant policy when the server provides it (server prerequisite 5). Otherwise the UI uses 15 minutes and 30 seconds.
- Server-side idle tracking is fed by the same interactions. While the user interacts, the UI calls `POST /api/auth/activity` at most once every 60 seconds, and also when "Stay signed in" is activated. Without these calls the server would end a session that the user is still working in (for example during a twenty-minute secret edit), and the save would then fail. Idle periods send nothing, and a background refresh is not activity. A refused activity call runs the session end path (spec `webui-session-security`, "User interaction keeps the server session alive").

### D4. Browser preferences

Only three preferences are stored in `localStorage`: theme, secret presentation (table or cards), and sidebar state. The presentation key includes the user's `sub` claim, so two accounts on one browser do not share a choice. Preferences contain no tenant data and no token. The theme is applied before the first paint by a small inline script in `index.html` that reads the stored choice or the `prefers-color-scheme` media query. This avoids a flash of the wrong theme.

### D5. Dark mode with Tailwind

Dark mode uses Tailwind v4's class-based variant, `@custom-variant dark (&:where(.dark, .dark *))`, so that the `dark` class on `<html>` controls all `dark:` utilities. When the user has not chosen a theme, the class follows `prefers-color-scheme`. Colors are defined once as theme variables in the Tailwind CSS entry file, which is how the "design tokens" requirement in `webui-platform` is met.
- Alternative: rely on the default `prefers-color-scheme` variant. Rejected: it cannot honor an explicit user choice without a second mechanism.

### D6. CDK usage map

| Proposal primitive | Used for |
|---|---|
| Clipboard | Copy of credential values. Pre-loaded through `beginCopy`, with retry, and the `PendingCopy` object is always destroyed. |
| Tree | Folder tree in `secret-browser` and the inheritance tree in `access-control`. Keyboard navigation through the CDK tree key manager. |
| Overlay and Dialog | Command palette (Overlay), secret detail panel (Overlay), confirmations, the idle countdown, and the backup passphrase dialog (Dialog). Focus is restored to the trigger on close. |
| A11y | `LiveAnnouncer` for copy, timeout, and conflict messages; `FocusTrap` where a panel is not a Dialog. |
| Layout | `BreakpointObserver` decides between the persistent sidebar and the drawer, and between table and card defaults. |

### D7. Clipboard lifecycle

A copy is performed in four steps: fetch the value from the server, place it on the clipboard through the CDK clipboard service, start a clear timer, and drop the value from component state. Each copy increments a generation counter. The clear timer runs only if its generation is still the latest, so a newer copy is never cleared by an older timer. The timer is computed from the copy timestamp, so a throttled background tab still clears on time when it resumes.
- Risk: browsers may refuse clipboard writes without a user gesture or focus, and clearing with an empty string may be refused in some browsers. → Mitigation: copy only from a click handler, and report a failed clear to the user with the message "Could not clear the clipboard". Implementation must test the clear path in each supported browser before tasks are closed.

### D8. Secret value handling in memory

- Listings and the detail panel hold metadata only. A value is fetched on demand for a copy or for a reveal, and it is held in a local variable, not in a shared signal.
- Editing forms hold values until the form is saved, cancelled, or left. Leaving the route clears the form state, and the session end clears all of it (spec `webui-session-security`).
- Form fields holding values use `type="password"` (with a reveal control) and `autocomplete="off"`, so that browsers do not offer them as autofill or suggestions.
- The web UI never logs secret values, and its error messages use names and identifiers only.

### D9. Version history, diff, and rollback

Version history is read from the server's version endpoints (prerequisite 2). The diff is computed in the browser on decrypted field values for the two selected versions. Only the result (unchanged, changed, added, removed) is rendered by default; values are rendered only after the explicit "reveal values" action. No diff library is required: structured and key-value secrets are flat field maps, and file-based secrets compare name and size, plus a changed flag from the server.
- Rollback calls a server endpoint that creates a new version from the selected one. The UI never rewrites history.
- Saves send the version token from the loaded secret (`If-Match` in the request, prerequisite 2). A stale token returns `412 Precondition Failed` (not `409`: the status code for a failed `If-Match` condition), and the form shows the conflict dialog.

### D10. Audit log viewer

Pagination is cursor-based on the sequence number (`afterSequence`), because offset paging is unstable on an append-only log. Filters are kept in the route query so that a filtered view can be shared, and every filter change resets the cursor.
- Chain link status is checked in the browser by comparing each entry's `previousHash` with the `hash` of the preceding loaded entry. The browser does not recompute SHA-256 over canonical fields; that is the server's verification endpoint (already implemented). The browser therefore reports "Link verified" or "Link broken" for loaded neighbors and shows full-log integrity only from the server's verify result.
- Requires prerequisite 8 (hash fields and cursor paging).

### D11. Access control and tenant administration are server-confirmed

Security changes (grants, grant revocation, inheritance, group membership, policies, roles) are never optimistic. The UI sends the request, waits for the server, and then reloads the affected view. Break and restore previews are computed from the ACL view data: the rows marked as inherited show what would be lost or regained. The server still decides, and the preview is refreshed on conflict.

### D12. Transfer: validation parity and server-side work

- CSV and JSON are parsed in the browser. Row validation reuses the same rule set as the create form, so the preview is immediate. The server's dry-run response (prerequisite 4) is authoritative for the preview's final counts and errors, and the confirmation is sent only when the dry run has no errors.
- The encrypted tenant backup is produced by the server and encrypted with a key derived from the passphrase. The passphrase is sent once over TLS in the export request and is never stored or logged. The alternative, encrypting in the browser, would require the browser to hold every decrypted value of the tenant. That is rejected for the same reason as D8.
- CSV export neutralizes cells starting with `=`, `+`, `-`, `@`, tab, or carriage return by prefixing an apostrophe.

### D13. Route guards and the server as authority

`authGuard` redirects to sign-in with a same-origin return path. `adminGuard` checks the `role` claim in memory and redirects to folders with a notice. The server enforces every rule again; a guard is a convenience, and a manipulated client gains nothing from it. Forbidden responses from the server are shown as "not available" for admin pages and as the spec's messages elsewhere.

### D14. Behavior of blocked features

A capability whose server prerequisite is not yet applied is not shipped as a placeholder screen. Its routes are not registered until its prerequisite is applied. This keeps the sidebar accurate and prevents users from seeing controls that always fail.

## Dependency order

Server prerequisites come first, as required by `openspec/config.yaml`. The web UI tasks are grouped by prerequisite:

| Web UI capability | Blocked by server prerequisite |
|---|---|
| `webui-platform`, `webui-shell-navigation` (navigation, routes, and quick actions), `webui-secret-browser` (tree, listing, copy and clipboard), `webui-session-security` (sign-in and in-memory token) | none. Until prerequisite 1 is applied, a page reload requires signing in again, and the idle countdown uses the server policy defaults |
| `webui-secret-browser` (effective permissions), `webui-secret-editor` (create folder selector) | 6 |
| `webui-shell-navigation` (palette search over secrets) | 7 |
| `webui-session-security` (refresh, rotation, revoke) | 1 |
| `webui-secret-editor` (version history, diff, rollback, conflicts, structured and key-value) | 2, 3 |
| `webui-secret-transfer` (import and backup only; JSON and CSV export use existing endpoints) | 4 |
| `webui-access-control-management` | 6 |
| `webui-audit-log-viewer` (chain link status, paging) | 8 |
| `webui-tenant-administration` | 5, and 6 for group views |

## Risks / Trade-offs

- **[Clipboard clear is refused by the browser]** → Clear from the same user gesture where possible, report failure, and test each supported browser. The spec's "newer copy preserved" rule keeps the timer safe even if the clear fails.
- **[Refresh cookie requires a single origin]** → Development proxy and single-origin deployment. CORS is avoided. If the origins must differ, the cookie `SameSite` attribute and CORS credential rules need a new design decision.
- **[Ctrl+K conflicts with the browser address bar on some platforms]** → Handle the shortcut with `preventDefault` only while the app has focus. The visible search control remains the fallback.
- **[Secret values remain in browser memory until garbage collection]** → Values are dropped from component state on use and cleared on session end. The browser cannot guarantee memory wiping; this is accepted as a limit of any web client.
- **[Plaintext JSON export]** → Requires explicit confirmation. Each value read is already audited by the server, so every export leaves a trail.
- **[Lost backup passphrase]** → Warning before export, and no recovery path, as specified.
- **[Blocked tasks stall the release]** → Server prerequisites are scoped as separate changes so that the core shell and secret browsing can ship earlier.
- **[Client-side permission checks drift from server rules]** → The UI never widens access. A mismatch can only hide an action the server would allow, and the server remains the single source for every refusal.

## Migration Plan

This change adds a new application and writes no data. There is nothing to migrate.

Rollout order:
1. Apply the server prerequisites in their own changes, in the order listed above.
2. Build the shell and the core secret browsing features that need no new server contract (see the dependency table).
3. Add each blocked feature when its prerequisite is applied.

Rollback: because the web UI is a new client, rolling it back means removing its deployment. The server is unaffected. Each feature's routes are registered only when its prerequisite is present, so a partial rollback removes routes but leaves the shell working.

## Open Questions

- Whether tenant policy values should be shown to non-administrators as read-only (for example, the idle timeout that applies to them). This does not change any spec requirement and can be decided when prerequisite 5 is written.
- Whether the backup passphrase should be generated by the application as an option. This is a usability choice that can be added later without changing the spec.
