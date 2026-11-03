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
On successful authentication, the system SHALL issue a signed JSON Web Token that identifies the user, their tenant, and their roles, and that has a defined expiration time.

#### Scenario: Token issued on sign-in
- **WHEN** a user successfully authenticates
- **THEN** the response contains a signed token with the user's identity, tenant, roles, and an expiration time

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
