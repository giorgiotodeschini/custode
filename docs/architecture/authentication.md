# Authentication

Custode authenticates users with local credentials stored through ASP.NET Core Identity and issues signed JWT access tokens. Token validation is independent of how a token was issued, so additional identity providers (per-tenant OIDC) can be added later without changing how endpoints read the caller's identity.

## Sign-in

`POST /api/auth/sign-in` is the only anonymous endpoint that issues tokens.

Request body:

```json
{ "login": "alice", "password": "..." }
```

- Success returns `200 OK` with `{ "accessToken": "...", "expiresAt": "..." }`.
- Any failure (unknown login or wrong password) returns `401 Unauthorized` with an empty body. The two cases are indistinguishable to the caller.

## Token claims

| Claim | Value |
|---|---|
| `sub` | Identity user id (GUID). |
| `tenant_id` | Tenant the user belongs to (GUID). Required; see below. |
| `role` | One claim per Identity role assigned to the user. |
| `iat`, `nbf`, `exp` | Issue time, not-before, and expiry. Lifetime is `Jwt:LifetimeMinutes`. |
| `iss`, `aud` | `Jwt:Issuer` and `Jwt:Audience`. |

Tokens are signed with HMAC-SHA256 using `Jwt:SigningKey`.

## Validation

Every endpoint requires an authenticated caller unless it is explicitly marked `AllowAnonymous`. A request is rejected with `401 Unauthorized` when:

- the token is missing;
- the signature does not match the configured key;
- the issuer or audience does not match;
- the token is expired (clock skew tolerance is one minute);
- the token has no `tenant_id` claim, or the claim is not a GUID.

Tokens are never read from anywhere other than the `Authorization: Bearer` header.

## Configuration

```json
"Jwt": {
  "Issuer": "custode",
  "Audience": "custode-clients",
  "SigningKey": "<at least 32 bytes>",
  "LifetimeMinutes": 15
}
```

`SigningKey` must not be committed to source control. Provide it through environment variables or a secret store. The server refuses to issue tokens if the section is invalid.

## Extension point for external identity providers

- Token issuance is isolated behind `ITokenIssuer` (`src/Custode.Infrastructure/Authentication/`). Only local sign-in calls it.
- Token consumption depends only on the validated principal (`sub`, `tenant_id`, `role`). A future OIDC handler must produce the same `tenant_id` claim, which is the contract that tenant scoping relies on.
- Per-tenant OIDC configuration is out of scope for this change.

## Tests

The behavior above is covered in `tests/Custode.Server.Tests/Authentication/`, `tests/Custode.Server.Tests/Identity/`, and uses an in-process test host with the same service composition as `Program.cs`.
