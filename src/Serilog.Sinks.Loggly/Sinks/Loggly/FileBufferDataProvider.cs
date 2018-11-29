﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Loggly;
using Newtonsoft.Json;
using Serilog.Debugging;
using Serilog.Sinks.Loggly.Durable;
#if HRESULTS
using System.Runtime.InteropServices;
#endif

namespace Serilog.Sinks.Loggly
{
    interface IBufferDataProvider
    {
        IEnumerable<LogglyEvent> GetNextBatchOfEvents();
        void MarkCurrentBatchAsProcessed();
        void MoveBookmarkForward();
    }

    /// <summary>
    /// Provides a facade to all File operations, namely bookmark management and 
    /// buffered data readings
    /// </summary>
    internal class FileBufferDataProvider : IBufferDataProvider
    {
#if HRESULTS
        //for Marshalling error checks
        const int ErrorSharingViolation = 32;
        const int ErrorLockViolation = 33;
#endif
        readonly string _candidateSearchPath;
        readonly string _logFolder;
        
        readonly int _batchPostingLimit;
        readonly long? _eventBodyLimitBytes;
        readonly int? _retainedFileCountLimit;

        readonly IFileSystemAdapter _fileSystemAdapter;
        readonly IBookmarkProvider _bookmarkProvider;
        readonly Encoding _encoding;

        readonly JsonSerializer _serializer = JsonSerializer.Create();

        // the following fields control the internal state and position of the queue
        protected FileSetPosition CurrentBookmark { get; set; }
        FileSetPosition _futureBookmark;
        IEnumerable<LogglyEvent> _currentBatchOfEventsToProcess;

        public FileBufferDataProvider(
            string baseBufferFileName, 
            IFileSystemAdapter fileSystemAdapter, 
            IBookmarkProvider bookmarkProvider, 
            Encoding encoding, 
            int batchPostingLimit, 
            long? eventBodyLimitBytes, 
            int? retainedFileCountLimit)
        {
            //construct a valid path to a file in the log folder to get the folder path:
            _logFolder = Path.GetDirectoryName(Path.GetFullPath(baseBufferFileName + ".bookmark"));
            _candidateSearchPath = Path.GetFileName(baseBufferFileName) + "*.json";

            _fileSystemAdapter = fileSystemAdapter;
            _bookmarkProvider = bookmarkProvider;
            _encoding = encoding;
            _batchPostingLimit = batchPostingLimit;
            _eventBodyLimitBytes = eventBodyLimitBytes;
            _retainedFileCountLimit = retainedFileCountLimit;
        }

        public IEnumerable<LogglyEvent> GetNextBatchOfEvents()
        {
            //if current batch has not yet been processed, return it
            if (_currentBatchOfEventsToProcess != null)
                return _currentBatchOfEventsToProcess;

            //if we have a bookmark in place, it may be the next position to read from
            // otherwise try to get a valid one
            if (CurrentBookmark == null)
            {
                //read the current bookmark from file, and if invalid, try to create a valid one
                CurrentBookmark = TryGetValidBookmark();

                if (!IsValidBookmark(CurrentBookmark))
                    return Enumerable.Empty<LogglyEvent>();
            }

            //bookmark is valid, so lets get the next batch from the files.
            RefreshCurrentListOfEvents();
            
            //this should never return null. If there is nothing to return, please return an empty list instead.
            return _currentBatchOfEventsToProcess ?? Enumerable.Empty<LogglyEvent>();
        }

        public void MarkCurrentBatchAsProcessed()
        {
            //reset internal state: only write to the bookmark file if we move forward.
            //otherwise, there is a risk of rereading the current (first) buffer file again
            if(_futureBookmark != null)
                _bookmarkProvider.UpdateBookmark(_futureBookmark);

            //we can move the marker to what's in "future" (next expected position)
            CurrentBookmark = _futureBookmark;
            _currentBatchOfEventsToProcess = null;
        }

