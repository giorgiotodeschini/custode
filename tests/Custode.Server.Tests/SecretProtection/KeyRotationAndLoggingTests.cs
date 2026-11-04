// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

using Custode.Infrastructure.SecretProtection;
using Custode.Server.Tests.Hosting;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Custode.Server.Tests.SecretProtection;

public sealed class KeyRotationAndLoggingTests : IAsyncLifetime
{
    private const string Plaintext = "api-token-VERY-SECRET-42";

    private CustodeTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await CustodeTestHost.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [Fact]
    public void KeyRotation_KeepsExistingSecretsReadable()
    {
        var protector = _host.Services.GetRequiredService<ISecretProtector>();
        var tenantId = Guid.NewGuid();
        var beforeRotation = protector.Protect(tenantId, "before-rotation");

        var keyManager = _host.Services.GetRequiredService<IKeyManager>();
        var keysBefore = keyManager.GetAllKeys().Count();
        keyManager.CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90));

        var afterRotation = protector.Protect(tenantId, "after-rotation");

        Assert.Equal(keysBefore + 1, keyManager.GetAllKeys().Count());
        Assert.Equal("before-rotation", protector.Unprotect(tenantId, beforeRotation));
        Assert.Equal("after-rotation", protector.Unprotect(tenantId, afterRotation));
    }

    [Fact]
    public void SecretOperations_DoNotWritePlaintextToLogs()
    {
        var logs = new CapturingLoggerProvider();
        _host.Services.GetRequiredService<ILoggerFactory>().AddProvider(logs);
        var protector = _host.Services.GetRequiredService<ISecretProtector>();
        var tenantId = Guid.NewGuid();

        var ciphertext = protector.Protect(tenantId, Plaintext);
        protector.Unprotect(tenantId, ciphertext);

        Assert.DoesNotContain(logs.Messages, message => message.Contains(Plaintext));
    }

    [Fact]
    public async Task FailedSecretOperations_DoNotWritePlaintextToLogs()
    {
        var logs = new CapturingLoggerProvider();
        var missingPath = Path.Combine(Path.GetTempPath(), "custode-tests", "missing-for-logging.pfx");
        await using var host = await CustodeTestHost.StartAsync(sqliteFilePath: null, keyEncryptionCertificatePath: missingPath);
        host.Services.GetRequiredService<ILoggerFactory>().AddProvider(logs);
        var protector = host.Services.GetRequiredService<ISecretProtector>();

        Assert.Throws<SecretProtectionUnavailableException>(() => protector.Protect(Guid.NewGuid(), Plaintext));

        Assert.DoesNotContain(logs.Messages, message => message.Contains(Plaintext));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return _messages.ToList();
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private void Add(string message)
        {
            lock (_messages)
            {
                _messages.Add(message);
            }
        }

        private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                provider.Add(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
            }
        }
    }
}
