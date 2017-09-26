using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Loggly;
using Newtonsoft.Json;
using Serilog.Debugging;

namespace Serilog.Sinks.Loggly
{
    interface IBufferDataProvider
    {
        IEnumerable<LogglyEvent> GetBatchOfEvents();
        void MarkCurrentBatchAsProcessed();
        void MoveBookmarkFoward();
    }

    /// <summary>
    /// Provides a facade to all File operations, namely bookmark management and 
    /// buffered data readings
    /// </summary>
    class FileBufferDataProvider : IBufferDataProvider
    {
        readonly string _candidateSearchPath;
        readonly string _logFolder;
        readonly string _bookmarkFilename;

        readonly int _batchPostingLimit;
        readonly long? _eventBodyLimitBytes;

        readonly IFileSystemAdapter _fileSystemAdapter;
        readonly IBookmarkProvider _bookmarkProvider;
        readonly Encoding _encoding;

        readonly JsonSerializer _serializer = JsonSerializer.Create();

        // the following fields control the internal state and position of the queue
        Bookmark _currentBookmark;
        Bookmark _futureBookmark;
        IEnumerable<LogglyEvent> _currentBatchOfEventsToProcess;

        public FileBufferDataProvider(
            string baseBufferFileName, 
            IFileSystemAdapter fileSystemAdapter, 
            IBookmarkProvider bookmarkProvider,
            Encoding encoding,
            int batchPostingLimit,
            long? eventBodyLimitBytes
        )
        {
            _bookmarkFilename = Path.GetFullPath(baseBufferFileName + ".bookmark");
            _logFolder = Path.GetDirectoryName(_bookmarkFilename);
            _candidateSearchPath = Path.GetFileName(baseBufferFileName) + "*.json";

            _fileSystemAdapter = fileSystemAdapter;
            _bookmarkProvider = bookmarkProvider;
            _encoding = encoding;
            _batchPostingLimit = batchPostingLimit;
            _eventBodyLimitBytes = eventBodyLimitBytes;
        }

        public IEnumerable<LogglyEvent> GetBatchOfEvents()
        {
            //if current batch has not yet been processed, return it
            if (_currentBatchOfEventsToProcess != null)
                return _currentBatchOfEventsToProcess;

            //if we have a bookmark in place, it may be the next position to read from
            // otherwise try to get a valid one
            if (_currentBookmark == null)
            {
                //read the current bookmark from file, and if invalid, try to create a valid one
                _currentBookmark = TryGetValidBookmark();

                if (!IsValidBookmark(_currentBookmark))
                    return new List<LogglyEvent>();
            }

            //bookmark is valid, so lets get the next batch from the files.
            _currentBatchOfEventsToProcess = GetListOfEvents(_currentBookmark.FileName);
            //_futureBookmark = new Bookmark(nextByteCount, _currentBookmark.FileName); //performed in futureBookmark
            return _currentBatchOfEventsToProcess;
        }

        public void MarkCurrentBatchAsProcessed()
        {
            //reset internal state: 
            if(_futureBookmark != null)
                _bookmarkProvider.UpdateBookmark(_futureBookmark);

            //we can move the marker to what's in "future" (next expected position)
            _currentBookmark = _futureBookmark;
            _currentBatchOfEventsToProcess = null;
        }

        public void MoveBookmarkFoward()
        {
            // Only advance the bookmark if no other process has the
            // current file locked, and its length is as we found it.
            var fileSet = GetEventBufferFileSet();
            if (fileSet.Length == 2 
                && fileSet.First() == _currentBookmark.FileName 
                && IsUnlockedAtLength(_currentBookmark.FileName, _currentBookmark.Position))
            {
                //move to next file
                _bookmarkProvider.UpdateBookmark(new Bookmark(0, fileSet[1]));
            }

            if (fileSet.Length > 2)
            {
                // Once there's a third file waiting to ship, we do our
                // best to move on, though a lock on the current file
                // will delay this.
                _fileSystemAdapter.DeleteFile(fileSet[0]);
            }
        }

