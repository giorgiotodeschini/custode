using Custode.Infrastructure.Authentication;
using Custode.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Custode.Server.Features.Authentication.SignIn;

public sealed class SignInHandler(UserManager<ApplicationUser> userManager, ITokenIssuer tokenIssuer)
{
    public async Task<IssuedToken?> HandleAsync(SignInRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByNameAsync(request.Login);

        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            return null;
        }

        return await tokenIssuer.IssueAsync(user, cancellationToken);
    }
}
