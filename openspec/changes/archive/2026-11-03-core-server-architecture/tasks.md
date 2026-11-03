# Tasks

## 1. Solution scaffolding

- [x] 1.1 Create the .NET 10 solution with the ASP.NET Core host project and the `Custode.Infrastructure` library; verify `dotnet build` succeeds with no warnings treated as errors
- [x] 1.2 Add a test project and wire it to the solution; verify `dotnet test` runs and reports zero failures on an empty suite
- [x] 1.3 Add a `Features/` folder convention and a composition entry point that registers slices explicitly in `Program.cs`; verify the host starts and lists zero slices without error

## 2. Shared persistence and tenant scoping

- [x] 2.1 Define `ITenantOwned` with a `TenantId` property and an `ITenantContext` abstraction; verify with a unit test that a tenant-owned type exposes `TenantId`
- [x] 2.2 Implement `AppDbContext` with a global query filter on `TenantId` for every `ITenantOwned` entity; verify an integration test returns only rows of the current tenant from a seeded two-tenant database (tenant-isolation: cross-tenant reads)
- [x] 2.3 Implement the save-time check that rejects tenant-owned entities without a `TenantId` or with a `TenantId` different from the current tenant; verify tests for both rejection cases (tenant-isolation: entity without tenant is rejected)
- [x] 2.4 Implement the explicitly named tenant-independent query path guarded by an operator policy; verify a test that a non-operator is forbidden and an operator can read across tenants (tenant-isolation: tenant-free operations)
- [x] 2.5 Verify that updating another tenant's entity by identifier changes no data; verify with an integration test (tenant-isolation: cross-tenant update)
- [x] 2.6 Document the tenant scoping rules and the operator query path in `docs/architecture/tenancy.md`; verify the document covers the filter, the save-time check, and the operator path

## 3. Authentication

- [x] 3.1 Configure ASP.NET Core Identity on `AppDbContext` for users and roles, with password hashing defaults; verify a test that a created user's stored password is not the plaintext (local-authentication: credential storage)
- [x] 3.2 Implement `ITokenIssuer` producing a signed JWT with `sub`, `tenant_id`, roles, and expiration; verify a test decodes an issued token and checks each claim
- [x] 3.3 Implement the sign-in endpoint returning the same generic error for unknown users and wrong passwords; verify tests for valid credentials, wrong password, and unknown login (local-authentication: user authentication)
- [x] 3.4 Configure JWT bearer validation with secure-by-default authorization and an explicit `AllowAnonymous` marker; verify tests for expired tokens, invalid signatures, and requests without a token (local-authentication: protected endpoints)
- [x] 3.5 Bind the tenant scope from the `tenant_id` claim only; verify tests showing that a request without the claim is rejected and that a tenant supplied in headers or body is ignored (tenant-isolation: client cannot set tenant scope)
- [x] 3.6 Document the sign-in flow, token claims, and the extension point for additional issuers in `docs/architecture/authentication.md`; verify the document lists every claim the token contains

## 4. Secret encryption

- [x] 4.1 Configure ASP.NET Core Data Protection with the key ring persisted through `PersistKeysToDbContext` and encrypted with the X.509 key-encryption key; verify the key ring table holds no plaintext key material (secret-encryption: keys persisted and managed)
- [x] 4.2 Implement startup validation that the KEK can be loaded and the key ring can be read, refusing secret operations on failure; verify tests for a missing KEK and an unreadable key ring (secret-encryption: key ring is missing)
- [x] 4.3 Implement a secret protector using the purpose `custode.secrets.v1|<tenantId>`; verify a test that ciphertext is not equal to plaintext and that the same value protects differently per tenant (secret-encryption: at rest, purpose binding, tenant scope)
- [x] 4.4 Verify that decryption under a different purpose or after the record is moved to another tenant fails and returns no plaintext; verify with unit tests (secret-encryption: purpose binding, tenant scope)
- [x] 4.5 Verify key rotation keeps existing secrets readable by activating a new key and decrypting an older ciphertext; verify with an integration test (secret-encryption: key rotation)
- [x] 4.6 Add a logging rule and a test that secret create/read/update paths never write the plaintext to logs; verify by capturing logs during those operations (secret-encryption: secret value in logs)
- [x] 4.7 Document key ring backup, KEK provisioning, and rotation procedure in `docs/operations/key-management.md`; verify the document contains the KEK backup and recovery steps

## 5. Integration verification

- [x] 5.1 Run the full test suite against a clean database with two tenants and one operator; verify that all tenant-isolation, authentication, and encryption scenarios pass end to end
- [x] 5.2 Restart the host and verify that a previously stored secret decrypts successfully (secret-encryption: server restarts)

## Workflow follow-up

- Archive the change after the server core review is complete and all tracked tasks are checked.
- Verify the archived result and sync the specs to `openspec/specs/`.
