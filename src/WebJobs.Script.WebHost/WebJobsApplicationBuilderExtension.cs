// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
//using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

//using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class WebJobsApplicationBuilderExtension
    {
        // Add these class level variables
        //private static Timer _placeholderTimer;
        //private static IEnvironment _environment;

        public static IApplicationBuilder UseWebJobsScriptHost(this IApplicationBuilder builder, IApplicationLifetime applicationLifetime)
        {
            return UseWebJobsScriptHost(builder, applicationLifetime, null);
        }

        public static IApplicationBuilder UseWebJobsScriptHost(this IApplicationBuilder builder, IApplicationLifetime applicationLifetime, Action<WebJobsRouteBuilder> routes)
        {
            IEnvironment environment = builder.ApplicationServices.GetService<IEnvironment>() ?? SystemEnvironment.Instance;
            IOptionsMonitor<StandbyOptions> standbyOptionsMonitor = builder.ApplicationServices.GetService<IOptionsMonitor<StandbyOptions>>();
            IOptionsMonitor<HttpBodyControlOptions> httpBodyControlOptionsMonitor = builder.ApplicationServices.GetService<IOptionsMonitor<HttpBodyControlOptions>>();
            IServiceProvider serviceProvider = builder.ApplicationServices;

            //_environment = environment;
            //_placeholderTimer = new Timer(OnTimedEvent, null, 60000, 60000);

            StandbyOptions standbyOptions = standbyOptionsMonitor.CurrentValue;
            standbyOptionsMonitor.OnChange(newOptions => standbyOptions = newOptions);

            HttpBodyControlOptions httpBodyControlOptions = httpBodyControlOptionsMonitor.CurrentValue;
            httpBodyControlOptionsMonitor.OnChange(newOptions => httpBodyControlOptions = newOptions);

            builder.UseMiddleware<HttpRequestBodySizeMiddleware>();
            builder.UseMiddleware<SystemTraceMiddleware>();
            builder.UseMiddleware<HostnameFixupMiddleware>();
            if (environment.IsLinuxConsumption())
            {
                builder.UseMiddleware<EnvironmentReadyCheckMiddleware>();
            }

            if (standbyOptions.InStandbyMode)
            {
                builder.UseMiddleware<PlaceholderSpecializationMiddleware>();
            }

            // Specialization can change the CompatMode setting, so this must run later than
            // the PlaceholderSpecializationMiddleware
            builder.UseWhen(context => httpBodyControlOptions.AllowSynchronousIO || context.Request.IsAdminDownloadRequest(), config =>
            {
                config.UseMiddleware<AllowSynchronousIOMiddleware>();
            });

            // This middleware must be registered before we establish the request service provider.
            builder.UseWhen(context => !context.Request.IsAdminRequest(), config =>
            {
                config.UseMiddleware<HostAvailabilityCheckMiddleware>();
            });

            builder.UseWhen(context => HostWarmupMiddleware.IsWarmUpRequest(context.Request, standbyOptions.InStandbyMode, environment), config =>
            {
                config.UseMiddleware<HostWarmupMiddleware>();
            });

            // This middleware must be registered before any other middleware depending on
            // JobHost/ScriptHost scoped services.
            builder.UseMiddleware<ScriptHostRequestServiceProviderMiddleware>();

            if (environment.IsLinuxAzureManagedHosting())
            {
                builder.UseMiddleware<AppServiceHeaderFixupMiddleware>();
            }

            builder.UseMiddleware<ExceptionMiddleware>();
            builder.UseWhen(HomepageMiddleware.IsHomepageRequest, config =>
            {
                config.UseMiddleware<HomepageMiddleware>();
            });
            builder.UseWhen(context => !context.Request.IsAdminRequest() && HttpThrottleMiddleware.ShouldEnable(serviceProvider), config =>
            {
                config.UseMiddleware<HttpThrottleMiddleware>();
            });

            builder.UseMiddleware<JobHostPipelineMiddleware>();
            builder.UseMiddleware<FunctionInvocationMiddleware>();

            // Register /admin/vfs, and /admin/zip to the VirtualFileSystem middleware.
            builder.UseWhen(VirtualFileSystemMiddleware.IsVirtualFileSystemRequest, config => config.UseMiddleware<VirtualFileSystemMiddleware>());

            // MVC routes (routes defined by Controllers like HostController, FunctionsController, ... must be added before functions/proxy routes so they are matched first and can not be overridden by functions or proxy routes)
            // source here: https://github.com/aspnet/Mvc/blob/master/src/Microsoft.AspNetCore.Mvc.Core/Builder/MvcApplicationBuilderExtensions.cs
            builder.UseMvc();

            // Ensure the HTTP binding routing is registered after all middleware
            builder.UseHttpBindingRouting(applicationLifetime, routes);

            return builder;
        }

        // Implement a timer callback that updates the environment variables
        //private static void OnTimedEvent(object state)
        //{
        //    if (_environment.IsPlaceholderModeEnabled())
        //    {
        //        Console.WriteLine("Triggering specialization");
        //        _environment.SetEnvironmentVariable(AzureWebsitePlaceholderMode, "0");
        //        _environment.SetEnvironmentVariable(AzureWebsiteContainerReady, "true");
        //    }
        //}
    }
}