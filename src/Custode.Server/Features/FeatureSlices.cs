using Custode.Server.Features.Authentication.SignIn;

namespace Custode.Server.Features;

public static class FeatureSlices
{
    private static readonly IFeatureSlice[] All =
    [
        new SignInSlice(),
    ];

    public static int Count => All.Length;

    public static void RegisterServices(IServiceCollection services)
    {
        foreach (var slice in All)
        {
            slice.RegisterServices(services);
        }
    }

    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        foreach (var slice in All)
        {
            slice.MapEndpoints(endpoints);
        }
    }
}
