// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.Authentication;

namespace Custode.Server.Authentication;

public sealed record IssuedSession(IssuedToken AccessToken, string RefreshToken, DateTimeOffset RefreshExpiresAtUtc);
