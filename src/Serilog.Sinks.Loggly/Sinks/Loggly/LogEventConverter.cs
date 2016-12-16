using System;
using Loggly;
using SyslogLevel = Loggly.Transports.Syslog.Level;
using Loggly.Config;
using Serilog.Debugging;
using Serilog.Events;

namespace Serilog.Sinks.Loggly
{
    /// <summary>
    /// Converts Serilog's Log Event to loogly-csharp LogglyEvent
    /// method was in LogglySink originally
    /// </summary>
    public class LogEventConverter
    {
        readonly IFormatProvider _formatProvider;

        public LogEventConverter(IFormatProvider formatProvider = null)
        {
            _formatProvider = formatProvider;
        }

        public LogglyEvent CreateLogglyEvent(LogEvent logEvent)
        {
            var logglyEvent = new LogglyEvent() { Timestamp = logEvent.Timestamp };

            var isHttpTransport = LogglyConfig.Instance.Transport.LogTransport == LogTransport.Https;
            logglyEvent.Syslog.Level = ToSyslogLevel(logEvent);

            logglyEvent.Data.AddIfAbsent("Message", logEvent.RenderMessage(_formatProvider));

            foreach (var key in logEvent.Properties.Keys)
            {
                var propertyValue = logEvent.Properties[key];
                var simpleValue = LogglyPropertyFormatter.Simplify(propertyValue);
                logglyEvent.Data.AddIfAbsent(key, simpleValue);
            }

            if (isHttpTransport)
            {
                // syslog will capture these via the header
                logglyEvent.Data.AddIfAbsent("Level", logEvent.Level.ToString());
            }

            if (logEvent.Exception != null)
            {
                logglyEvent.Data.AddIfAbsent("Exception", logEvent.Exception);
            }
            return logglyEvent;
        }


        static SyslogLevel ToSyslogLevel(LogEvent logEvent)
        {
            SyslogLevel syslogLevel;
            // map the level to a syslog level in case that transport is used.
            switch (logEvent.Level)
            {
                case LogEventLevel.Verbose:
                case LogEventLevel.Debug:
                    syslogLevel = SyslogLevel.Notice;
                    break;
                case LogEventLevel.Information:
                    syslogLevel = SyslogLevel.Information;
                    break;
                case LogEventLevel.Warning:
                    syslogLevel = SyslogLevel.Warning;
                    break;
                case LogEventLevel.Error:
                case LogEventLevel.Fatal:
                    syslogLevel = SyslogLevel.Error;
                    break;
                default:
                    SelfLog.WriteLine("Unexpected logging level, writing to loggly as Information");
                    syslogLevel = SyslogLevel.Information;
                    break;
            }
            return syslogLevel;
        }
    }
}
