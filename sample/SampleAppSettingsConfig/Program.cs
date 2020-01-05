using System;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Context;
using Serilog.Core;

namespace SampleAppSettingsConfig
{
    public class Program
    {
        public static void Main()
        {
            //SetupLogglyConfiguration();
            using (var logger = CreateLogger(@"C:\test\Logs\"))
            {
                //The messages being used here are the same as the set in the SampleDurableLoggle project
                logger.Information("Test message - app started");

                logger.Warning("Test message with {@Data}", new { P1 = "sample", P2 = DateTime.Now });
                logger.Warning("Test2 message with {@Data}", new { P1 = "sample2", P2 = 10 });
                logger.Information("Second test message");
                logger.Warning("Second test message with {@Data}", new { P1 = "sample2", P2 = DateTime.Now, P3 = DateTime.UtcNow, P4 = DateTimeOffset.Now, P5 = DateTimeOffset.UtcNow });
                logger.Information("Third test message");
                logger.Warning("Third test message with {@Data}", new { P1 = "sample3", P2 = DateTime.Now });
                using (LogContext.PushProperty("sampleProperty", "Sample Value"))
                {
                    logger.Information("message to send with {@Data}", new { P1 = "sample4", P2 = DateTime.Now });
                }
                Console.WriteLine(
                    "Pushed property added to object. Check loggly and data. Press Enter to terminate");
                Console.ReadLine();
            }
        }

        static Logger CreateLogger(string logFilePath)
        {
            //write selflog to stderr
            Serilog.Debugging.SelfLog.Enable(Console.Error);

            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            return new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .MinimumLevel.Debug()
                .CreateLogger();
        }
    }
}
