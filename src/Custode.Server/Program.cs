using Custode.Infrastructure.Persistence;
using Custode.Server;
using Custode.Server.Features;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Custode")));

builder.Services.AddCustodeServer(builder.Configuration);

var app = builder.Build();

app.UseCustodeServer();

app.Logger.LogInformation("Registered {Count} feature slices", FeatureSlices.Count);

app.Run();
