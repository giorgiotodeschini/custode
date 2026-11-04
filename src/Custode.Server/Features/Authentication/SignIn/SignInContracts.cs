// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Server.Features.Authentication.SignIn;

public sealed record SignInRequest(string Login, string Password);

public sealed record SignInResponse(string AccessToken, DateTimeOffset ExpiresAt);
