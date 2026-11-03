namespace Custode.Server.Features.Authentication.SignIn;

public sealed record SignInRequest(string Login, string Password);

public sealed record SignInResponse(string AccessToken, DateTimeOffset ExpiresAt);
