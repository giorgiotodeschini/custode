// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Infrastructure.Audit;

public interface IAuditTrail
{
    Task RecordAsync(AuditRecord record, CancellationToken cancellationToken);

    Task RecordDeniedAsync(AuditRecord record, CancellationToken cancellationToken);

    Task<AuditVerification> VerifyAsync(CancellationToken cancellationToken);
}

public sealed record AuditRecord(
    Guid ActorUserId,
    string Action,
    string? TargetType = null,
    string? TargetId = null,
    string? SecondaryTargetType = null,
    string? SecondaryTargetId = null);

public sealed record AuditVerification(bool IsIntact, long EntriesVerified, long? FirstAlteredSequence);

public static class AuditActions
{
    public const string SecretCreated = "secret created";
    public const string SecretUpdated = "secret updated";
    public const string SecretDeleted = "secret deleted";
    public const string SecretMoved = "secret moved";
    public const string SecretValueRead = "secret value read";
    public const string FolderCreated = "folder created";
    public const string FolderDeleted = "folder deleted";
    public const string GroupCreated = "group created";
    public const string GroupRenamed = "group renamed";
    public const string GroupDeleted = "group deleted";
    public const string MembershipAdded = "membership added";
    public const string MembershipRemoved = "membership removed";
    public const string GrantSet = "grant set";
    public const string GrantRevoked = "grant revoked";
    public const string InheritanceBroken = "inheritance broken";
    public const string InheritanceRestored = "inheritance restored";
    public const string VaultInitialized = "vault initialized";
    public const string AuditTamperingAttempt = "audit tampering attempt";
}
