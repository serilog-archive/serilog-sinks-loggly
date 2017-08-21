namespace Serilog.Sinks.Loggly
{
    class ExceptionDetails
    {
        public ExceptionDetails(string type,
            string message,
            string stackTrace,
            ExceptionDetails[] innerExceptions)
        {
            Type = type;
            Message = message;
            StackTrace = stackTrace;
            InnerExceptions = innerExceptions;
        }
        public string Type { get; }
        public string Message { get; }
        public string StackTrace { get; }
        public ExceptionDetails[] InnerExceptions { get; }
    }
}