// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Tenancy;
using Microsoft.AspNetCore.DataProtection;

namespace Custode.Infrastructure.SecretProtection;

public sealed class SecretProtector(IDataProtectionProvider dataProtectionProvider, SecretProtectionStatus status)
    : ISecretProtector
{
    private const string PurposePrefix = "custode.secrets.v1|";

    public string Protect(Guid tenantId, string plaintext)
    {
        EnsureAvailable(tenantId);

        return CreateProtector(tenantId).Protect(plaintext);
    }

    public string Unprotect(Guid tenantId, string protectedValue)
    {
        EnsureAvailable(tenantId);

        return CreateProtector(tenantId).Unprotect(protectedValue);
    }

    private IDataProtector CreateProtector(Guid tenantId)
    {
        return dataProtectionProvider.CreateProtector(PurposePrefix + tenantId);
    }

    private void EnsureAvailable(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new TenantScopeViolationException("Secret operations require a tenant.");
        }

        if (!status.IsAvailable)
        {
            throw new SecretProtectionUnavailableException(status.UnavailableReason);
        }
    }
}
