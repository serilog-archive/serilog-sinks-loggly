using System.IO;

namespace Serilog.Sinks.Loggly
{
    internal interface IFileSystemAdapter
    {
        //file ops
        bool Exists(string filePath);
        void DeleteFile(string filePath);
        Stream Open(string bookmarkFilename, FileMode openOrCreate, FileAccess readWrite, FileShare read);
        void WriteAllBytes(string filePath, byte[] bytesToWrite);

        //directory ops
        string[] GetFiles(string folder, string searchTerms);

    }
}