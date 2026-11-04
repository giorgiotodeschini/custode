// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Custode.Infrastructure.SecretProtection;

public sealed class SecretProtectionStartupCheck(
    IServiceProvider services,
    SecretProtectionStatus status,
    ILogger<SecretProtectionStartupCheck> logger) : IHostedService
{
    private const int KeyLifetimeDays = 90;
    private const string ProbePurpose = "custode.startup-probe";
    private const string ProbePayload = "custode-startup-probe";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (status.HasConfigurationError)
        {
            logger.LogError("Secret protection is unavailable: {Reason}", status.UnavailableReason);
            return Task.CompletedTask;
        }

        try
        {
            var keyManager = services.GetRequiredService<IKeyManager>();
            if (!keyManager.GetAllKeys().Any())
            {
                keyManager.CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(KeyLifetimeDays));
            }

            var protector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector(ProbePurpose);
            var roundTrip = protector.Unprotect(protector.Protect(ProbePayload));

            if (roundTrip != ProbePayload)
            {
                throw new InvalidOperationException("Startup probe round trip returned an unexpected value.");
            }

            status.MarkAvailable();
            logger.LogInformation("Secret protection is available.");
        }
        catch (Exception exception)
        {
            const string reason = "The data protection key ring could not be loaded or used.";
            status.MarkUnavailable(reason);
            logger.LogError(exception, "Secret protection is unavailable: {Reason}", reason);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
