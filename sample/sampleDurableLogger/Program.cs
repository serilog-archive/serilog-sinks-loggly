using System;
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
        public static void Main(string[] args)
        {
            SetupLogglyConfiguration();
            using (var logger = CreateLogger(@"c:\test\"))
            {
                logger.Information("Test message - app started");
                logger.Warning("Test message with {@Data}", new {P1 = "sample", P2 = DateTime.Now});
                logger.Warning("Test2 message with {@Data}", new {P1 = "sample2", P2 = 10});

                Console.WriteLine(
                    "Disconnect to test offline. Two messages will be sent. Press enter to send and wait a minute or so before reconnecting or use breakpoints to see that send fails.");
                Console.ReadLine();

                logger.Information("Second test message");
                logger.Warning("Second test message with {@Data}", new {P1 = "sample2", P2 = DateTime.Now});


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
                            bufferBaseFilename: logFilePath + "buffer")
                        .MinimumLevel.Information()
                )
                .WriteTo.Async(s => s.RollingFileAlternate(
                    logFilePath,
                    outputTemplate:
                        "[{ProcessId}] {Timestamp:yyyy-MM-dd HH:mm:ss.fff K} [{ThreadId}] [{Level}] [{SourceContext}] [{Category}] {Message}{NewLine}{Exception}",
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    retainedFileCountLimit: 100)
                        .MinimumLevel.Debug()
                )
                .CreateLogger();
        }

       
        static void SetupLogglyConfiguration()
        {
            ///CHANGE THESE TWO TO YOUR LOGGLY ACCOUNT: DO NOT COMMIT TO Source control!!!
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
    }
}
