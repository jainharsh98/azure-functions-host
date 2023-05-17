// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class FlexConsumptionMetricsPublisherOptions
    {
        internal const int DefaultMetricsPublishIntervalMS = 5000;
        internal const int DefaultMinimumActivityIntervalMS = 100;

        public FlexConsumptionMetricsPublisherOptions()
        {
            MetricsPublishIntervalMS = DefaultMetricsPublishIntervalMS;
            MinimumActivityIntervalMS = DefaultMinimumActivityIntervalMS;
            InitialPublishDelayMS = Utility.ColdStartDelayMS;
            MetricsFilePath = Environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsnMetricsPublishPath);
        }

        public int MetricsPublishIntervalMS { get; set; }

        public int InitialPublishDelayMS { get; set; }

        public int MinimumActivityIntervalMS { get; set; }

        public string MetricsFilePath { get; set; }
    }
}