        bool IsUnlockedAtLength(string file, long maxLen)
        {
            try
            {
                using (var fileStream = _fileSystemAdapter.Open(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
                {
                    return fileStream.Length <= maxLen;
                }
            }
#if HRESULTS
            catch (IOException ex)
            {
                var errorCode = Marshal.GetHRForException(ex) & ((1 << 16) - 1);
                if (errorCode != 32 && errorCode != 33)
                {
                    SelfLog.WriteLine("Unexpected I/O exception while testing locked status of {0}: {1}", file, ex);
                }
            }
#else
            catch (IOException)
            {
                // Where no HRESULT is available, assume IOExceptions indicate a locked file
            }
#endif
            catch (Exception ex)
            {
                SelfLog.WriteLine("Unexpected exception while testing locked status of {0}: {1}", file, ex);
            }

            return false;
        }

        List<LogglyEvent> GetListOfEvents(string currentFile)
        {
            var events = new List<LogglyEvent>();
            var count = 0;
            var positionTracker = _currentBookmark.Position;

            using (var current = _fileSystemAdapter.Open(currentFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                current.Position = positionTracker;

                while (count <= _batchPostingLimit && TryReadLine(current, ref positionTracker, out string nextLine))
                {
                    // Count is the indicator that work was done, so advances even in the (rare) case an
                    // oversized event is dropped.
                    ++count;

                    if (_eventBodyLimitBytes.HasValue 
                        && nextLine != null
                        && _encoding.GetByteCount(nextLine) > _eventBodyLimitBytes.Value)
                    {
                        SelfLog.WriteLine(
                            "Event JSON representation exceeds the byte size limit of {0} and will be dropped; data: {1}",
                            _eventBodyLimitBytes, nextLine);
                    }
                    if (!nextLine.StartsWith("{"))
                    {
                        //in some instances this can happen. TryReadLine no longer assumes a BOM if reading from the file start, 
                        //but there may be (unobserved yet) situations where the line read is still not complete and valid 
                        // Json. This and the try catch that follows are, therefore, attempts to preserve the 
                        //logging functionality active, though some events may be dropped in the process.
                        SelfLog.WriteLine(
                            "Event JSON representation does not start with the expected '{{' character. " +
                            "This may be related to a BOM issue in the buffer file. Event will be dropped; data: {0}",
                            nextLine);
                    }
                    else
                    {
                        try
                        {
                            events.Add(DeserializeEvent(nextLine));
                        }
                        catch (Exception ex)
                        {
                            SelfLog.WriteLine(
                                "Unable to deserialize the json event; Event will be dropped; exception: {0};  data: {1}",
                                ex.Message, nextLine);
                        }
                    }
                }
            }

            _futureBookmark = new Bookmark(positionTracker, _currentBookmark.FileName);
            return events;
        }

        // It would be ideal to chomp whitespace here, but not required.
        bool TryReadLine(Stream current, ref long nextStart, out string nextLine)
        {
            // determine if we are reading the first line in the file. This will help with 
            // solving the BOM marker issue ahead
            var firstline = nextStart == 0;

            if (current.Length <= nextStart)
            {
                nextLine = null;
                return false;
            }

            current.Position = nextStart;

            // Important not to dispose this StreamReader as the stream must remain open.
            var reader = new StreamReader(current, _encoding, false, 128);
            nextLine = reader.ReadLine();

            if (nextLine == null)
                return false;

            //If we have read the line, advance the count by the number of bytes + newline bytes to 
            //mark the start of the next line
            nextStart += _encoding.GetByteCount(nextLine) + _encoding.GetByteCount(Environment.NewLine);

            // ByteOrder marker may still be a problem if we a reading the first line. We can trim it from 
            // the read line. This should only affect the first line, anyways. Since we are not changing the
            // origin file, the previous count (nextStart) is still valid
            if (firstline && nextLine[0] == '\uFEFF') //includesBom
                nextLine = nextLine.Substring(1, nextLine.Length - 1);

            return true;
        }

        LogglyEvent DeserializeEvent(string eventLine)
        {
            return _serializer.Deserialize<LogglyEvent>(new JsonTextReader(new StringReader(eventLine)));
        }

        Bookmark TryGetValidBookmark()
        {
            //get from the bookmark file first;
            Bookmark newBookmark = _bookmarkProvider.GetCurrentBookmarkPosition();

            if (!IsValidBookmark(newBookmark))
            {
                newBookmark = CreateFreshBookmarkBasedOnBufferFiles();
            }

            return newBookmark;
        }

        Bookmark CreateFreshBookmarkBasedOnBufferFiles()
        {
            var fileSet = GetEventBufferFileSet();
            return fileSet.Any() ? new Bookmark(0, fileSet.First()) : null;
        }

        bool IsValidBookmark(Bookmark bookmark)
        {
            return bookmark?.FileName != null 
                   && _fileSystemAdapter.Exists(bookmark.FileName);
        }

        string[] GetEventBufferFileSet()
        {
            return _fileSystemAdapter.GetFiles(_logFolder, _candidateSearchPath)
                .OrderBy(name => name)
                .ToArray();
        }
    }
}