        public void MoveBookmarkForward()
        {
            //curren Batch is empty, so we should clear it out so that the enxt read cycle will refresh it correctly
            _currentBatchOfEventsToProcess = null;

            // Only advance the bookmark if no other process has the
            // current file locked, and its length is as we found it.
            // NOTE: we will typically enter this method after any buffer file is finished
            // (no events read from previous file). This is the oportunity to clear out files
            // especially the prevously read file

            var fileSet = GetEventBufferFileSet();

            try
            {
                //if we only have two files, move to the next one imediately, unless a locking situation 
                // impeads us from doing so
                if (fileSet.Length == 2
                    && fileSet.First() == CurrentBookmark.File
                    && IsUnlockedAtLength(CurrentBookmark.File, CurrentBookmark.NextLineStart))
                {
                    //move to next file
                    CurrentBookmark = new FileSetPosition(0, fileSet[1]);
                    //we can also delete the previously read file since we no longer need it
                    _fileSystemAdapter.DeleteFile(fileSet[0]);
                }

                if (fileSet.Length > 2)
                {
                    //determine where in the fileset on disk is the current bookmark (if any)
                    //there is no garantee the the current one is the first, so we need to make
                    //sure we start reading the next one based on the current one
                    var currentBookMarkedFileInFileSet = CurrentBookmark != null
                        ? Array.FindIndex(fileSet, f => f == CurrentBookmark.File)
                        : -1;

                    //when we have more files, we want to delete older ones, but this depends on the
                    // limit retention policy. If no limit retention policy is in place, the intent is to 
                    // send all messages, no matter how old. In this case, we should only delete the current 
                    // file (since we are finished with it) and start the bookmark at the next one. If we do have some 
                    // retention policy in place, then delete anything older then the limit and the next 
                    // message read should be at the start of the policy limit
                    if (_retainedFileCountLimit.HasValue)
                    {
                        if (fileSet.Length <= _retainedFileCountLimit.Value)
                        {
                            //start 
                            var fileIndexToStartAt = Math.Max(0, currentBookMarkedFileInFileSet + 1);

                            //if index of current is not the last , use next; otherwise preserve current
                            CurrentBookmark = fileIndexToStartAt <= fileSet.Length - 1
                                ? new FileSetPosition(0, fileSet[fileIndexToStartAt])
                                : CurrentBookmark;

                            //delete all the old files
                            DeleteFilesInFileSetUpToIndex(fileSet, fileIndexToStartAt);
                        }
                        else
                        {
                            //start at next or first in retention count
                            var fileIndexToStartAt = Math.Max(fileSet.Length - _retainedFileCountLimit.Value, currentBookMarkedFileInFileSet +1); 

                            CurrentBookmark =new FileSetPosition(0, fileSet[fileIndexToStartAt]);

                            //delete all the old files
                            DeleteFilesInFileSetUpToIndex(fileSet, fileIndexToStartAt);
                        }
                    }
                    else
                    {
                        // not sure this can occur, but if for some reason the file is no longer in the list
                        // we should start from the beginning, maybe; being a bit defensive here due to 
                        // https://github.com/serilog/serilog-sinks-loggly/issues/25
                        if (currentBookMarkedFileInFileSet == -1)
                        {
                            //if not in file set, use first in set (or none)
                            CurrentBookmark = fileSet.Length > 0
                                ? new FileSetPosition(0, fileSet[0])
                                : null;
                        }
                        else
                        {
                            //if index of current is not the last , use next; otherwise preserve current
                            CurrentBookmark = currentBookMarkedFileInFileSet <= fileSet.Length - 2
                                ? new FileSetPosition(0, fileSet[currentBookMarkedFileInFileSet + 1])
                                : CurrentBookmark;
                        }

                        //also clear all the previous files in the set to avoid problems (and because
                        //they should no longer be considered); If no previous exists (index is -1);
                        //keep existing; also do not reomve last file as it may be written to / locked
                        DeleteFilesInFileSetUpToIndex(fileSet, currentBookMarkedFileInFileSet + 1);
                    }
                }
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("An error occured while deleteing the files...{0}", ex.Message);
            }
            finally
            {
                //even if reading / deleteing files fails, we can / should update the bookmark file
                //it is important that the file have the reset position, otherwise we risk failing to 
                // move forward in the next read cycle
                //it's possible that no bookmark exists, especially if no valid messages have forced a 
                // durable log file to be created. In this case, the bookmark file will be empty and 
                // on disk
                if(CurrentBookmark != null)
                    _bookmarkProvider.UpdateBookmark(CurrentBookmark);
            }
        }

