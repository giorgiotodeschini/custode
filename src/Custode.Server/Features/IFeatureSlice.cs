namespace Custode.Server.Features;

public interface IFeatureSlice
{
    void RegisterServices(IServiceCollection services);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
