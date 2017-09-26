// Serilog.Sinks.Seq Copyright 2016 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using IOFile = System.IO.File;
using System.Threading.Tasks;
using System.Collections.Generic;
using Loggly;
using Newtonsoft.Json;

#if HRESULTS
using System.Runtime.InteropServices;
#endif

namespace Serilog.Sinks.Loggly
{
    class HttpLogShipper : IDisposable
    {
        readonly JsonSerializer _serializer = JsonSerializer.Create();

        readonly int _batchPostingLimit;
        readonly long? _eventBodyLimitBytes;
        readonly string _bookmarkFilename;
        readonly string _logFolder;
        readonly string _candidateSearchPath;
        readonly ExponentialBackoffConnectionSchedule _connectionSchedule;
        readonly long? _retainedInvalidPayloadsLimitBytes;
        readonly Encoding _encoding;

        readonly object _stateLock = new object();
        readonly PortableTimer _timer;
        readonly ControlledLevelSwitch _controlledSwitch;
        volatile bool _unloading;

        readonly LogglyClient _logglyClient;

        public HttpLogShipper(
            string bufferBaseFilename,
            int batchPostingLimit,
            TimeSpan period,
            long? eventBodyLimitBytes,
            LoggingLevelSwitch levelControlSwitch,
            long? retainedInvalidPayloadsLimitBytes,
            Encoding encoding)
        {
            _batchPostingLimit = batchPostingLimit;
            _eventBodyLimitBytes = eventBodyLimitBytes;
            _controlledSwitch = new ControlledLevelSwitch(levelControlSwitch);
            _connectionSchedule = new ExponentialBackoffConnectionSchedule(period);
            _retainedInvalidPayloadsLimitBytes = retainedInvalidPayloadsLimitBytes;
            _encoding = encoding;

            _logglyClient = new LogglyClient(); //we'll use the loggly client instead of HTTP directly

            _bookmarkFilename = Path.GetFullPath(bufferBaseFilename + ".bookmark");
            _logFolder = Path.GetDirectoryName(_bookmarkFilename);
            _candidateSearchPath = Path.GetFileName(bufferBaseFilename) + "*.json";

            _timer = new PortableTimer(c => OnTick());
            SetTimer();
        }

        void CloseAndFlush()
        {
            lock (_stateLock)
            {
                if (_unloading)
                    return;

                _unloading = true;
            }

            _timer.Dispose();

            OnTick().GetAwaiter().GetResult();
        }

        public bool IsIncluded(LogEvent logEvent)
        {
            return _controlledSwitch.IsIncluded(logEvent);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            CloseAndFlush();
        }

        void SetTimer()
        {
            // Note, called under _stateLock
            _timer.Start(_connectionSchedule.NextInterval);
        }

