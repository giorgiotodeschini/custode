# Spec Delta

## Purpose
Defines how the web UI holds the access token only in memory, renews the session through a refresh token that script cannot read, and ends idle sessions with a visible countdown so that a left-open browser does not expose tenant secrets.

## ADDED Requirements

### Requirement: Sign-in shows a generic error for failed credentials
The sign-in form SHALL send the login identifier and password to the server. A failed attempt SHALL show the same generic message whether the login identifier is unknown or the password is wrong. The password MUST NOT be stored or echoed in the page after submission.

#### Scenario: Wrong password
- **WHEN** a user submits an incorrect password
- **THEN** the message "Invalid login or password" is shown

#### Scenario: Unknown login
- **WHEN** a user submits an unknown login identifier
- **THEN** the same message "Invalid login or password" is shown

### Requirement: The access token is held in memory only
The access token SHALL be kept only in application memory. It MUST NOT be written to local storage, session storage, or any script-readable cookie. After a page reload, the application SHALL obtain a new access token through the refresh mechanism when a valid session exists.

#### Scenario: Token not persisted
- **WHEN** a user signs in and the browser storage is inspected
- **THEN** no access token is present in local storage, session storage, or script-readable cookies

#### Scenario: Reload with a valid session
- **WHEN** the user reloads the page and the refresh token is still valid
- **THEN** the application renews the session without showing the sign-in form

### Requirement: The refresh token is never readable by script
The refresh token SHALL be delivered and stored only as an HTTP-only, Secure, same-site cookie. The web UI MUST NOT read, store, or log the refresh token.

#### Scenario: Refresh token not visible to script
- **WHEN** the application runs in the browser
- **THEN** script cannot read the refresh token value

### Requirement: Refresh token rotation ends reused sessions
Each renewal SHALL replace the refresh token. When the application presents a refresh token that the server has already replaced, the session SHALL end and the user SHALL be asked to sign in again.

#### Scenario: Renewal rotates the token
- **WHEN** the application renews the session
- **THEN** the server returns a new refresh token and the previous one is no longer accepted

#### Scenario: Replayed refresh token
- **WHEN** the server rejects a previously replaced refresh token
- **THEN** the session ends and the sign-in form is shown with a notice that the session has ended

### Requirement: Renewal happens before expiry and is shared
The application SHALL renew the access token before it expires and SHALL retry a request once after a renewal when the server responds unauthorized. Concurrent requests that need renewal SHALL share a single renewal request.

#### Scenario: Renewal before expiry
- **WHEN** the access token reaches 80 percent of its lifetime
- **THEN** the application renews the session in the background

#### Scenario: Concurrent requests
- **WHEN** several requests are sent while the access token has expired
- **THEN** one renewal request is sent and all requests use its result

### Requirement: Idle sessions show a countdown before they end
The application SHALL count user inactivity from the last keyboard, pointer, or touch interaction. Inactivity is measured against the clock, so time spent in a background tab counts as inactive. The default idle timeout is 15 minutes, and the timeout MUST be the value set by tenant policy when available. Sixty seconds before the timeout, a modal SHALL show a countdown until the session ends.

#### Scenario: Countdown displayed
- **WHEN** the user has been inactive for 14 minutes of a 15-minute timeout
- **THEN** a modal opens showing a countdown from 60 seconds

#### Scenario: Stay signed in
- **WHEN** the user activates "Stay signed in" in the countdown modal
- **THEN** an activity report is sent to the server, the modal closes, and the idle timer restarts

#### Scenario: Countdown reaches zero
- **WHEN** the countdown reaches zero without interaction
- **THEN** the session ends and the sign-in form is shown with a notice that the session timed out

#### Scenario: Background tab counted
- **WHEN** the user switches away and returns after the idle timeout has passed
- **THEN** the session has ended and the sign-in form is shown

### Requirement: The countdown modal is accessible and does not block focus
The idle countdown SHALL be a modal alert dialog. Keyboard focus SHALL move into the dialog when it opens, and the remaining time SHALL be announced at least once every 10 seconds through a live region.

#### Scenario: Dialog opened
- **WHEN** the countdown modal opens
- **THEN** focus moves to the "Stay signed in" button and the dialog is announced as an alert

### Requirement: Ending a session clears sensitive client state
When a session ends by sign-out, idle timeout, or refresh failure, the application SHALL clear the in-memory access token, any decrypted secret data in memory, the open detail panels, and any clipboard content copied by the application. The clipboard MUST be cleared only if it still holds a value copied by the application.

#### Scenario: Sign-out with a copied credential
- **WHEN** the user signs out while a copied credential is still on the clipboard
- **THEN** the clipboard is cleared

#### Scenario: Idle timeout clears screens
- **WHEN** the idle timeout ends the session
- **THEN** no secret data remains in the page state

### Requirement: Sign-out revokes the refresh token
Signing out SHALL ask the server to revoke the refresh token and SHALL return the user to the sign-in form.

#### Scenario: Explicit sign-out
- **WHEN** the user selects sign-out
- **THEN** the refresh token is revoked on the server and the sign-in form is displayed

### Requirement: Session end is shared across browser tabs
When a session ends in one tab of the application, every other open tab of the application for the same browser profile SHALL also end its session and show the sign-in form.

#### Scenario: Sign-out in one tab
- **WHEN** the user signs out in one tab while a second tab is open
- **THEN** the second tab shows the sign-in form

### Requirement: Return URL is restricted to the application
After a session ends and the user signs in again, the application SHALL return the user to the page they were on only when that address is a relative path within the application. Any other return address MUST be ignored.

#### Scenario: Return to a folder
- **WHEN** a session ends on `/folders/production` and the user signs in again
- **THEN** the user is returned to `/folders/production`

#### Scenario: External return address
- **WHEN** the return address points to another site
- **THEN** the address is ignored and the user is shown the folders screen

### Requirement: User interaction keeps the server session alive
While the user interacts with the application, the application SHALL report that activity to the server through `POST /api/auth/activity`, at most once every 60 seconds. Keyboard, pointer, and touch interactions and the activation of "Stay signed in" are reported. The application MUST NOT send activity reports while the user is idle, and a background token renewal MUST NOT be treated as activity.

#### Scenario: Continuous interaction is reported at a bounded rate
- **WHEN** the user types continuously in a form for three minutes
- **THEN** at most one activity report is sent per minute, and the session stays active

#### Scenario: Idle user sends no report
- **WHEN** the user performs no interaction for five minutes
- **THEN** no activity report is sent

#### Scenario: Long form edit survives the idle timeout
- **WHEN** the user edits a secret form for twenty minutes with interactions throughout and then saves
- **THEN** the save is accepted and the session is not ended by the server

#### Scenario: Refused activity report ends the session
- **WHEN** an activity report is refused as unauthorized
- **THEN** the session end path runs and the sign-in form is shown with a notice that the session has ended

