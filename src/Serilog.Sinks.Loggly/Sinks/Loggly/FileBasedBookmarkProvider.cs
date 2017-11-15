using System;
using System.IO;
using System.Text;
using Serilog.Debugging;
using Serilog.Sinks.Loggly.Durable;

namespace Serilog.Sinks.Loggly
{
    class FileBasedBookmarkProvider : IBookmarkProvider
    {
        readonly IFileSystemAdapter _fileSystemAdapter;
        readonly Encoding _encoding;

        readonly string _bookmarkFilename;
        Stream _currentBookmarkFileStream;

        public FileBasedBookmarkProvider(string bufferBaseFilename, IFileSystemAdapter fileSystemAdapter, Encoding encoding)
        {
            _bookmarkFilename = Path.GetFullPath(bufferBaseFilename + ".bookmark");
            _fileSystemAdapter = fileSystemAdapter;
            _encoding = encoding;
        }

        public void Dispose()
        {
            _currentBookmarkFileStream?.Dispose();
        }

        public FileSetPosition GetCurrentBookmarkPosition()
        {
            EnsureCurrentBookmarkStreamIsOpen();

            if (_currentBookmarkFileStream.Length != 0)
            {
                using (var bookmarkStreamReader = new StreamReader(_currentBookmarkFileStream, _encoding, false, 128, true))
                {
                    //set the position to 0, to begin reading the initial line
                    bookmarkStreamReader.BaseStream.Position = 0;
                    var bookmarkInfoLine = bookmarkStreamReader.ReadLine();

                    if (bookmarkInfoLine != null)
                    {
                        //reset position after read
                        var parts = bookmarkInfoLine.Split(new[] {":::"}, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2 && long.TryParse(parts[0], out long position))
                        {
                            return new FileSetPosition(position, parts[1]);
                        }

                        SelfLog.WriteLine("Unable to read a line correctly from bookmark file");
                    }
                    else
                    {
                        SelfLog.WriteLine(
                            "For some unknown reason, we were unable to read the non-empty bookmark info...");
                    }
                }
            }
            
            //bookmark file is empty or has been misread, so return a null bookmark
            return null;
        }

        public void UpdateBookmark(FileSetPosition newBookmark)
        {
            EnsureCurrentBookmarkStreamIsOpen();

            using (var bookmarkStreamWriter = new StreamWriter(_currentBookmarkFileStream, _encoding, 128, true))
            {
                bookmarkStreamWriter.BaseStream.Position = 0;
                bookmarkStreamWriter.WriteLine("{0}:::{1}", newBookmark.NextLineStart, newBookmark.File);
                bookmarkStreamWriter.Flush();
            }
        }

        void EnsureCurrentBookmarkStreamIsOpen()
        {
            //this will ensure a stream is available, even if it means creating a new file associated to it
            if (_currentBookmarkFileStream == null)
                _currentBookmarkFileStream = _fileSystemAdapter.Open(
                    _bookmarkFilename, 
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, 
                    FileShare.Read);
        }


    }
}