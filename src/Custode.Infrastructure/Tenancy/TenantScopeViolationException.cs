// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Infrastructure.Tenancy;

public sealed class TenantScopeViolationException(string message) : InvalidOperationException(message);
