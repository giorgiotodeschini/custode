# session-management Specification

## Purpose
Defines how a signed-in user keeps a session through refresh tokens that scripts cannot read, how a session ends by sign-out, idle timeout, absolute lifetime, or account disablement, and how every authenticated request is checked against the current state of its session.

## Requirements

### Requirement: Sign-in starts a refresh session
A successful sign-in SHALL create a session for the user. The refresh token SHALL be delivered only as an HTTP-only, Secure, same-site cookie named `custode_refresh` with the path `/api/auth`. The sign-in response body MUST NOT contain the refresh token. The access token SHALL carry the identifier of its session.

#### Scenario: Refresh cookie set on sign-in
- **WHEN** a user signs in with valid credentials
- **THEN** the response sets the `custode_refresh` cookie as HTTP-only, Secure, same-site, with the path `/api/auth`

#### Scenario: Refresh token absent from the body
- **WHEN** a user signs in with valid credentials
- **THEN** the response body contains the access token and its expiry, and no refresh token

#### Scenario: Session identifier in the access token
- **WHEN** a user signs in with valid credentials
- **THEN** the access token contains a session identifier that names the new session

### Requirement: Refresh rotates the refresh token
The system SHALL provide `POST /api/auth/refresh`. A request SHALL be accepted only when it carries the refresh cookie and the request header `X-Custode-Request`. A valid request SHALL replace the refresh token with a new one, issue a new access token, and return its expiry. The replaced refresh token MUST NOT be accepted again.

#### Scenario: Valid refresh
- **WHEN** a request carries a valid refresh cookie and the `X-Custode-Request` header
- **THEN** a new access token and a replacement refresh cookie are returned, and the previous refresh token is no longer valid

#### Scenario: Missing request header
- **WHEN** a refresh request does not carry the `X-Custode-Request` header
- **THEN** the request is rejected as forbidden

#### Scenario: Missing refresh cookie
- **WHEN** a refresh request carries no refresh cookie
- **THEN** the request is rejected as unauthorized

### Requirement: Reuse of a rotated refresh token ends the session
A refresh token that has already been replaced is a reused token. When a reused token is presented after the reuse grace window, the system SHALL revoke the whole session, record an audit entry with the action "refresh token reuse detected" and the outcome "denied", and reject the request as unauthorized. The grace window is 10 seconds by default and is configured by the operator. A reused token presented within the grace window SHALL be rejected as unauthorized without revoking the session.

#### Scenario: Reuse after the grace window
- **WHEN** a replaced refresh token is presented 30 seconds after its replacement
- **THEN** the session is revoked, the audit entry "refresh token reuse detected" is recorded with the outcome "denied", and the request is rejected as unauthorized

#### Scenario: Reuse within the grace window
- **WHEN** a replaced refresh token is presented within the grace window
- **THEN** the request is rejected as unauthorized and the session remains active

### Requirement: Absolute session lifetime follows the tenant policy
A session SHALL end when its absolute lifetime, measured from the moment the session started, is exceeded. The absolute lifetime SHALL be the tenant's refresh token lifetime in effect at the time of each refresh. An access token SHALL expire after the tenant's access token lifetime.

#### Scenario: Session passes its absolute lifetime
- **WHEN** a refresh is requested after the absolute lifetime of the session has passed
- **THEN** the request is rejected as unauthorized and the session is ended

#### Scenario: Shortened policy applies at the next refresh
- **WHEN** a tenant administrator shortens the refresh token lifetime and an existing session refreshes
- **THEN** the session uses the shortened lifetime measured from its start

### Requirement: Idle sessions end after the tenant idle timeout
A session SHALL end when no authenticated activity has been recorded for longer than the tenant's idle timeout. Authenticated activity is a successful authenticated request or a call to `POST /api/auth/activity`. A refresh request MUST NOT count as activity. Activity SHALL be recorded at most once per minute for each session.

#### Scenario: Refresh does not extend idleness
- **WHEN** a session has no authenticated activity for longer than the idle timeout and a refresh is requested
- **THEN** the refresh is rejected as unauthorized and the session is ended

#### Scenario: Activity extends the session
- **WHEN** a client calls `POST /api/auth/activity` before the idle timeout
- **THEN** the session stays active for another idle timeout period

#### Scenario: Idle request rejected
- **WHEN** an access token whose session is idle beyond the timeout is used on an endpoint
- **THEN** the request is rejected as unauthorized

### Requirement: Sign-out revokes the session
The system SHALL provide `POST /api/auth/sign-out`. The request SHALL revoke the session named by the refresh cookie and by the access token, clear the refresh cookie, and record an audit entry with the action "session revoked". The request SHALL carry the `X-Custode-Request` header. After sign-out, the access token of that session MUST be rejected on the next request.

#### Scenario: Sign-out
- **WHEN** a signed-in user calls the sign-out endpoint
- **THEN** the session is revoked, the refresh cookie is cleared, and the audit entry "session revoked" is recorded

#### Scenario: Access token after sign-out
- **WHEN** an access token of a signed-out session is presented before its expiry
- **THEN** the request is rejected as unauthorized

### Requirement: Every authenticated request checks its session
A request authenticated by an access token SHALL be accepted only when the token's session identifier names an active session of the token's tenant that is not revoked, not idle beyond the timeout, and not past its absolute lifetime, and when the session's user account is enabled. A token without a session identifier MUST be rejected as unauthorized. This check SHALL be separate from signature and expiry validation.

#### Scenario: Revoked session before token expiry
- **WHEN** a session is revoked and its access token is still within its expiry
- **THEN** the next request with that token is rejected as unauthorized

#### Scenario: Token without session identifier
- **WHEN** a token is valid in signature and expiry but has no session identifier
- **THEN** the request is rejected as unauthorized

### Requirement: Disabled accounts lose access at once
When a member account is disabled, every session of that account SHALL be revoked. No request by that account SHALL be accepted afterwards, including requests with an unexpired access token. Sign-in and refresh for a disabled account SHALL fail with the same generic response as for an unknown login.

#### Scenario: Disabled account with an unexpired token
- **WHEN** an administrator disables a member who holds an unexpired access token and calls an endpoint
- **THEN** the request is rejected as unauthorized

#### Scenario: Sign-in of a disabled account
- **WHEN** a disabled member submits correct credentials
- **THEN** the response is the same generic failure as for an unknown login

### Requirement: Password change ends the user's other sessions
When a user changes their password, every other session of that user SHALL be revoked. Each revoked session SHALL be recorded in the audit log with the action "session revoked". The session that performed the change SHALL remain active.

#### Scenario: Other sessions ended by a password change
- **WHEN** a user with two active sessions changes their password from one of them
- **THEN** the other session is revoked and the session that changed the password remains active
