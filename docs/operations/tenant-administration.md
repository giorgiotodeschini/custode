# Tenant administration and backups

## Tenant policies

A tenant administrator sets the policy with `PUT /api/tenant/policies`. Every value is required and must be in range; otherwise nothing is saved and the response names the field.

| Field | Range | Default |
|---|---|---|
| `idleTimeoutMinutes` | 5 to 120 | 15 |
| `accessTokenLifetimeMinutes` | 5 to 60 | `Jwt:LifetimeMinutes` (15) |
| `refreshTokenLifetimeDays` | 1 to 30 | 7 |
| `clipboardClearSeconds` | 10 to 120 | 30 |

Shortening the idle timeout ends idle sessions at their next request. The refresh token lifetime applies at each session's next refresh, measured from the session start.

## Session settings

| Setting | Default | Meaning |
|---|---|---|
| `Sessions:ReuseGraceSeconds` | 10 | Window in which a replaced refresh token is rejected without ending the session. Range 0 to 60. |

## Encrypted tenant backups

`POST /api/backups` returns a single `.custode-backup` file. Only a tenant administrator with Read on every folder of the tenant can create one.

- The passphrase must be at least 12 characters. It is normalized (Unicode NFC) before use, and it is never stored. **If it is lost, the backup cannot be opened.** Keep it in a password manager, separate from the backup file.
- The file is encrypted with a key derived from the passphrase (PBKDF2-HMAC-SHA256, 600,000 iterations) and sealed with AES-256-GCM. Changing any byte of the file, or using a wrong passphrase, makes decryption fail.
- The content includes the secret values and all their versions, so the file must be stored as carefully as the vault itself.
- `Backups:MaxBackupBytes` limits the decrypted content size (default 256 MiB). A larger tenant gets `413`.
- Each export is recorded in the audit log with the action `tenant backup exported`.

Restoring a backup is not implemented yet. The format version (1) is recorded in the file so that a future restore can recognize it.

## Bulk import

`POST /api/secrets/imports` accepts at most 500 rows and at most `Secrets:MaxImportBodyBytes` (default 5 MiB). A dry run (`?dryRun=true`) reports every row and writes nothing. A commit stores all rows or none.
