namespace Serilog.Sinks.Loggly.Durable
{
    public class FileSetPosition
    {
        public FileSetPosition(long position, string fileFullPath)
        {
            NextLineStart = position;
            File = fileFullPath;
        }

        public long NextLineStart { get; }
        public string File { get; }

        public static readonly FileSetPosition None = default(FileSetPosition);
    }
}