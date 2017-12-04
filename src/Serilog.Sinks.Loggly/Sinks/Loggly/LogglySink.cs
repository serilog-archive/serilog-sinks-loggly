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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Loggly;
using Serilog.Events;
using Serilog.Sinks.PeriodicBatching;

namespace Serilog.Sinks.Loggly
{
    /// <summary>
    /// Writes log events to the Loggly.com service.
    /// </summary>
    public class LogglySink : PeriodicBatchingSink
    {
        readonly LogEventConverter _converter;
        readonly LogglyClient _client;
        readonly LogglyConfigAdapter _adapter;

        /// <summary>
        /// A reasonable default for the number of events posted in
        /// each batch.
        /// </summary>
        public const int DefaultBatchPostingLimit = 10;

        /// <summary>
        /// A reasonable default time to wait between checking for event batches.
        /// </summary>
        public static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Construct a sink that saves logs to the specified storage account. Properties are being send as data and the level is used as tag.
        /// </summary>
        /// <param name="batchSizeLimit">The maximum number of events to post in a single batch.</param>
        /// <param name="period">The time to wait between checking for event batches.</param>
        ///  <param name="formatProvider">Supplies culture-specific formatting information, or null.</param>        
        public LogglySink(IFormatProvider formatProvider, int batchSizeLimit, TimeSpan period) : this(formatProvider, batchSizeLimit, period, null, null)
        {            
        }

        /// <summary>
        /// Construct a sink that saves logs to the specified storage account. Properties are being send as data and the level is used as tag.
        /// </summary>
        /// <param name="batchSizeLimit">The maximum number of events to post in a single batch.</param>
        /// <param name="period">The time to wait between checking for event batches.</param>
        ///  <param name="formatProvider">Supplies culture-specific formatting information, or null.</param>
        /// <param name="logglyConfig">Used to configure underlying LogglyClient programmaticaly. Otherwise use app.Config.</param>
        /// <param name="includes">Decides if the sink should include specific properties in the log message</param>
        public LogglySink(IFormatProvider formatProvider, int batchSizeLimit, TimeSpan period, LogglyConfiguration logglyConfig, LogIncludes includes)
            : base (batchSizeLimit, period)
        {
            if (logglyConfig != null)
            {
                _adapter = new LogglyConfigAdapter();
                _adapter.ConfigureLogglyClient(logglyConfig);
            }
            _client = new LogglyClient();
            _converter = new LogEventConverter(formatProvider, includes);
        }

        /// <summary>
        /// Emit a batch of log events, running asynchronously.
        /// </summary>
        /// <param name="events">The events to emit.</param>
        /// <remarks>Override either <see cref="PeriodicBatchingSink.EmitBatch"/> or <see cref="PeriodicBatchingSink.EmitBatchAsync"/>,
        /// not both.</remarks>
        protected override async Task EmitBatchAsync(IEnumerable<LogEvent> events)
        {
            await _client.Log(events.Select(_converter.CreateLogglyEvent));
        }
        
    }
}
