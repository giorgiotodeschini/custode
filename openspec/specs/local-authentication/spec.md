# local-authentication Specification

## Purpose
Defines how users of a Custode instance are identified with local credentials and how authenticated sessions are represented by signed tokens, leaving room for external identity providers in later phases.

## Requirements

### Requirement: Users authenticate with local credentials
The system SHALL allow users to authenticate with a unique login identifier and a password. Passwords MUST NOT be stored or returned in plaintext.

#### Scenario: Valid credentials
- **WHEN** a user submits a correct login identifier and password
- **THEN** the system authenticates the user and issues a token

#### Scenario: Invalid credentials
- **WHEN** a user submits an incorrect password or an unknown login identifier
- **THEN** the system rejects the attempt with the same generic error for both cases

### Requirement: Tokens are issued as signed JWTs
On successful authentication, the system SHALL issue a signed JSON Web Token that identifies the user, their tenant, their roles, and the session it belongs to, and that has an expiration time equal to the tenant's access token lifetime.

#### Scenario: Token issued on sign-in
- **WHEN** a user successfully authenticates
- **THEN** the response contains a signed token with the user's identity, tenant, roles, session identifier, and an expiration time

#### Scenario: Expired token
- **WHEN** a request presents a token past its expiration time
- **THEN** the request is rejected as unauthorized

#### Scenario: Token with invalid signature
- **WHEN** a request presents a token whose signature does not validate
- **THEN** the request is rejected as unauthorized

### Requirement: Protected endpoints require a valid token
Every endpoint that is not explicitly marked as anonymous SHALL require a valid token. The system MUST reject requests without a token.

#### Scenario: Request without token to protected endpoint
- **WHEN** a request without a token is made to a protected endpoint
- **THEN** the request is rejected as unauthorized

### Requirement: Credential storage uses the identity framework
User accounts, password hashes, and roles SHALL be managed through ASP.NET Core Identity. Password hashing MUST use the identity framework's default hashing.

#### Scenario: Account is created
- **WHEN** a new user account is created
- **THEN** its password is stored only as a hash produced by the identity framework

### Requirement: Authentication is extensible to external providers
The authentication design SHALL keep token validation separate from credential verification, so that additional identity providers, including per-tenant OIDC, can be added without changing how tokens are consumed by slices.

#### Scenario: Token consumed by a slice
- **WHEN** a slice reads the identity of the caller
- **THEN** it obtains the same identity representation regardless of how the token was issued

### Requirement: Initial passwords require a change at first sign-in
An account flagged as requiring a password change SHALL be able to sign in, but the issued access token SHALL be restricted. Every endpoint except the password change endpoint and the sign-out endpoint SHALL reject a restricted token as forbidden, with the reason "password change required". The restriction SHALL end when the password is changed.

#### Scenario: Flagged account signs in
- **WHEN** a member flagged for a password change signs in with the correct password
- **THEN** the access token is issued with the restriction

#### Scenario: Restricted token used on another endpoint
- **WHEN** a restricted token is presented to a listing endpoint
- **THEN** the request is rejected as forbidden with the reason "password change required"

#### Scenario: Restriction removed after change
- **WHEN** the user changes the password and signs in again
- **THEN** the new access token is not restricted

### Requirement: Users change their own password
The system SHALL provide `POST /api/auth/change-password`, which verifies the current password and stores a new password that meets the identity password policy, only as a hash produced by the identity framework.

#### Scenario: Password changed
- **WHEN** a signed-in user submits the correct current password and a new password that meets the policy
- **THEN** the password is replaced, the change requirement is cleared, and the audit entry "password changed" is recorded

#### Scenario: Wrong current password
- **WHEN** a signed-in user submits an incorrect current password
- **THEN** the request is rejected as bad request and the password is unchanged

#### Scenario: New password below the policy
- **WHEN** a signed-in user submits a new password that does not meet the identity password policy
- **THEN** the request is rejected as bad request with the policy failures and the password is unchanged
