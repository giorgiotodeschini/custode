# Authentication

Custode authenticates users with local credentials stored through ASP.NET Core Identity and issues signed JWT access tokens. Token validation is independent of how a token was issued, so additional identity providers (per-tenant OIDC) can be added later without changing how endpoints read the caller's identity.

## Sign-in

`POST /api/auth/sign-in` is the only anonymous endpoint that issues tokens.

Request body:

```json
{ "login": "alice", "password": "..." }
```

- Success returns `200 OK` with `{ "accessToken": "...", "expiresAt": "..." }`. The refresh token is not in the body; it is set as the `custode_refresh` cookie (see Sessions).
- A disabled account gets the same generic `401` as an unknown login.
- Any failure (unknown login or wrong password) returns `401 Unauthorized` with an empty body. The two cases are indistinguishable to the caller.

## Token claims

| Claim | Value |
|---|---|
| `sub` | Identity user id (GUID). |
| `tenant_id` | Tenant the user belongs to (GUID). Required; see below. |
| `role` | One claim per Identity role assigned to the user. |
| `sid` | Identifier of the session the token belongs to. Required; a token without it is rejected. |
| `iat`, `nbf`, `exp` | Issue time, not-before, and expiry. Lifetime is the tenant's access token lifetime policy, falling back to `Jwt:LifetimeMinutes`. |
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

## Sessions

Each sign-in starts a session. The refresh token is a random 32-byte value, delivered only as the HTTP-only, Secure, `SameSite=Strict` cookie `custode_refresh` with the path `/api/auth`. The server stores only its SHA-256 hash.

- `POST /api/auth/refresh` rotates the refresh token and returns a new access token. It requires the header `X-Custode-Request`; without it the request is `403`.
- A refresh token that was already replaced is a reuse. Within a 10-second grace window (`Sessions:ReuseGraceSeconds`, default 10) it is rejected without effect, which lets two browser tabs refresh at once. After the window, the session is revoked and the audit entry `refresh token reuse detected` is recorded.
- `POST /api/auth/sign-out` revokes the session and clears the cookie.
- `POST /api/auth/activity` records activity. Every authenticated request also records activity, at most once a minute. A refresh does not count as activity.
- A session ends after the tenant's idle timeout without activity, or after the tenant's refresh token lifetime from its start.

Every authenticated request is checked against its session: the session must exist, not be revoked, not be idle or expired, and its user must be enabled. A revoked session therefore stops working before its access token expires.

## Password change

Accounts created by a tenant administrator carry the requirement to change the password. Until it is changed, every endpoint except `POST /api/auth/change-password` and `POST /api/auth/sign-out` answers `403` with the reason `password change required`. `POST /api/auth/change-password` takes the current and the new password and ends the user's other sessions.

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
- Token consumption depends only on the validated principal (`sub`, `tenant_id`, `role`, `sid`). A future OIDC handler must produce the same `tenant_id` claim, which is the contract that tenant scoping relies on.
- Per-tenant OIDC configuration is out of scope for this change.

## Tests

The behavior above is covered in `tests/Custode.Server.Tests/Authentication/`, `tests/Custode.Server.Tests/Identity/`, `tests/Custode.Server.Tests/Sessions/`, and uses an in-process test host with the same service composition as `Program.cs`. Behavior that needs concurrent writes is covered on PostgreSQL in `tests/Custode.Server.Tests/EndToEnd/PostgreSqlSessionTests.cs`.