        void DeleteFilesInFileSetUpToIndex(string[] fileSet, int fileIndexToDeleteUpTo)
        {
            for (int i = 0; i < fileIndexToDeleteUpTo && i < fileSet.Length - 1; i++)
            {
                _fileSystemAdapter.DeleteFile(fileSet[i]);
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
                //NOTE: this seems to be a way to check for file lock validations as in :
                // https://stackoverflow.com/questions/16642858/files-how-to-distinguish-file-lock-and-permission-denied-cases
                //sharing violation and LockViolation are expected, and we can follow trough if they occur

                var errorCode = Marshal.GetHRForException(ex) & ((1 << 16) - 1);
                if (errorCode != ErrorSharingViolation  && errorCode != ErrorLockViolation )
                {
                    SelfLog.WriteLine("Unexpected I/O exception while testing locked status of {0}: {1}", file, ex);
                }
            }
#else
            catch (IOException ex)
            {
                // Where no HRESULT is available, assume IOExceptions indicate a locked file
                SelfLog.WriteLine("Unexpected IOException while testing locked status of {0}: {1}", file, ex);
            }
#endif
            catch (Exception ex)
            {
                SelfLog.WriteLine("Unexpected exception while testing locked status of {0}: {1}", file, ex);
            }

            return false;
        }

