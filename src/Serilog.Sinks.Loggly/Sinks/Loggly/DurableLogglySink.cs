// Serilog.Sinks.Seq Copyright 2016 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Text;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.RollingFile;

namespace Serilog.Sinks.Loggly
{
    class DurableLogglySink : ILogEventSink, IDisposable
    {
        readonly HttpLogShipper _shipper;
        readonly RollingFileSink _sink;

        public DurableLogglySink(
            string bufferBaseFilename,
            int batchPostingLimit,
            TimeSpan period,
            long? bufferFileSizeLimitBytes,
            long? eventBodyLimitBytes,
            LoggingLevelSwitch levelControlSwitch,
            long? retainedInvalidPayloadsLimitBytes,
            int? retainedFileCountLimit = null,
            IFormatProvider formatProvider = null
            )
        {
            if (bufferBaseFilename == null) throw new ArgumentNullException(nameof(bufferBaseFilename));

            //use a consistent UTF encoding with BOM so no confusion will exist when reading / deserializing
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier:false);

            //handles sending events to Loggly's API through LogglyClient and manages the pending list
            _shipper = new HttpLogShipper(
                bufferBaseFilename, 
                batchPostingLimit, 
                period, 
                eventBodyLimitBytes, 
                levelControlSwitch,
                retainedInvalidPayloadsLimitBytes,
                encoding);

            //writes events to the file to support connection recovery
            _sink = new RollingFileSink(
                bufferBaseFilename + "-{Date}.json",
                new LogglyFormatter(formatProvider), //serializes as LogglyEvent
                bufferFileSizeLimitBytes,
                retainedFileCountLimit,
                encoding);
        }

        public void Dispose()
        {
            _sink.Dispose();
            _shipper.Dispose();
        }

        public void Emit(LogEvent logEvent)
        {
            // This is a lagging indicator, but the network bandwidth usage benefits
            // are worth the ambiguity.
            if (_shipper.IsIncluded(logEvent))
            {
                _sink.Emit(logEvent);
            }
        }
    }
}