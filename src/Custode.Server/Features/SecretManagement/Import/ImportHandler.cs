// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Access;
using Custode.Infrastructure.Audit;
using Custode.Infrastructure.Identity;
using Custode.Infrastructure.Persistence;
using Custode.Infrastructure.Tenancy;
using Custode.Infrastructure.Vault;
using Custode.Server.Features.SecretManagement.Common;
using Custode.Server.Features.SecretManagement.Secrets;
using Microsoft.EntityFrameworkCore;

namespace Custode.Server.Features.SecretManagement.Import;

internal sealed class ImportHandler(
    AppDbContext dbContext,
    IFolderAccess folderAccess,
    SecretValueCodec codec,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IAuditTrail auditTrail)
{
    public const int MaxRows = 500;

    private sealed record PreparedRow(int Index, string Name, SecretType Type, string FolderPath, string? FileName, string ProtectedPayload);

    public async Task<ImportOutcome> HandleAsync(ImportRequest request, bool dryRun, CancellationToken cancellationToken)
    {
        var items = request.Items ?? [];

        if (items.Count is 0 or > MaxRows)
        {
            throw new InvalidVaultRequestException($"An import requires between 1 and {MaxRows} rows.");
        }

        var tenantId = tenantContext.RequireTenantId();
        var userId = currentUser.Id;
        var createMissing = request.CreateMissingFolders ?? false;

        var statuses = new string?[items.Count];
        var messages = new string?[items.Count];
        var folderIdOfRow = new Guid?[items.Count];
        var prepared = new List<PreparedRow>();

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];

            try
            {
                var name = SecretName.Normalize(item.Name);
                var type = SecretValueCodec.ParseType(item.Type);
                var folderPath = FolderPath.Normalize(item.FolderPath);
                var value = codec.Protect(tenantId, type, new SecretValueInput(
                    item.Username,
                    item.Password,
                    item.Value,
                    item.FileName,
                    item.ContentBase64,
                    item.Fields,
                    item.Pairs?.Select(pair => new SecretPairInput(pair.Key, pair.Value)).ToList()));

                prepared.Add(new PreparedRow(index, name, type, folderPath, value.FileName, value.ProtectedPayload));
            }
            catch (InvalidVaultRequestException exception)
            {
                statuses[index] = "invalid";
                messages[index] = exception.Message;
            }
        }

        var folders = await dbContext.Folders.AsNoTracking().ToDictionaryAsync(folder => folder.Path, folder => folder, cancellationToken);
        var permissions = await folderAccess.GetEffectivePermissionsForAllFoldersAsync(userId, cancellationToken);

        foreach (var row in prepared)
        {
            var (status, message, folderId) = ResolveFolder(row.FolderPath, folders, permissions, createMissing);
            folderIdOfRow[row.Index] = folderId;
            if (status is not null)
            {
                statuses[row.Index] = status;
                messages[row.Index] = message;
            }
        }

        var existingNames = folders.Count == 0
            ? new HashSet<(Guid, string)>()
            : (await dbContext.Secrets.AsNoTracking()
                .Where(secret => folders.Values.Select(folder => folder.Id).Contains(secret.FolderId))
                .Select(secret => new { secret.FolderId, secret.Name })
                .ToListAsync(cancellationToken))
                .Select(secret => (secret.FolderId, secret.Name))
                .ToHashSet();

        var batchKeys = prepared.GroupBy(row => (row.FolderPath, row.Name)).Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(row => row.Index)).ToHashSet();

        foreach (var row in prepared.Where(row => statuses[row.Index] is null))
        {
            if (batchKeys.Contains(row.Index) || (folderIdOfRow[row.Index] is { } folderId && existingNames.Contains((folderId, row.Name))))
            {
                statuses[row.Index] = "conflict";
                messages[row.Index] = "A secret with this name already exists in the folder.";
            }
        }

        var rows = Enumerable.Range(0, items.Count)
            .Select(index => new ImportRowResult(index, statuses[index] ?? "valid", messages[index]))
            .ToList();

        if (dryRun)
        {
            return new ImportOutcome(StatusCodes.Status200OK, new ImportResponse(null, true, false, 0, rows));
        }

        if (rows.Any(row => row.Status == "forbidden"))
        {
            var firstForbidden = rows.First(row => row.Status == "forbidden").Index;
            await auditTrail.RecordDeniedAsync(
                new AuditRecord(userId, AuditActions.SecretsImported, "folder", folderIdOfRow[firstForbidden]?.ToString("D")),
                cancellationToken);

            return new ImportOutcome(StatusCodes.Status403Forbidden, new ImportResponse(null, false, false, 0, rows));
        }

        if (rows.Any(row => row.Status != "valid"))
        {
            return new ImportOutcome(StatusCodes.Status422UnprocessableEntity, new ImportResponse(null, false, false, 0, rows));
        }

        var batchId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var toCreate = prepared;

        // One unit of work: every folder, every secret and every audit entry commits together, or none does.
        await AuditedWork.CommitAsync(dbContext, async () =>
        {
            var existing = await dbContext.Folders.AsNoTracking().ToDictionaryAsync(folder => folder.Path, folder => folder.Id, cancellationToken);
            var idByPath = new Dictionary<string, Guid>(existing);

            if (createMissing)
            {
                foreach (var path in toCreate.Select(row => row.FolderPath).Distinct().SelectMany(FolderPath.AncestorsAndSelf).Distinct().OrderBy(path => path.Count(character => character == '/')))
                {
                    if (idByPath.ContainsKey(path))
                    {
                        continue;
                    }

                    var folder = new Folder
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        Path = path,
                        ParentId = FolderPath.ParentOf(path) is { } parentPath ? idByPath[parentPath] : null,
                        InheritsPermissions = true,
                        CreatedAtUtc = now,
                    };

                    dbContext.Folders.Add(folder);
                    idByPath[path] = folder.Id;

                    await auditTrail.RecordAsync(
                        new AuditRecord(userId, AuditActions.FolderCreated, "folder", folder.Id.ToString("D")),
                        cancellationToken);
                }
            }

            foreach (var row in toCreate)
            {
                var folderId = idByPath[row.FolderPath];
                var secret = new Secret
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    FolderId = folderId,
                    Name = row.Name,
                    Type = row.Type,
                    FileName = row.FileName,
                    ProtectedPayload = row.ProtectedPayload,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    CurrentVersion = 1,
                    ConcurrencyStamp = Guid.NewGuid(),
                };

                dbContext.Secrets.Add(secret);
                dbContext.SecretVersions.Add(SecretVersioning.Snapshot(secret, SecretVersionChangeKind.Created, null, userId, now));

                await auditTrail.RecordAsync(
                    new AuditRecord(userId, AuditActions.SecretCreated, "secret", secret.Id.ToString("D"), "folder", folderId.ToString("D")),
                    cancellationToken);
            }

            await auditTrail.RecordAsync(
                new AuditRecord(userId, AuditActions.SecretsImported, "import", batchId.ToString("D"), "batch", batchId.ToString("D")),
                cancellationToken);

            return true;
        }, cancellationToken);

        return new ImportOutcome(StatusCodes.Status200OK, new ImportResponse(batchId, false, true, toCreate.Count, rows));
    }

    private static (string? Status, string? Message, Guid? FolderId) ResolveFolder(
        string folderPath,
        IReadOnlyDictionary<string, Folder> folders,
        IReadOnlyDictionary<Guid, FolderPermissions> permissions,
        bool createMissing)
    {
        if (folders.TryGetValue(folderPath, out var folder))
        {
            return Check(permissions.GetValueOrDefault(folder.Id), folder.Id);
        }

        if (!createMissing)
        {
            return ("folder-not-found", "The target folder does not exist.", null);
        }

        // A missing folder can be created only with Create on its nearest existing ancestor.
        var ancestors = FolderPath.AncestorsAndSelf(folderPath).Reverse().Skip(1);

        foreach (var ancestor in ancestors)
        {
            if (folders.TryGetValue(ancestor, out var nearest))
            {
                return Check(permissions.GetValueOrDefault(nearest.Id), nearest.Id);
            }
        }

        return ("folder-not-found", "The target folder does not exist.", null);
    }

    private static (string? Status, string? Message, Guid? FolderId) Check(FolderPermissions effective, Guid folderId)
    {
        if (effective == FolderPermissions.None)
        {
            return ("folder-not-found", "The target folder does not exist.", null);
        }

        if (!effective.HasFlag(FolderPermissions.Create))
        {
            return ("forbidden", "Create permission is required on the target folder.", folderId);
        }

        return (null, null, folderId);
    }
}
