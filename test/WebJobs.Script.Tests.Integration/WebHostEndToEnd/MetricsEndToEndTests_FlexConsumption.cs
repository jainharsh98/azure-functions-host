// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    public class MetricsEndToEndTests_FlexConsumption : IClassFixture<MetricsEndToEndTests_FlexConsumption.TestFixture>
    {
        private readonly ScriptSettingsManager _settingsManager;
        private TestFixture _fixture;

        public MetricsEndToEndTests_FlexConsumption(TestFixture fixture)
        {
            _fixture = fixture;
            _settingsManager = ScriptSettingsManager.Instance;
        }

        [Fact]
        public async Task ShortHaulTest_ExpectedMetricsGenerated()
        {
            var vars = new Dictionary<string, string>
            {
                { RpcWorkerConstants.FunctionWorkerRuntimeSettingName, RpcWorkerConstants.DotNetLanguageWorkerName}
            };
            using (_fixture.Host.WebHostServices.CreateScopedEnvironment(vars))
            {
                _fixture.CleanupMetricsFiles();

                string functionKey = await _fixture.Host.GetFunctionSecretAsync("HttpTrigger");
                string uri = $"api/httptrigger?code={functionKey}&name=Mathew";

                int totalMilliseconds = 5000;
                int executionCount = 0;
                DateTime start = DateTime.Now;
                while ((DateTime.Now - start).TotalMilliseconds < totalMilliseconds)
                {
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri);
                    HttpResponseMessage response = await _fixture.Host.HttpClient.SendAsync(request);
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    executionCount++;

                    await Task.Delay(50);
                }

                var options = _fixture.Host.WebHostServices.GetService<IOptions<FlexConsumptionMetricsPublisherOptions>>();
                int expectedFileCount = (totalMilliseconds / options.Value.MetricsPublishIntervalMS) + 1;

                // wait for final file to be written
                await Task.Delay(1000);

                // verify metrics files were written with expected content
                var directory = new DirectoryInfo(_fixture.MetricsPublishPath);
                var files = directory.GetFiles();
                Assert.Equal(files.Length, expectedFileCount);

                long totalPublishedExecutionCount = 0;
                long totalPublishedExecutionTime = 0;
                foreach (var file in files)
                {
                    // parse the metrics
                    string contents = File.ReadAllText(file.FullName);
                    var metrics = JsonConvert.DeserializeObject<FlexConsumptionMetricsPublisher.Metrics>(contents);

                    // verify the execution time
                    Assert.Equal(metrics.FunctionExecutionCount * FlexConsumptionMetricsPublisherOptions.DefaultMinimumActivityIntervalMS, metrics.FunctionExecutionTimeMS);

                    totalPublishedExecutionCount += metrics.FunctionExecutionCount;
                    totalPublishedExecutionTime += metrics.FunctionExecutionTimeMS;
                }

                Assert.Equal(executionCount, totalPublishedExecutionCount);
                Assert.Equal(totalPublishedExecutionCount * FlexConsumptionMetricsPublisherOptions.DefaultMinimumActivityIntervalMS, totalPublishedExecutionTime);
            }
        }

        public class TestFixture : EndToEndTestFixture
        {
            public TestFixture()
                : base(Path.Combine(Environment.CurrentDirectory, @"..", "..", "..", "..", "..", "sample", "csharp"), "samples", RpcWorkerConstants.DotNetLanguageWorkerName)
            {
                MockWebHookProvider = new Mock<IScriptWebHookProvider>(MockBehavior.Strict);
            }

            public Mock<IScriptWebHookProvider> MockWebHookProvider { get; }

            public string MetricsPublishPath { get; private set; }

            public override void ConfigureWebHost(IServiceCollection services)
            {
                base.ConfigureWebHost(services);

                services.Configure<FlexConsumptionMetricsPublisherOptions>(options =>
                {
                    options.MetricsPublishIntervalMS = 1000;
                    options.InitialPublishDelayMS = 0;
                    options.MetricsFilePath = MetricsPublishPath;
                });

                MetricsPublishPath = Path.Combine(Path.GetTempPath(), "metrics");

                var environment = new TestEnvironment();
                string testSiteName = "somewebsite";
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagAllowSynchronousIO);
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, testSiteName);
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, "e777fde04dea4eb931d5e5f06e65b4fdf5b375aed60af41dd7b491cf5792e01b");
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AntaresPlatformVersionWindows, "89.0.7.73");
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AntaresComputerName, "RD281878FCB8E7");
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.FlexConsumptionSku);

                // have to set these statically here because some APIs in the host aren't going through IEnvironment
                string key = TestHelpers.GenerateKeyHexString();
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey, key);
                Environment.SetEnvironmentVariable("AzureWebEncryptionKey", key);
                Environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, testSiteName);

                services.AddSingleton<IEnvironment>(_ => environment);
            }

            public override void ConfigureScriptHost(IServiceCollection services)
            {
                base.ConfigureScriptHost(services);

                // the test host by default registers a mock, but we want the actual logger for these tests
                services.AddSingleton<IMetricsLogger, WebHostMetricsLogger>();
            }

            public override void ConfigureScriptHost(IWebJobsBuilder webJobsBuilder)
            {
                base.ConfigureScriptHost(webJobsBuilder);

                webJobsBuilder.Services.AddSingleton<IScriptWebHookProvider>(MockWebHookProvider.Object);

                webJobsBuilder.Services.Configure<ScriptJobHostOptions>(o =>
                {
                    o.Functions = new[]
                    {
                        "HttpTrigger"
                    };
                });
            }

            public void CleanupMetricsFiles()
            {
                var directory = new DirectoryInfo(MetricsPublishPath);
                foreach (var file in directory.GetFiles())
                {
                    file.Delete();
                }
            }

            public override async Task DisposeAsync()
            {
                await base.DisposeAsync();

                CleanupMetricsFiles();
            }
        }
    }
}
