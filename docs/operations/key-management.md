# Key management

Custode encrypts secret values with ASP.NET Core Data Protection. The data protection key ring is stored in PostgreSQL, and every key in it is encrypted with a separate key-encryption key (KEK) that is never stored in the database.

If the KEK is lost, every secret encrypted with it becomes unrecoverable. Treat the KEK as the most sensitive asset of the deployment.

## Components

| Item | Where it lives |
|---|---|
| Secret ciphertext | Application data (not yet implemented). |
| Data protection key ring | `DataProtectionKeys` table, each key encrypted with the KEK. |
| Key-encryption key (KEK) | X.509 certificate with private key, PKCS#12 (`.pfx`) file on the host. |

## Provisioning the KEK

1. Create or obtain an X.509 certificate with a private key, exported as PKCS#12.
2. Place the `.pfx` file on the host, readable only by the service account.
3. Configure the location and password. Provide the password through an environment variable or a secret store, never in source control:

```
DataProtection__KeyEncryptionCertificatePath=/etc/custode/kek.pfx
DataProtection__KeyEncryptionCertificatePassword=<from secret store>
```

4. Start the server. The startup check creates the first data protection key when the ring is empty, and reports `Secret protection is available.` in the logs.

## Startup behavior

The server verifies secret protection at startup and then serves secret operations only if verification succeeded.

| Condition | Result |
|---|---|
| KEK path not configured, file missing, no private key, or unreadable | Secret operations are refused (`SecretProtectionUnavailableException`). No key ring is written to the database and no keys are created on disk. An error is logged with the reason. |
| Key ring cannot be decrypted with the configured KEK (for example, a different certificate) | Secret operations are refused. Keys are not regenerated silently. |
| KEK and key ring are valid | Secret operations are served. |

Automatic key generation is disabled when the key ring is persisted, so an unreadable ring can never be replaced silently.

## Backup and recovery

- Back up the KEK `.pfx` file and its password separately from the database backups. A database backup alone cannot decrypt secrets.
- Back up the `DataProtectionKeys` table together with the application database so that secrets and the keys that encrypt them are restored from the same point in time.
- Restore procedure: restore the database, place the original KEK file on the host, start the server, and confirm `Secret protection is available.` in the logs.
- If the KEK is lost or does not match the key ring, the server refuses secret operations. Restoring the matching KEK is the only way to recover those secrets.

## Rotation

- Keys in the ring have a 90-day lifetime. New keys are created through ASP.NET Core Data Protection (`IKeyManager.CreateNewKey`). Secrets encrypted with older keys remain readable because older keys stay in the ring.
- Rotating the KEK itself (replacing the certificate) requires re-encrypting the key ring with the new certificate. This procedure is not automated yet.
- Operator-triggered key rotation is not yet exposed through an endpoint or command.

## Logging rule

Secret plaintext must never be written to logs, exception messages, or telemetry. The secret protection components log only status and failure reasons. Tests in `tests/Custode.Server.Tests/SecretProtection/` verify this for protect, unprotect, and refused operations. Future secret create, read, and update endpoints must follow the same rule and log identifiers only.

## Tenant binding of ciphertext

Each secret is protected with the purpose `custode.secrets.v1|<tenantId>`. A ciphertext can only be decrypted by the tenant it was created for, even if the record is moved in the database.
