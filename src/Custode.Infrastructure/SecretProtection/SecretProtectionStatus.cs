// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Infrastructure.SecretProtection;

public sealed class SecretProtectionStatus
{
    private const string NotVerifiedReason = "Secret protection has not been verified yet.";

    public bool IsAvailable { get; private set; }

    public string UnavailableReason { get; private set; } = NotVerifiedReason;

    public bool HasConfigurationError { get; private set; }

    public void MarkConfigurationFailed(string reason)
    {
        HasConfigurationError = true;
        MarkUnavailable(reason);
    }

    public void MarkAvailable()
    {
        IsAvailable = true;
        UnavailableReason = string.Empty;
    }

    public void MarkUnavailable(string reason)
    {
        IsAvailable = false;
        UnavailableReason = reason;
    }
}
