using System;
using Loggly.Config;

namespace Serilog.Sinks.Loggly
{
    class LogglyConfigAdapter
    {
        public void ConfigureLogglyClient(LogglyConfiguration logglyConfiguration)
        {
            var config = LogglyConfig.Instance;

            if (!string.IsNullOrWhiteSpace(logglyConfiguration.ApplicationName))
                config.ApplicationName = logglyConfiguration.ApplicationName;

            if (string.IsNullOrWhiteSpace(logglyConfiguration.CustomerToken))
                throw new ArgumentNullException("CustomerToken", "CustomerToken is required");

            config.CustomerToken = logglyConfiguration.CustomerToken;
            config.IsEnabled = logglyConfiguration.IsEnabled;

            if (logglyConfiguration.Tags != null)
            {
                foreach (var tag in logglyConfiguration.Tags)
                {
                    config.TagConfig.Tags.Add(tag);
                }
            }

            config.ThrowExceptions = logglyConfiguration.ThrowExceptions;

            if (logglyConfiguration.LogTransport != TransportProtocol.Https)
                config.Transport.LogTransport = (LogTransport)Enum.Parse(typeof(LogTransport), logglyConfiguration.LogTransport.ToString());

            if (!string.IsNullOrWhiteSpace(logglyConfiguration.EndpointHostName))
                config.Transport.EndpointHostname = logglyConfiguration.EndpointHostName;

            if (logglyConfiguration.EndpointPort > 0 && logglyConfiguration.EndpointPort <= ushort.MaxValue)
                config.Transport.EndpointPort = logglyConfiguration.EndpointPort;

            config.Transport.IsOmitTimestamp = logglyConfiguration.OmitTimestamp;
            config.Transport = config.Transport.GetCoercedToValidConfig();
        }
    }
}