        void RefreshCurrentListOfEvents()
        {
            var events = new List<LogglyEvent>();
            var count = 0;
            var positionTracker = CurrentBookmark.NextLineStart;

            using (var currentBufferStream = _fileSystemAdapter.Open(CurrentBookmark.File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                while (count < _batchPostingLimit && TryReadLine(currentBufferStream, ref positionTracker, out string readLine))
                {
                    // Count is the indicator that work was done, so advances even in the (rare) case an
                    // oversized event is dropped.
                    ++count;

                    if (_eventBodyLimitBytes.HasValue 
                        && readLine != null
                        && _encoding.GetByteCount(readLine) > _eventBodyLimitBytes.Value)
                    {
                        SelfLog.WriteLine(
                            "Event JSON representation exceeds the byte size limit of {0} and will be dropped; data: {1}",
                            _eventBodyLimitBytes, readLine);
                    }

                    if (!readLine.StartsWith("{"))
                    {
                        //in some instances this can happen. TryReadLine no longer assumes a BOM if reading from the file start, 
                        //but there may be (unobserved yet) situations where the line read is still not complete and valid 
                        // Json. This and the try catch that follows are, therefore, attempts to preserve the 
                        //logging functionality active, though some events may be dropped in the process.
                        SelfLog.WriteLine(
                            "Event JSON representation does not start with the expected '{{' character. " +
                            "This may be related to a BOM issue in the buffer file. Event will be dropped; data: {0}",
                            readLine);
                    }
                    else
                    {
                        try
                        {
                            events.Add(DeserializeEvent(readLine));
                        }
                        catch (Exception ex)
                        {
                            SelfLog.WriteLine(
                                "Unable to deserialize the json event; Event will be dropped; exception: {0};  data: {1}",
                                ex.Message, readLine);
                        }
                    }
                }
            }

            _futureBookmark = new FileSetPosition(positionTracker, CurrentBookmark.File);
            _currentBatchOfEventsToProcess = events;
        }

        // It would be ideal to chomp whitespace here, but not required.
        bool TryReadLine(Stream current, ref long nextStart, out string readLine)
        {
            // determine if we are reading the first line in the file. This will help with 
            // solving the BOM marker issue ahead
            var firstline = nextStart == 0;

            if (current.Length <= nextStart)
            {
                readLine = null;
                return false;
            }

            // Important not to dispose this StreamReader as the stream must remain open.
            using (var reader = new StreamReader(current, _encoding, false, 128, true))
            {
                // ByteOrder marker may still be a problem if we a reading the first line. We can test for it 
                // directly from the stream. This should only affect the first readline op, anyways. Since we 
                // If it's there, we need to move the start index by 3 bytes, so position will be correct throughout
                if (firstline && StreamContainsBomMarker(current))
                {
                    nextStart += 3;
                }

                //readline moves the marker forward farther then the line length, so it needs to be placed
                // at the right position. This makes sure we try to read a line from the right starting point
                current.Position = nextStart;
                readLine = reader.ReadLine();

                if (readLine == null)
                    return false;

                //If we have read the line, advance the count by the number of bytes + newline bytes to 
                //mark the start of the next line
                nextStart += _encoding.GetByteCount(readLine) + _encoding.GetByteCount(Environment.NewLine);

                return true;
            }
        }

        static bool StreamContainsBomMarker(Stream current)
        {
            bool isBom = false;
            long currentPosition = current.Position; //save to reset after BOM check

            byte[] potentialBomMarker = new byte[3];
            current.Position = 0;
            current.Read(potentialBomMarker, 0, 3);
            //BOM is "ef bb bf" =>  239  187 191
            if (potentialBomMarker[0] == 239
                && potentialBomMarker[1] == 187
                && potentialBomMarker[2] == 191)
            {
                isBom = true;
            }

            current.Position = currentPosition; //put position back where it was
            return isBom;
        }

        LogglyEvent DeserializeEvent(string eventLine)
        {
            return _serializer.Deserialize<LogglyEvent>(new JsonTextReader(new StringReader(eventLine)));
        }

        FileSetPosition TryGetValidBookmark()
        {
            //get from the bookmark file first;
            FileSetPosition newBookmark = _bookmarkProvider.GetCurrentBookmarkPosition();

            if (!IsValidBookmark(newBookmark))
            {
                newBookmark = CreateFreshBookmarkBasedOnBufferFiles();
            }

            return newBookmark;
        }

        FileSetPosition CreateFreshBookmarkBasedOnBufferFiles()
        {
            var fileSet = GetEventBufferFileSet();

            //the new bookmark should consider file retention rules, if any
            // if no retention rule is in place (send all data to loggly, no matter how old)
            // then take the first file and make a FileSetPosition out of it,
            // otherwise, make the position marker relative to the oldest file as in the rule
            //NOTE: this only happens when the previous bookmark is invalid (that's how we 
            // entered this method) so , if the prevous bookmark points to a valid file
            // that will continue to be read till the end.
            if (_retainedFileCountLimit.HasValue 
                && fileSet.Length > _retainedFileCountLimit.Value)
            {
                    //we have more files then our rule requires (older than needed)
                    // so point to the oldest allowed by our rule
                    return new FileSetPosition(0, fileSet.Skip(fileSet.Length - _retainedFileCountLimit.Value).First());
            }

            return fileSet.Any() ? new FileSetPosition(0, fileSet.First()) : null;
        }

        bool IsValidBookmark(FileSetPosition bookmark)
        {
            return bookmark?.File != null 
                   && _fileSystemAdapter.Exists(bookmark.File);
        }

        string[] GetEventBufferFileSet()
        {
            return _fileSystemAdapter.GetFiles(_logFolder, _candidateSearchPath)
                .OrderBy(name => name)
                .ToArray();
        }
    }
}