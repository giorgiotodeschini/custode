// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Infrastructure.Sessions;

public sealed class SessionOptions
{
    public const string SectionName = "Sessions";

    public const int DefaultReuseGraceSeconds = 10;

    public int ReuseGraceSeconds { get; set; } = DefaultReuseGraceSeconds;
}
