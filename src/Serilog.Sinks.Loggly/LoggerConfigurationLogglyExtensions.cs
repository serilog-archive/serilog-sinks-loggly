// Copyright 2014 Serilog Contributors
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.Loggly;

namespace Serilog
{
    /// <summary>
    /// Adds the WriteTo.Loggly() extension method to <see cref="LoggerConfiguration"/>.
    /// </summary>
    public static class LoggerConfigurationLogglyExtensions
    {
        /// <summary>
        /// Adds a sink that writes log events to the Loggly.com webservice. Properties are being send as data and the level is used as category.
        /// </summary>
        /// <param name="loggerConfiguration">The logger configuration.</param>
        /// <param name="restrictedToMinimumLevel">The minimum log event level required in order to write an event to the sink.</param>
        /// <param name="formatProvider">Supplies culture-specific formatting information, or null.</param>
        /// <param name="batchPostingLimit">The maximum number of events to post in a single batch.</param>
        /// <param name="period">The time to wait between checking for event batches.</param>
        /// <param name="bufferBaseFilename">Path for a set of files that will be used to buffer events until they
        /// can be successfully transmitted across the network. Individual files will be created using the
        /// pattern <paramref name="bufferBaseFilename"/>-{Date}.json.</param>
        /// <param name="bufferFileSizeLimitBytes">The maximum size, in bytes, to which the buffer
        /// log file for a specific date will be allowed to grow. By default no limit will be applied.</param>
        /// <param name="eventBodyLimitBytes">The maximum size, in bytes, that the JSON representation of
        /// an event may take before it is dropped rather than being sent to the Loggly server. Specify null for no limit.
        /// The default is 1 MB.</param>
        /// <param name="controlLevelSwitch">If provided, the switch will be updated based on the Seq server's level setting
        /// for the corresponding API key. Passing the same key to MinimumLevel.ControlledBy() will make the whole pipeline
        /// dynamically controlled. Do not specify <paramref name="restrictedToMinimumLevel"/> with this setting.</param>
        /// <param name="retainedInvalidPayloadsLimitBytes">A soft limit for the number of bytes to use for storing failed requests.  
        /// The limit is soft in that it can be exceeded by any single error payload, but in that case only that single error
        /// payload will be retained.</param>
        /// <param name="retainedFileCountLimit">number of files to retain for the buffer. If defined, this also controls which records 
        /// <param name="logglyConfig">Used to configure underlying LogglyClient programmaticaly. Otherwise use app.Config.</param>
        /// <param name="includes">Decides if the sink should include specific properties in the log message</param>
        /// in the buffer get sent to the remote Loggly instance</param>
        /// <returns>Logger configuration, allowing configuration to continue.</returns>
        /// <exception cref="ArgumentNullException">A required parameter is null.</exception>
        public static LoggerConfiguration Loggly(
            this LoggerSinkConfiguration loggerConfiguration,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            int batchPostingLimit = LogglySink.DefaultBatchPostingLimit,
            TimeSpan? period = null,
            IFormatProvider formatProvider = null,
            string bufferBaseFilename = null,
            long? bufferFileSizeLimitBytes = null,
            long? eventBodyLimitBytes = 1024 * 1024,
            LoggingLevelSwitch controlLevelSwitch = null,
            long? retainedInvalidPayloadsLimitBytes = null,
            int? retainedFileCountLimit = null,
            LogglyConfiguration logglyConfig = null,
            LogIncludes includes = null)
        {
            if (loggerConfiguration == null) throw new ArgumentNullException(nameof(loggerConfiguration));
            if (bufferFileSizeLimitBytes.HasValue && bufferFileSizeLimitBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(bufferFileSizeLimitBytes), "Negative value provided; file size limit must be non-negative.");

            var defaultedPeriod = period ?? LogglySink.DefaultPeriod;

            ILogEventSink sink;

            if (bufferBaseFilename == null)
            {
                sink = new LogglySink(formatProvider, batchPostingLimit, defaultedPeriod, logglyConfig, includes);
            }
            else
            {
                sink = new DurableLogglySink(
                    bufferBaseFilename,
                    batchPostingLimit,
                    defaultedPeriod,
                    bufferFileSizeLimitBytes,
                    eventBodyLimitBytes,
                    controlLevelSwitch,
                    retainedInvalidPayloadsLimitBytes, 
                    retainedFileCountLimit,
                    formatProvider,
                    logglyConfig,
                    includes);
            }

            return loggerConfiguration.Sink(sink, restrictedToMinimumLevel);
        }
    }
}
