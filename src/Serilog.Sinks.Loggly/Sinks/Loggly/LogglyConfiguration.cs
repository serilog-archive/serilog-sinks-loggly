using System.Collections.Generic;

namespace Serilog.Sinks.Loggly
{
    public class LogglyConfiguration
    {
        public string ApplicationName { get; set; }
        public string CustomerToken { get; set; }
        public List<string> Tags { get; set; }
        public bool IsEnabled { get; set; } = true;
        public bool ThrowExceptions { get; set; }

        /// <summary>
        /// Defaults to Https
        /// </summary>
        public TransportProtocol LogTransport { get; set; }

        /// <summary>
        /// Defaults to logs-01.loggly.com
        /// </summary>
        public string EndpointHostName { get; set; }

        /// <summary>
        /// Defaults to default port for selected LogTransport.
        /// E.g. https is 443, SyslogTcp/-Udp is 514 and SyslogSecure is 6514.
        /// </summary>
        public int EndpointPort { get; set; }

        /// <summary>
        /// Defines if timestamp should automatically be added to the json body when using Https
        /// </summary>
        public bool OmitTimestamp { get; set; }
    }

    public enum TransportProtocol
    {
        /// <summary>
        /// Https.
        /// </summary>
        Https,

        /// <summary>
        /// SyslogSecure.
        /// </summary>
        SyslogSecure,

        /// <summary>
        /// SyslogUdp.
        /// </summary>
        SyslogUdp,

        /// <summary>
        /// SyslogTcp.
        /// </summary>
        SyslogTcp,
    }
}
