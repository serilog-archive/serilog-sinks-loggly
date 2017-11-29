// Serilog.Sinks.Seq Copyright 2016 Serilog Contributors
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
using System.IO;
using Newtonsoft.Json;
using Serilog.Events;
using Serilog.Formatting;

namespace Serilog.Sinks.Loggly
{
    /// <summary>
    ///  Formatter for the JSON schema accepted by Loggly's /bulk endpoint.
    /// </summary>
    class LogglyFormatter : ITextFormatter
    {
        readonly JsonSerializer _serializer = JsonSerializer.Create();
        readonly LogEventConverter _converter;

        public LogglyFormatter(IFormatProvider formatProvider, LogIncludes includes)
        {
            //the converter should receive the format provider used, in order to 
            // handle dateTimes and dateTimeOffsets in a controlled manner
            _converter = new LogEventConverter(formatProvider, includes);
        }
        public void Format(LogEvent logEvent, TextWriter output)
        {
            //Serializing the LogglyEvent means we can work with it from here on out and 
            // avoid the serialization / deserialization troubles serilog's logevent 
            // currently poses.
            _serializer.Serialize(output, _converter.CreateLogglyEvent(logEvent));
            output.WriteLine(); //adds the necessary linebreak
        }
    }
}
