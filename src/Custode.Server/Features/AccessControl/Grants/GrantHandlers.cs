// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.AccessControl.Common;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.AccessControl.Grants;

internal static class GrantParser
{
    public static PrincipalKind ParsePrincipalKind(string? raw)
    {
        return raw switch
        {
            "user" => PrincipalKind.User,
            "group" => PrincipalKind.Group,
            _ => throw new InvalidVaultRequestException("The principal kind must be 'user' or 'group'."),
        };
    }

    public static string FormatPrincipalKind(PrincipalKind kind)
    {
        return kind == PrincipalKind.User ? "user" : "group";
    }

    public static FolderPermissions ParsePermissions(IReadOnlyList<string>? permissions)
    {
        if (permissions is null || permissions.Count == 0)
        {
            throw new InvalidVaultRequestException("At least one permission is required.");
        }

        var result = FolderPermissions.None;

        foreach (var permission in permissions)
        {
            result |= permission.ToLowerInvariant() switch
            {
                "read" => FolderPermissions.Read,
                "write" => FolderPermissions.Write,
                "create" => FolderPermissions.Create,
                "delete" => FolderPermissions.Delete,
                _ => throw new InvalidVaultRequestException("Permissions must be read, write, create, or delete."),
            };
        }

        return result;
    }
}

internal sealed class SetGrantHandler(
    AppDbContext dbContext,
    ITenantContext tenantContext,
    TenantUserLookup userLookup,
    ICurrentUser currentUser,
    IAuditTrail auditTrail)
{
    public async Task HandleAsync(SetGrantRequest request, CancellationToken cancellationToken)
    {
        var folder = await FolderLookup.RequireFolderAsync(dbContext, request.FolderPath, cancellationToken);
        var kind = GrantParser.ParsePrincipalKind(request.PrincipalKind);
        var permissions = GrantParser.ParsePermissions(request.Permissions);

        if (kind == PrincipalKind.User)
        {
            await userLookup.RequireUserAsync(request.PrincipalId);
        }
        else
        {
            await GroupLookup.RequireGroupAsync(dbContext, request.PrincipalId, cancellationToken);
        }

        var existing = await dbContext.FolderGrants.FirstOrDefaultAsync(
            grant => grant.FolderId == folder.Id && grant.PrincipalKind == kind && grant.PrincipalId == request.PrincipalId,
            cancellationToken);

        var tenantId = tenantContext.RequireTenantId();

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            if (existing is null)
            {
                dbContext.FolderGrants.Add(new FolderGrant
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    FolderId = folder.Id,
                    PrincipalKind = kind,
                    PrincipalId = request.PrincipalId,
                    Permissions = permissions,
                });
            }
            else
            {
                existing.Permissions = permissions;
            }

            await auditTrail.RecordAsync(
                new AuditRecord(
                    currentUser.Id,
                    AuditActions.GrantSet,
                    "folder",
                    folder.Id.ToString("D"),
                    GrantParser.FormatPrincipalKind(kind),
                    request.PrincipalId.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);
    }
}

internal sealed class RevokeGrantHandler(AppDbContext dbContext, ICurrentUser currentUser, IAuditTrail auditTrail)
{
    public async Task HandleAsync(string? folderPath, string? principalKind, Guid principalId, CancellationToken cancellationToken)
    {
        var folder = await FolderLookup.RequireFolderAsync(dbContext, folderPath, cancellationToken);
        var kind = GrantParser.ParsePrincipalKind(principalKind);

        var grant = await dbContext.FolderGrants.FirstOrDefaultAsync(
            entry => entry.FolderId == folder.Id && entry.PrincipalKind == kind && entry.PrincipalId == principalId,
            cancellationToken)
            ?? throw new VaultNotFoundException("No grant exists for this folder and principal.");

        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            dbContext.FolderGrants.Remove(grant);
            await auditTrail.RecordAsync(
                new AuditRecord(
                    currentUser.Id,
                    AuditActions.GrantRevoked,
                    "folder",
                    folder.Id.ToString("D"),
                    GrantParser.FormatPrincipalKind(kind),
                    principalId.ToString("D")),
                cancellationToken);
            return true;
        }, cancellationToken);
    }
}
