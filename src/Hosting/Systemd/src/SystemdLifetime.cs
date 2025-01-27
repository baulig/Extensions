// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Hosting.Systemd
{
    public class SystemdLifetime : IHostLifetime, IDisposable
    {
        private readonly ManualResetEvent _shutdownBlock = new ManualResetEvent(false);
        private CancellationTokenRegistration _applicationStartedRegistration;
        private CancellationTokenRegistration _applicationStoppingRegistration;

        public SystemdLifetime(IHostEnvironment environment, IHostApplicationLifetime applicationLifetime, ISystemdNotifier systemdNotifier, ILoggerFactory loggerFactory)
        {
            Environment = environment ?? throw new ArgumentNullException(nameof(environment));
            ApplicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
            SystemdNotifier = systemdNotifier ?? throw new ArgumentNullException(nameof(systemdNotifier));
            Logger = loggerFactory.CreateLogger("Microsoft.Hosting.Lifetime");
        }

        private IHostApplicationLifetime ApplicationLifetime { get; }
        private IHostEnvironment Environment { get; }
        private ILogger Logger { get; }
        private ISystemdNotifier SystemdNotifier { get; }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task WaitForStartAsync(CancellationToken cancellationToken)
        {
            _applicationStartedRegistration = ApplicationLifetime.ApplicationStarted.Register(state =>
            {
                ((SystemdLifetime)state).OnApplicationStarted();
            },
            this);
            _applicationStoppingRegistration = ApplicationLifetime.ApplicationStopping.Register(state =>
            {
                ((SystemdLifetime)state).OnApplicationStopping();
            },
            this);

            // systemd sends SIGTERM to stop the service.
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            return Task.CompletedTask;
        }

        private void OnApplicationStarted()
        {
            Logger.LogInformation("Application started. Hosting environment: {envName}; Content root path: {contentRoot}",
                Environment.EnvironmentName, Environment.ContentRootPath);

            SystemdNotifier.Notify(ServiceState.Ready);
        }

        private void OnApplicationStopping()
        {
            Logger.LogInformation("Application is shutting down...");

            SystemdNotifier.Notify(ServiceState.Stopping);
        }

        private void OnProcessExit(object sender, EventArgs e)
        {
            ApplicationLifetime.StopApplication();

            _shutdownBlock.WaitOne();

            // On Linux if the shutdown is triggered by SIGTERM then that's signaled with the 143 exit code.
            // Suppress that since we shut down gracefully. https://github.com/aspnet/AspNetCore/issues/6526
            System.Environment.ExitCode = 0;
        }

        public void Dispose()
        {
            _shutdownBlock.Set();

            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

            _applicationStartedRegistration.Dispose();
            _applicationStoppingRegistration.Dispose();
        }
    }
}
