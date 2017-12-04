using System;
using System.IO;
using System.Linq;
using System.Threading;
using Loggly;
using Loggly.Config;
using Loggly.Transports.Syslog;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace Serilog.Sinks.Loggly.Tests
{
    public class ExceptionSerialization
    {
        [Fact]
        public void ReturnFalseGivenValueOf1()
        {
            var writer = new StringWriter();
            var formatter = new LogglyFormatter(null, null);
            try
            {
                ThrowException();
            }
            catch (Exception e)
            {
                var evt = new LogEvent(DateTimeOffset.UtcNow,
                    LogEventLevel.Error, e,
                    new MessageTemplate("Hello", Enumerable.Empty<MessageTemplateToken>()),
                    Enumerable.Empty<LogEventProperty>());
                formatter.Format(evt, writer);
            }

            var s = writer.ToString();
            Assert.NotEmpty(s);
        }

        [Fact]
        public void InnerExceptionsAreSerialized()
        {
            var converter = new LogEventConverter(null);
            Exception innerException = null;
            try
            {
                try
                {
                    ThrowException();

                }
                catch (Exception e)
                {
                    innerException = e;
                    throw new InvalidOperationException("Outer Exception Message",e);
                }
            }
            catch (InvalidOperationException e)
            {
                var evt = new LogEvent(DateTimeOffset.UtcNow,
                    LogEventLevel.Error, e,
                    new MessageTemplate("Hello", Enumerable.Empty<MessageTemplateToken>()),
                    Enumerable.Empty<LogEventProperty>());
                var logglyEvent = converter.CreateLogglyEvent(evt);

                var exceptionnDetails = logglyEvent.Data["Exception"] as ExceptionDetails;
                Assert.NotEmpty(exceptionnDetails.InnerExceptions);
                Assert.Equal(innerException.Message,exceptionnDetails.InnerExceptions[0].Message);
            }
        }

        [Fact]
        public void AggregateExceptionsAreSerialized()
        {
            var converter = new LogEventConverter(null);
            Exception innerException = null;
            try
            {
                try
                {
                    ThrowException();

                }
                catch (Exception e)
                {
                    innerException = e;
                    throw new AggregateException(e,e);
                }
            }
            catch (AggregateException e)
            {
                var evt = new LogEvent(DateTimeOffset.UtcNow,
                    LogEventLevel.Error, e,
                    new MessageTemplate("Hello", Enumerable.Empty<MessageTemplateToken>()),
                    Enumerable.Empty<LogEventProperty>());
                var logglyEvent = converter.CreateLogglyEvent(evt);

                var exceptionnDetails = logglyEvent.Data["Exception"] as ExceptionDetails;
                Assert.Equal(2,exceptionnDetails.InnerExceptions.Length);
                Assert.Equal(innerException.Message, exceptionnDetails.InnerExceptions[0].Message);
                Assert.Equal(innerException.Message, exceptionnDetails.InnerExceptions[1].Message);
            }

        }

        [Fact]
        public void LogglyClientSendException()
        {
            var config = LogglyConfig.Instance;
            config.CustomerToken = "83fe7674-f87d-473e-a8af-bbbbbbbbbbbb";
            config.ApplicationName = $"test";

            config.Transport.EndpointHostname = "logs-01.loggly.com" ;
            config.Transport.EndpointPort = 443;
            config.Transport.LogTransport = LogTransport.Https;

            var ct = new ApplicationNameTag { Formatter = "application-{0}" };
            config.TagConfig.Tags.Add(ct);
            var logglyClient = new LogglyClient();

            
            try
            {
                ThrowException();
            }
            catch (Exception e)
            {
                LogglyEvent logglyEvent = new LogglyEvent
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Syslog = {Level = Level.Emergency}
                };
                logglyEvent.Data.AddIfAbsent("Message", "xZx");
                logglyEvent.Data.AddIfAbsent("Level", "Error");
                logglyEvent.Data.AddIfAbsent("Exception", e);

                var res = logglyClient.Log(logglyEvent).Result;
                Assert.Equal(ResponseCode.Success, res.Code);
            }
            Thread.Sleep(5000);
        }

        private static void ThrowException()
        {
            throw new ArgumentOutOfRangeException("Hello", "xyz", "sdsd");
        }

        
    }
}
