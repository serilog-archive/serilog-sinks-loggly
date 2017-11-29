using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly LogIncludes _includes;

        public LogEventConverter(IFormatProvider formatProvider = null, LogIncludes includes = null)
        {
            _formatProvider = formatProvider;
            _includes = includes ?? new LogIncludes();
        }

        public LogglyEvent CreateLogglyEvent(LogEvent logEvent)
        {
            var logglyEvent = new LogglyEvent() { Timestamp = logEvent.Timestamp };

            var isHttpTransport = LogglyConfig.Instance.Transport.LogTransport == LogTransport.Https;
            logglyEvent.Syslog.Level = ToSyslogLevel(logEvent);
            
            if (_includes.IncludeMessage)
            {
                logglyEvent.Data.AddIfAbsent("Message", logEvent.RenderMessage(_formatProvider));
            }

            foreach (var key in logEvent.Properties.Keys)
            {
                var propertyValue = logEvent.Properties[key];
                var simpleValue = LogglyPropertyFormatter.Simplify(propertyValue, _formatProvider);
                logglyEvent.Data.AddIfAbsent(key, simpleValue);
            }

            if (isHttpTransport && _includes.IncludeLevel)
            {
                // syslog will capture these via the header
                logglyEvent.Data.AddIfAbsent("Level", logEvent.Level.ToString());
            }

            if (logEvent.Exception != null && _includes.IncludeExceptionWhenExists)
            {
                logglyEvent.Data.AddIfAbsent("Exception", GetExceptionInfo(logEvent.Exception));
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

        /// <summary>
        /// Returns a minification of the exception information. Also takes care of the InnerException(s).  
        /// </summary>
        /// <param name="exception"></param>
        /// <returns></returns>
        ExceptionDetails GetExceptionInfo(Exception exception)
        {
            ExceptionDetails exceptionInfo = new ExceptionDetails(
                exception.GetType().FullName,
                exception.Message, exception.StackTrace,
                GetInnerExceptions(exception));

            return exceptionInfo;
        }

        /// <summary>
        /// produces the collection of the inner-exceptions if exist
        /// Takes care of the differentiation between AggregateException and regualr exceptions
        /// </summary>
        /// <param name="exception"></param>
        /// <returns></returns>
        ExceptionDetails[] GetInnerExceptions(Exception exception)
        {
            IEnumerable<Exception> exceptions = Enumerable.Empty<Exception>();
            var ex = exception as AggregateException;
            if (ex != null)
            {
                var aggregateEx = ex;
                exceptions = aggregateEx.Flatten().InnerExceptions;
            }
            else if (exception.InnerException != null)
            {
                exceptions = new[] { exception.InnerException };
            }
            return exceptions.Select(GetExceptionInfo).ToArray();
        }
    }
}
