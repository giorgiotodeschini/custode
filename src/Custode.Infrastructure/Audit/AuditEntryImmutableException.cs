// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Infrastructure.Audit;

public sealed class AuditEntryImmutableException()
    : InvalidOperationException("Audit entries are immutable and cannot be modified or deleted.");
