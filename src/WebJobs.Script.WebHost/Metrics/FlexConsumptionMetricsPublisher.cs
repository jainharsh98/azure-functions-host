// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics
{
    public class FlexConsumptionMetricsPublisher : IMetricsPublisher, IDisposable
    {
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptions;
        private readonly TimeSpan _metricPublishInterval;
        private readonly IEnvironment _environment;
        private readonly ILogger<FlexConsumptionMetricsPublisher> _logger;
        private readonly TimeSpan _initialPublishDelay;
        private readonly object _lock = new object();
        private readonly string _metricsFilePath;
        private readonly IFileSystem _fileSystem;
        private readonly IOptions<FlexConsumptionMetricsPublisherOptions> _options;

        private Timer _metricsPublisherTimer;
        private bool _initialized = false;
        private ValueStopwatch _stopwatch;
        private long _activeFunctionCount = 0;
        private long _functionExecutionCount = 0;
        private long _functionExecutionTimeMS = 0;
        private string _stampName;
        private IDisposable _standbyOptionsOnChangeSubscription;

        public FlexConsumptionMetricsPublisher(IEnvironment environment, IOptionsMonitor<StandbyOptions> standbyOptions, IOptions<FlexConsumptionMetricsPublisherOptions> options, ILogger<FlexConsumptionMetricsPublisher> logger, IFileSystem fileSystem)
        {
            _standbyOptions = standbyOptions ?? throw new ArgumentNullException(nameof(standbyOptions));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fileSystem = fileSystem ?? new FileSystem();

            _metricPublishInterval = TimeSpan.FromMilliseconds(_options.Value.MetricsPublishIntervalMS);
            _initialPublishDelay = TimeSpan.FromMilliseconds(_options.Value.InitialPublishDelayMS);
            _metricsFilePath = _options.Value.MetricsFilePath;

            if (string.IsNullOrEmpty(_metricsFilePath))
            {
                throw new ArgumentException($"{EnvironmentSettingNames.FunctionsnMetricsPublishPath} not configured.");
            }

            // TODO: configure correct cold start delay

            if (_standbyOptions.CurrentValue.InStandbyMode)
            {
                _standbyOptionsOnChangeSubscription = _standbyOptions.OnChange(o => OnStandbyOptionsChange());
            }
            else
            {
                Start();
            }
        }

        public void Start()
        {
            Initialize();

            _metricsPublisherTimer = new Timer(OnFunctionMetricsPublishTimer, null, _initialPublishDelay, _metricPublishInterval);

            _logger.LogInformation("Starting metrics publisher");
        }

        public void Initialize()
        {
            _stampName = _environment.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteHomeStampName);
            _initialized = true;
        }

        internal async void OnFunctionMetricsPublishTimer(object state)
        {
            if (_functionExecutionCount == 0 && _functionExecutionTimeMS == 0)
            {
                // no activity to report
                return;
            }

            // we've been accumulating function activity for the entire period
            // publish this activity and reset
            Metrics metrics = null;
            lock (_lock)
            {
                metrics = new Metrics
                {
                    FunctionExecutionCount = _functionExecutionCount,
                    FunctionExecutionTimeMS = _functionExecutionTimeMS
                };

                _functionExecutionTimeMS = _functionExecutionCount = 0;
            }

            await PublishMetricsAsync(metrics);
        }

        private async Task PublishMetricsAsync(Metrics metrics)
        {
            try
            {
                _fileSystem.Directory.CreateDirectory(_metricsFilePath);

                string content = JsonConvert.SerializeObject(metrics);
                string fileName = $"{Guid.NewGuid().ToString().ToLower()}.json";
                string filePath = Path.Combine(_metricsFilePath, fileName);

                using (var streamWriter = _fileSystem.File.CreateText(filePath))
                {
                    await streamWriter.WriteAsync(content);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing metrics file.");
            }
        }

        private void OnStandbyOptionsChange()
        {
            if (!_standbyOptions.CurrentValue.InStandbyMode)
            {
                Start();
            }
        }

        public void OnFunctionStarted(string functionName, string invocationId)
        {
            if (!_initialized)
            {
                return;
            }

            lock (_lock)
            {
                if (_activeFunctionCount == 0)
                {
                    // we're transitioning from inactive to active
                    _stopwatch = ValueStopwatch.StartNew();
                }

                _activeFunctionCount++;
            }
        }

        public void OnFunctionCompleted(string functionName, string invocationId)
        {
            if (!_initialized)
            {
                return;
            }

            lock (_lock)
            {
                if (_activeFunctionCount > 0)
                {
                    _activeFunctionCount--;
                }

                if (_activeFunctionCount == 0)
                {
                    // we're transitioning from active to inactive accumulate the elapsed time,
                    // applying the minimum interval
                    var elapsedMS = _stopwatch.GetElapsedTime().TotalMilliseconds;
                    var duration = Math.Max(elapsedMS, _options.Value.MinimumActivityIntervalMS);
                    _functionExecutionTimeMS += (long)duration;
                }

                // for every completed invocation, increment our invocation count
                _functionExecutionCount++;
            }
        }

        public void AddFunctionExecutionActivity(string functionName, string invocationId, int concurrency, string executionStage, bool success, long executionTimeSpan, string executionId, DateTime eventTimeStamp, DateTime functionStartTime)
        {
            // nothing to do here - we only care about Started/Completed events.
        }

        public void Dispose()
        {
            _metricsPublisherTimer?.Dispose();
            _metricsPublisherTimer = null;

            _standbyOptionsOnChangeSubscription?.Dispose();
            _standbyOptionsOnChangeSubscription = null;
        }

        internal class Metrics
        {
            public long FunctionExecutionTimeMS { get; set; }

            public long FunctionExecutionCount { get; set; }
        }
    }
}
