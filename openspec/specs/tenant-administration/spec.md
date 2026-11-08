# tenant-administration Specification

## Purpose
Defines how tenant administrators manage the members of their tenant, including their lifecycle, the tenant administrator role, and the tenant-wide security policies that control sessions and token lifetimes, so that the rules governing a tenant can be maintained through the API.

## Requirements

### Requirement: Members are listed with their status
The system SHALL provide `GET /api/tenant/members`, available to tenant administrators. The response SHALL list every member of the caller's tenant with its identifier, login, roles, number of groups, enabled state, and whether a password change is required. The response MUST NOT contain password data.

#### Scenario: Members listed
- **WHEN** a tenant administrator lists the members of the tenant
- **THEN** each member is listed with the fields above and no member of another tenant is listed

### Requirement: Members are created with an initial password
A tenant administrator SHALL create a member through `POST /api/tenant/members` with a login and an initial password. The member joins the administrator's tenant, which the request cannot choose. The new account is enabled, has no roles, and must change its password at first sign-in.

#### Scenario: Member created
- **WHEN** a tenant administrator creates a member with an unused login and a password that meets the policy
- **THEN** the member is created in the administrator's tenant, enabled, flagged for password change, and the audit entry "member created" is recorded

#### Scenario: Initial password never returned or logged
- **WHEN** a member is created
- **THEN** neither the response nor any log entry contains the initial password

#### Scenario: Login already in use
- **WHEN** a tenant administrator creates a member with a login that exists
- **THEN** the request is rejected as conflict and no member is created

#### Scenario: Password below the policy
- **WHEN** a tenant administrator creates a member with a password that does not meet the identity password policy
- **THEN** the request is rejected as bad request with the policy failures and no member is created

### Requirement: Members can be disabled and enabled
A tenant administrator SHALL disable and re-enable a member of the tenant through `POST /api/tenant/members/{id}/disable` and `POST /api/tenant/members/{id}/enable`. Disabling revokes every session of the member.

#### Scenario: Member disabled
- **WHEN** a tenant administrator disables a member who has two active sessions
- **THEN** the member is disabled, both sessions are revoked, and the audit entry "member disabled" is recorded

#### Scenario: Member re-enabled
- **WHEN** a tenant administrator enables a disabled member
- **THEN** the member can sign in again and the audit entry "member enabled" is recorded

#### Scenario: Last enabled administrator
- **WHEN** a tenant administrator disables the only enabled tenant administrator of the tenant
- **THEN** the request is rejected as conflict and the member stays enabled

#### Scenario: Member of another tenant
- **WHEN** a tenant administrator disables a user identifier that belongs to another tenant
- **THEN** the response is not found and the other tenant is unchanged

### Requirement: The tenant administrator role can be granted and withdrawn
A tenant administrator SHALL grant or withdraw the tenant administrator role of a member through `PUT /api/tenant/members/{id}/administrator` with a field `isAdministrator`. Withdrawing the role revokes the member's sessions.

#### Scenario: Role granted
- **WHEN** a tenant administrator grants the role to an enabled member
- **THEN** the member has the tenant administrator role at its next sign-in and the audit entry is recorded

#### Scenario: Role withdrawn with another administrator present
- **WHEN** a tenant administrator withdraws the role from another administrator while at least one other enabled administrator exists
- **THEN** the role is removed, the member's sessions are revoked, and the audit entry is recorded

#### Scenario: Last administrator protected
- **WHEN** the role is withdrawn from the only enabled tenant administrator
- **THEN** the request is rejected as conflict and the role is kept

### Requirement: Tenant security policies are validated and tenant-wide
The tenant SHALL have four policy values, each with a range and a default: `idleTimeoutMinutes` (5 to 120, default 15), `accessTokenLifetimeMinutes` (5 to 60, default 15), `refreshTokenLifetimeDays` (1 to 30, default 7), and `clipboardClearSeconds` (10 to 120, default 30). A tenant administrator SHALL replace them through `PUT /api/tenant/policies`, and a request with any value out of range SHALL be rejected without saving anything.

#### Scenario: Access token lifetime out of range
- **WHEN** a tenant administrator saves an access token lifetime of 61 minutes
- **THEN** the request is rejected as bad request naming `accessTokenLifetimeMinutes` and nothing is saved

#### Scenario: Valid policy saved
- **WHEN** a tenant administrator saves an idle timeout of 20 minutes and the other values are within range
- **THEN** the policy is saved and the audit entry "tenant policy changed" is recorded

#### Scenario: Value out of range
- **WHEN** a tenant administrator saves an idle timeout of 2 minutes
- **THEN** the request is rejected as bad request naming `idleTimeoutMinutes` and nothing is saved

#### Scenario: Defaults for a new tenant
- **WHEN** a tenant has never saved a policy
- **THEN** the policy reads return the default values

### Requirement: Any member can read the effective policy
Every authenticated member of a tenant SHALL be able to read the tenant's policy values through `GET /api/tenant/policies`. Only a tenant administrator MAY change them.

#### Scenario: Regular member reads the policy
- **WHEN** a member without the tenant administrator role reads the policy
- **THEN** the current policy values are returned

#### Scenario: Regular member changes the policy
- **WHEN** a member without the tenant administrator role attempts to save the policy
- **THEN** the request is rejected as forbidden, the attempt is recorded as denied in the audit log, and the policy is unchanged

### Requirement: Policy changes take effect at the next use
A changed access token lifetime SHALL apply to tokens issued after the change. A changed idle timeout SHALL apply to existing sessions at their next request. A changed refresh token lifetime SHALL apply to existing sessions at their next refresh, as specified in `session-management`.

#### Scenario: Idle timeout shortened
- **WHEN** the idle timeout is reduced from 30 to 10 minutes and a session has been idle for 12 minutes
- **THEN** the next request of that session is rejected as unauthorized
