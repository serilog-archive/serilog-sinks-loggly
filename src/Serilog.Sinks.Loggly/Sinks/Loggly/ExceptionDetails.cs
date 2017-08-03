namespace Serilog.Sinks.Loggly
{
    class ExceptionDetails
    {
        public string Type { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }
        public ExceptionDetails[] InnerExceptions { get; set; }
    }
}