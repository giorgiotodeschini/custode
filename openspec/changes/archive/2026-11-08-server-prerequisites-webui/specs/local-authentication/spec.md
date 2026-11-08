# Spec Delta

## MODIFIED Requirements

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

## ADDED Requirements

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
