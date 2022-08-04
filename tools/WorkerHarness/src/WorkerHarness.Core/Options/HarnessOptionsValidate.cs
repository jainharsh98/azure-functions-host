﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace WorkerHarness.Core.Options
{
    public class HarnessOptionsValidate : IHarnessOptionsValidate
    {
        private readonly ILogger<HarnessOptionsValidate> _logger;

        public HarnessOptionsValidate(ILogger<HarnessOptionsValidate> logger)
        {
            _logger = logger;
        }

        public bool Validate(HarnessOptions harnessOptions)
        {
            bool valid = true;
            string errorMessage = "Invalid or missing --{0} argument";

            // validate scenario file
            ValidateScenarioFile(harnessOptions, ref valid, errorMessage);

            // validate worker executable
            ValidateWorkerPath(harnessOptions, ref valid, errorMessage);

            // validate language executable
            ValidateLanguageExecutable(harnessOptions, ref valid, errorMessage);

            // set worker directory
            harnessOptions.WorkerDirectory = Path.GetDirectoryName(harnessOptions.WorkerPath);

            return valid;
        }


        private void ValidateLanguageExecutable(HarnessOptions harnessOptions, ref bool valid, string errorMessage)
        {
            if (string.IsNullOrEmpty(harnessOptions.LanguageExecutable))
            {
                _logger.LogError(errorMessage, "languageExecutable");
                valid = false;
            }
            else
            {
                harnessOptions.LanguageExecutable = Path.GetFullPath(harnessOptions.LanguageExecutable);

                if (!File.Exists(harnessOptions.LanguageExecutable))
                {
                    _logger.LogError(errorMessage, "languageExecutable");
                    valid = false;
                }
            }
        }

        private void ValidateWorkerPath(HarnessOptions harnessOptions, ref bool valid, string errorMessage)
        {
            if (string.IsNullOrEmpty(harnessOptions.WorkerPath))
            {
                _logger.LogError(errorMessage, "workerPath");
                valid = false;
            }
            else
            {
                harnessOptions.WorkerPath = Path.GetFullPath(harnessOptions.WorkerPath);

                if (!File.Exists(harnessOptions.WorkerPath))
                {
                    _logger.LogError(errorMessage, "workerPath");
                    valid = false;
                }
            }
        }

        private void ValidateScenarioFile(HarnessOptions harnessOptions, ref bool valid, string errorMessage)
        {
            if (string.IsNullOrEmpty(harnessOptions.ScenarioFile))
            {
                _logger.LogError(errorMessage, "scenarioFile");
                valid = false;
            }
            else
            {
                harnessOptions.ScenarioFile = Path.GetFullPath(harnessOptions.ScenarioFile);

                if (!File.Exists(harnessOptions.ScenarioFile))
                {
                    _logger.LogError(errorMessage, "scenarioFile");
                    valid = false;
                }
            }
        }
    }
}