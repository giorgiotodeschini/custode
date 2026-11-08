# Spec Delta

## ADDED Requirements

### Requirement: Credential resolution is the only tenant-free lookup for unauthenticated requests
Sign-in and refresh are the only requests without an authenticated tenant. They SHALL resolve the credential they receive (a login identifier or a refresh token) without a tenant scope, and SHALL bind the request to the tenant of the resolved user or session. Credential resolution SHALL NOT return data beyond the identity and session needed to authenticate, and SHALL NOT accept a tenant identifier from the client. No other unauthenticated operation MAY read tenant-owned data across tenants.

#### Scenario: Sign-in resolves the tenant from the user
- **WHEN** a user signs in with a login identifier that belongs to a tenant
- **THEN** the session is bound to that tenant, and a tenant identifier in the request is ignored

#### Scenario: Refresh resolves the tenant from the session
- **WHEN** a refresh request presents a refresh token
- **THEN** the request is bound to the tenant of the session that holds the token, and no other session is read

#### Scenario: Unauthenticated read of tenant data
- **WHEN** an unauthenticated request asks for tenant-owned data
- **THEN** the request is rejected as unauthorized
