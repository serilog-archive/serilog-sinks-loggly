using System;
using System.Globalization;
using Loggly;
using Loggly.Config;
using Serilog;
using Serilog.Core;
using Serilog.Core.Enrichers;
using Serilog.Enrichers;
using Serilog.Sinks.RollingFileAlternate;

namespace SampleDurableLogger
{
    public class Program
    {
        public static void Main()
        {
            SetupLogglyConfiguration();
            using (var logger = CreateLogger(@"C:\test\Logs\"))
            {
                logger.Information("Test message - app started");
                logger.Warning("Test message with {@Data}", new {P1 = "sample", P2 = DateTime.Now});
                logger.Warning("Test2 message with {@Data}", new {P1 = "sample2", P2 = 10});

                Console.WriteLine(
                    "Disconnect to test offline. Two messages will be sent. Press enter to send and wait a minute or so before reconnecting or use breakpoints to see that send fails.");
                Console.ReadLine();

                logger.Information("Second test message");
                logger.Warning("Second test message with {@Data}", new {P1 = "sample2", P2 = DateTime.Now, P3 = DateTime.UtcNow, P4 = DateTimeOffset.Now, P5 = DateTimeOffset.UtcNow});


                Console.WriteLine(
                    "Offline messages written. Once you have confirmed that messages have been written locally, reconnect to see messages go out. Press Enter for more messages to be written.");
                Console.ReadLine();

                logger.Information("Third test message");
                logger.Warning("Third test message with {@Data}", new {P1 = "sample3", P2 = DateTime.Now});

                Console.WriteLine(
                    "Back online messages written. Check loggly and files for data. Wait a minute or so before reconnecting. Press Enter to terminate");
                Console.ReadLine();
            }
        }

        static Logger CreateLogger(string logFilePath)
        {
            //write selflog to stderr
            Serilog.Debugging.SelfLog.Enable(Console.Error);

            return new LoggerConfiguration()
                .MinimumLevel.Debug()
                //Add enrichers
                .Enrich.FromLogContext()
                .Enrich.WithProcessId()
                .Enrich.WithThreadId()
                .Enrich.With(new EnvironmentUserNameEnricher())
                .Enrich.With(new MachineNameEnricher())
                .Enrich.With(new PropertyEnricher("Environment", "development"))
                //Add sinks
                .WriteTo.Async(s => s.Loggly(
                            bufferBaseFilename: logFilePath + "buffer",
                            formatProvider: CreateLoggingCulture())
                        .MinimumLevel.Information()
                )
                .WriteTo.Async(s => s.RollingFileAlternate(
                    logFilePath,
                    outputTemplate:
                        "[{ProcessId}] {Timestamp} [{ThreadId}] [{Level}] [{SourceContext}] [{Category}] {Message}{NewLine}{Exception}",
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    retainedFileCountLimit: 100,
                    formatProvider: CreateLoggingCulture())
                        .MinimumLevel.Debug()
                )
                .CreateLogger();
        }

       
        static void SetupLogglyConfiguration()
        {
            //CHANGE THESE TWO TO YOUR LOGGLY ACCOUNT: DO NOT COMMIT TO Source control!!!
            const string appName = "AppNameHere";
            const string customerToken = "yourkeyhere";

            //Configure Loggly
            var config = LogglyConfig.Instance;
            config.CustomerToken = customerToken;
            config.ApplicationName = appName;
            config.Transport = new TransportConfiguration()
            {
                EndpointHostname = "logs-01.loggly.com",
                EndpointPort = 443,
                LogTransport = LogTransport.Https
            };
            config.ThrowExceptions = true;

            //Define Tags sent to Loggly
            config.TagConfig.Tags.AddRange(new ITag[]{
                new ApplicationNameTag {Formatter = "application-{0}"},
                new HostnameTag { Formatter = "host-{0}" }
            });
        }

        static CultureInfo CreateLoggingCulture()
        {
            var loggingCulture = new CultureInfo("");

            //with this DateTime and DateTimeOffset string representations will be sortable. By default, 
            // serialization without a culture or formater will use InvariantCulture. This may or may not be 
            // desirable, depending on the sorting needs you require or even the region your in. In this sample
            // the invariant culture is used as a base, but the DateTime format is changed to a specific representation.
            // Instead of the dd/MM/yyyy hh:mm:ss, we'll force yyyy-MM-dd HH:mm:ss.fff which is sortable and obtainable
            // by overriding ShortDatePattern and LongTimePattern.
            //
            //Do note that they don't include the TimeZone by default, so a datetime will not have the TZ
            // while a DateTimeOffset will in it's string representation. 
            // Both use the longTimePattern for time formatting, but including the time zone in the 
            // pattern will duplicate the TZ representation when using DateTimeOffset which serilog does
            // for the timestamp.
            //
            //If you do not require specific formats, this method will not be required. Just pass in null (the default) 
            // for IFormatProvider in the Loggly() sink configuration method. 
            loggingCulture.DateTimeFormat.ShortDatePattern = "yyyy-MM-dd";
            loggingCulture.DateTimeFormat.LongTimePattern = "HH:mm:ss.fff";

            return loggingCulture;
        }
    }
}
