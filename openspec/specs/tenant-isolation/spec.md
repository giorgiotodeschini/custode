# tenant-isolation Specification

## Purpose
Defines how multiple tenants share one database while their data remains strictly separated, so that no tenant can read or modify another tenant's data.

## Requirements

### Requirement: Tenant-owned data carries a tenant identifier
Every entity that belongs to a tenant SHALL store a `TenantId` value that identifies its owning tenant. An entity MUST NOT be persisted without a `TenantId`.

#### Scenario: Tenant-owned entity is created
- **WHEN** a tenant-owned entity is created
- **THEN** it is stored with the `TenantId` of the tenant performing the operation

#### Scenario: Entity without tenant is rejected
- **WHEN** an attempt is made to persist a tenant-owned entity without a `TenantId`
- **THEN** the operation fails and nothing is stored

### Requirement: Requests operate within exactly one tenant scope
Every authenticated request SHALL be bound to exactly one tenant, determined from the caller's identity. Requests that cannot be bound to a tenant MUST be rejected.

#### Scenario: Authenticated request with a tenant
- **WHEN** an authenticated user makes a request
- **THEN** all data access for that request is limited to the user's tenant

#### Scenario: Request without a tenant claim
- **WHEN** a request carries a valid identity that is not associated with any tenant
- **THEN** the request is rejected as unauthorized

### Requirement: Cross-tenant access is impossible through regular operations
Queries and commands on tenant-owned data SHALL automatically include the current tenant scope. A regular operation MUST NOT return or modify data belonging to another tenant, even when the requested identifier belongs to that tenant.

#### Scenario: Reading another tenant's entity by identifier
- **WHEN** a user requests an entity by identifier and that entity belongs to a different tenant
- **THEN** the system responds as if the entity does not exist

#### Scenario: Updating another tenant's entity by identifier
- **WHEN** a user attempts to update an entity that belongs to a different tenant
- **THEN** the update is not applied and no data of the other tenant is changed

### Requirement: Tenant scope cannot be set by the client
The tenant of a request SHALL be derived only from the authenticated identity. A client MUST NOT be able to choose or override the tenant through request content, headers, or query parameters.

#### Scenario: Client supplies a different tenant identifier
- **WHEN** a request includes a tenant identifier that differs from the authenticated tenant
- **THEN** the supplied identifier is ignored and the request is processed within the authenticated tenant only

### Requirement: Tenant-free operations are explicitly marked
Operations that intentionally act across tenants, such as provisioning a new tenant, SHALL be explicitly marked as tenant-independent and SHALL be restricted to privileged operators.

#### Scenario: Non-operator attempts a tenant-independent operation
- **WHEN** a user without operator privileges invokes a tenant-independent operation
- **THEN** the operation is rejected as forbidden

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
