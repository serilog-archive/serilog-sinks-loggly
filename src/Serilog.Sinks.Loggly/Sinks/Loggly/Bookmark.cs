namespace Serilog.Sinks.Loggly
{
    public class Bookmark
    {
        public Bookmark(long position, string fileName)
        {
            Position = position;
            FileName = fileName;
        }

        public long Position { get; }
        public string FileName { get; }
    }
}