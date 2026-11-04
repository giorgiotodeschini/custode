// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Infrastructure.SecretProtection;

public sealed class SecretProtectionUnavailableException(string reason)
    : InvalidOperationException($"Secret operations are unavailable: {reason}");