        async Task OnTick()
        {
            LogEventLevel? minimumAcceptedLevel = LogEventLevel.Debug;

            try
            {
                // Locking the bookmark ensures that though there may be multiple instances of this
                // class running, only one will ship logs at a time.
                using (var bookmark = IOFile.Open(_bookmarkFilename, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
                {
                    using (var bookmarkStreamReader = new StreamReader(bookmark, _encoding, false, 128))
                    {
                        using (var bookmarkStreamWriter = new StreamWriter(bookmark))
                        {
                            int count;
                            do
                            {
                                count = 0;

                                long nextLineBeginsAtOffset;
                                string currentFile;

                                TryReadBookmark(bookmark, bookmarkStreamReader, out nextLineBeginsAtOffset, out currentFile);

                                var fileSet = GetFileSet();

                                if (currentFile == null || !IOFile.Exists(currentFile))
                                {
                                    nextLineBeginsAtOffset = 0;
                                    currentFile = fileSet.FirstOrDefault();
                                }

                                if (currentFile == null)
                                    continue;

                                //grab the list of pending LogglyEvents from the file
                                var payload = GetListOfEvents(currentFile, ref nextLineBeginsAtOffset, ref count);

                                if (count > 0)
                                {
                                    //send the loggly events through the bulk API
                                    var result = await _logglyClient.Log(payload).ConfigureAwait(false);
                                    if (result.Code == ResponseCode.Success)
                                    {
                                        _connectionSchedule.MarkSuccess();
                                        WriteBookmark(bookmarkStreamWriter, nextLineBeginsAtOffset, currentFile);
                                    }
                                    else if (result.Code == ResponseCode.Error)
                                    {
                                        // The connection attempt was successful - the payload we sent was the problem.
                                        _connectionSchedule.MarkSuccess();
                                        WriteBookmark(bookmarkStreamWriter, nextLineBeginsAtOffset, currentFile);
                                        DumpInvalidPayload(result, payload);
                                    }
                                    else
                                    {
                                        _connectionSchedule.MarkFailure();
                                        SelfLog.WriteLine("Received failed HTTP shipping result {0}: {1}", result.Code,
                                            result.Message);

                                        break;
                                    }
                                }
                                else
                                {
                                    // For whatever reason, there's nothing waiting to send. This means we should try connecting again at the
                                    // regular interval, so mark the attempt as successful.
                                    _connectionSchedule.MarkSuccess();

                                    // Only advance the bookmark if no other process has the
                                    // current file locked, and its length is as we found it.
                                    if (fileSet.Length == 2 && fileSet.First() == currentFile &&
                                        IsUnlockedAtLength(currentFile, nextLineBeginsAtOffset))
                                    {
                                        WriteBookmark(bookmarkStreamWriter, 0, fileSet[1]);
                                    }

                                    if (fileSet.Length > 2)
                                    {
                                        // Once there's a third file waiting to ship, we do our
                                        // best to move on, though a lock on the current file
                                        // will delay this.

                                        IOFile.Delete(fileSet[0]);
                                    }
                                }
                            } while (count == _batchPostingLimit);
                        }
                    } 
                }
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("Exception while emitting periodic batch from {0}: {1}", this, ex);
                _connectionSchedule.MarkFailure();
            }
            finally
            {
                lock (_stateLock)
                {
                    _controlledSwitch.Update(minimumAcceptedLevel);

                    if (!_unloading)
                        SetTimer();
                }
            }
        }

        const string InvalidPayloadFilePrefix = "invalid-";
        void DumpInvalidPayload(LogResponse result, IEnumerable<LogglyEvent> payload)
        {
            var invalidPayloadFilename = $"{InvalidPayloadFilePrefix}{result.Code}-{Guid.NewGuid():n}.json";
            var invalidPayloadFile = Path.Combine(_logFolder, invalidPayloadFilename);
            SelfLog.WriteLine("HTTP shipping failed with {0}: {1}; dumping payload to {2}", result.Code, result.Message, invalidPayloadFile);

            byte[] bytesToWrite;
            using (StringWriter writer = new StringWriter())
            {
                SerializeLogglyEventsToWriter(payload, writer);
                bytesToWrite = _encoding.GetBytes(writer.ToString());
            }

            if (_retainedInvalidPayloadsLimitBytes.HasValue)
            {
                CleanUpInvalidPayloadFiles(_retainedInvalidPayloadsLimitBytes.Value - bytesToWrite.Length, _logFolder);
            }
            IOFile.WriteAllBytes(invalidPayloadFile, bytesToWrite);
            //Adding this to perist WHY the invalid payload existed
            // the library is not using these files to resend data, so format is not important.
            IOFile.AppendAllText(invalidPayloadFile, string.Format(@"\n\n error info: HTTP shipping failed with {0}: {1}; dumping payload to {2}", result.Code, result.Message, invalidPayloadFile));
        }

        static void CleanUpInvalidPayloadFiles(long maxNumberOfBytesToRetain, string logFolder)
        {
            try
            {
                var candiateFiles = Directory.EnumerateFiles(logFolder, $"{InvalidPayloadFilePrefix}*.json");
                DeleteOldFiles(maxNumberOfBytesToRetain, candiateFiles);
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("Exception thrown while trying to clean up invalid payload files: {0}", ex);
            }
        }

        static IEnumerable<FileInfo> WhereCumulativeSizeGreaterThan(IEnumerable<FileInfo> files, long maxCumulativeSize)
        {
            long cumulative = 0;
            foreach (var file in files)
            {
                cumulative += file.Length;
                if (cumulative > maxCumulativeSize)
                {
                    yield return file;
                }
            }
        }

        /// <summary>
        /// Deletes oldest files in the group of invalid-* files. 
        /// Existing files are ordered (from most recent to oldest) and file size is acumulated. All files
        /// who's cumulative byte count passes the defined limit are removed. Limit is therefore bytes 
        /// and not number of files
        /// </summary>
        /// <param name="maxNumberOfBytesToRetain"></param>
        /// <param name="files"></param>
        static void DeleteOldFiles(long maxNumberOfBytesToRetain, IEnumerable<string> files)
        {
            var orderedFileInfos = from candidateFile in files
                                   let candidateFileInfo = new FileInfo(candidateFile)
                                   orderby candidateFileInfo.LastAccessTimeUtc descending
                                   select candidateFileInfo;

            var invalidPayloadFilesToDelete = WhereCumulativeSizeGreaterThan(orderedFileInfos, maxNumberOfBytesToRetain);

            foreach (var fileToDelete in invalidPayloadFilesToDelete)
            {
                try
                {
                    fileToDelete.Delete();
                }
                catch (Exception ex)
                {
                    SelfLog.WriteLine("Exception '{0}' thrown while trying to delete file {1}", ex.Message, fileToDelete.FullName);
                }
            }
        }

        List<LogglyEvent> GetListOfEvents(string currentFile, ref long nextLineBeginsAtOffset, ref int count)
        {
            var events = new List<LogglyEvent>();

            using (var current = IOFile.Open(currentFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                current.Position = nextLineBeginsAtOffset;

                string nextLine;
                while (count < _batchPostingLimit &&
                       TryReadLine(current, ref nextLineBeginsAtOffset, out nextLine))
                {
                    // Count is the indicator that work was done, so advances even in the (rare) case an
                    // oversized event is dropped.
                    ++count;

                    if (_eventBodyLimitBytes.HasValue && _encoding.GetByteCount(nextLine) > _eventBodyLimitBytes.Value)
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
                            "Event JSON representation does not start with the expected '{{' character. "+
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

            return events;
        }

        LogglyEvent DeserializeEvent(string eventLine)
        {
            return _serializer.Deserialize<LogglyEvent>(new JsonTextReader(new StringReader(eventLine)));
        }

        void SerializeLogglyEventsToWriter(IEnumerable<LogglyEvent> events, TextWriter writer)
        {
            foreach (var logglyEvent in events)
            {
                _serializer.Serialize(writer, logglyEvent);
                writer.WriteLine();
            }
        }

        static bool IsUnlockedAtLength(string file, long maxLen)
        {
            try
            {
                using (var fileStream = IOFile.Open(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
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

        static void WriteBookmark(StreamWriter bookmarkStreamWriter, long nextLineBeginsAtOffset, string currentFile)
        {
            bookmarkStreamWriter.WriteLine("{0}:::{1}", nextLineBeginsAtOffset, currentFile);
            bookmarkStreamWriter.Flush();
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

        static void TryReadBookmark(Stream bookmark, StreamReader bookmarkStreamReader, out long nextLineBeginsAtOffset,
            out string currentFile)
        {
            nextLineBeginsAtOffset = 0;
            currentFile = null;

            if (bookmark.Length != 0)
            {
                bookmarkStreamReader.BaseStream.Position = 0;
                var current = bookmarkStreamReader.ReadLine();

                if (current != null)
                {
                    bookmark.Position = 0;
                    var parts = current.Split(new[] {":::"}, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        nextLineBeginsAtOffset = long.Parse(parts[0]);
                        currentFile = parts[1];
                    }
                    else
                    {
                        SelfLog.WriteLine("Unable to read a line correctly from bookmark file");
                    }
                }
                else
                {
                    SelfLog.WriteLine(
                        "For some unknown reason, we were unable to read the non-empty bookmark info...");
                }
            }
            else
            {
                SelfLog.WriteLine("For some unknown reason, we were unable to read the bookmark stream!...");
            }
        }

        string[] GetFileSet()
        {
            return Directory.GetFiles(_logFolder, _candidateSearchPath)
                .OrderBy(n => n)
                .ToArray();
        }
    }
}

