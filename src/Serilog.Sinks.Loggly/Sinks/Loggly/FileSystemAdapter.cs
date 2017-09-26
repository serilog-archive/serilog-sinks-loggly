using System.IO;

namespace Serilog.Sinks.Loggly
{
    /// <summary>
    /// adapter to abstract away filesystem specific / coupled calls, especially using File and Directory
    /// </summary>
    internal class FileSystemAdapter : IFileSystemAdapter
    {
        public bool Exists(string filePath)
        {
            return System.IO.File.Exists(filePath);
        }

        public void DeleteFile(string filePath)
        {
            System.IO.File.Delete(filePath);
        }

        public Stream Open(string filePath, FileMode mode, FileAccess access, FileShare share)
        {
            return System.IO.File.Open(filePath, mode, access, share);
        }

        public void WriteAllBytes(string filePath, byte[] bytesToWrite)
        {
            System.IO.File.WriteAllBytes(filePath, bytesToWrite);
        }

        public string[] GetFiles(string path, string searchPattern)
        {
            return Directory.GetFiles(path, searchPattern);
        }
    }
}