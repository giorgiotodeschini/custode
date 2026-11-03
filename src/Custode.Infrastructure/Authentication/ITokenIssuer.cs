using Custode.Infrastructure.Identity;

namespace Custode.Infrastructure.Authentication;

public interface ITokenIssuer
{
    Task<IssuedToken> IssueAsync(ApplicationUser user, CancellationToken cancellationToken = default);
}

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);
