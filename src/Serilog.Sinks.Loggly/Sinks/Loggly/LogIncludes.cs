namespace Serilog.Sinks.Loggly
{
    public class LogIncludes
    {
        /// <summary>
        /// Adds Serilog Level to all log events. Defaults to true.
        /// </summary>
        public bool IncludeLevel { get; set; } = true;

        /// <summary>
        /// Adds Serilog Message to all log events. Defaults to true.
        /// </summary>
        public bool IncludeMessage { get; set; } = true;

        /// <summary>
        /// Adds Serilog Exception to log events when an exception exists. Defaults to true.
        /// </summary>
        public bool IncludeExceptionWhenExists { get; set; } = true;
    }
}