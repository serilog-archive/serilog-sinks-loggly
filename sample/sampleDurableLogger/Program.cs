using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Loggly;
using Loggly.Config;
using Serilog;
using Serilog.Core.Enrichers;
using Serilog.Enrichers;
using Serilog.Sinks.RollingFileAlternate;

namespace sampleDurableLogger
{
    public class Program
    {
        public static void Main(string[] args)
        {
            SetupLogglyConfiguration();
            var logger = CreateLoggerFactory(@"c:\test\").CreateLogger();

            logger.Information((Exception)null, "test message - app started");
            logger.Warning((Exception)null, "test message with {@data}", new { p1 = "sample", p2=DateTime.Now });
            logger.Warning((Exception)null, "test2 message with {@data}", new { p1 = "sample2", p2 = 10 });

            Console.WriteLine("disconnect to test offline. Two messages will be sent. Press enter to send and wait a minute or so before reconnecting or use breakpoints to see that send fails.");
            Console.ReadLine();

            logger.Information((Exception)null, "second test message - app started");
            logger.Warning((Exception)null, "second test message with {@data}", new { p1 = "sample2", p2 = DateTime.Now });


            Console.WriteLine("Offline messages written. Once you have confirmed that messages have been written locally, reconnect to see messages go out. Press Enter  for more messages to be written.");
            Console.ReadLine();

            logger.Information((Exception)null, "third test message - app started");
            logger.Warning((Exception)null, "third test message with {@data}", new { p1 = "sample3", p2 = DateTime.Now });

            Console.WriteLine("back online messages written. Check loggly and files for data. wait a minute or so before reconnecting. Press Enter to temrinate");
            Console.ReadLine();
        }

        public static LoggerConfiguration CreateLoggerFactory(string logFilePath)
        {
            return new LoggerConfiguration()
                .MinimumLevel.Debug()
                //Add enrichers
                .Enrich.FromLogContext()
                .Enrich.WithProcessId()
                .Enrich.WithThreadId()
                .Enrich.With(new EnvironmentUserNameEnricher())
                .Enrich.With(new MachineNameEnricher())
                .Enrich.With(new PropertyEnricher("Environment", GetLoggingEnvironment()))
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
                );
        }

        private static string GetLoggingEnvironment()
        {
            return "development";
        }

        private static void SetupLogglyConfiguration()
        {
            ///CHANGE THESE TOO TO YOUR LOGGLY ACCOUNT: DO NOT COMMIT TO Source control!!!
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
