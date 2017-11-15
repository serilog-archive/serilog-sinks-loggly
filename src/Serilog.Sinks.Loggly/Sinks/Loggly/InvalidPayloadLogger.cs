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
using Serilog.Debugging;
using System.Collections.Generic;
using Loggly;
using Newtonsoft.Json;

namespace Serilog.Sinks.Loggly
{
    class InvalidPayloadLogger
    {
        const string InvalidPayloadFilePrefix = "invalid-";
        readonly string _logFolder;
        readonly long? _retainedInvalidPayloadsLimitBytes;
        readonly Encoding _encoding;
        readonly IFileSystemAdapter _fileSystemAdapter;
        readonly JsonSerializer _serializer = JsonSerializer.Create();
        

        public InvalidPayloadLogger(string logFolder, Encoding encoding, IFileSystemAdapter fileSystemAdapter, long? retainedInvalidPayloadsLimitBytes = null)
        {
            _logFolder = logFolder;
            _encoding = encoding;
            _fileSystemAdapter = fileSystemAdapter;
            _retainedInvalidPayloadsLimitBytes = retainedInvalidPayloadsLimitBytes;
        }

        public void DumpInvalidPayload(LogResponse result, IEnumerable<LogglyEvent> payload)
        {
            var invalidPayloadFilename = $"{InvalidPayloadFilePrefix}{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}-{result.Code}-{Guid.NewGuid():n}.json";
            var invalidPayloadFile = Path.Combine(_logFolder, invalidPayloadFilename);
            SelfLog.WriteLine("HTTP shipping failed with {0}: {1}; dumping payload to {2}", result.Code, result.Message, invalidPayloadFile);

            byte[] bytesToWrite = SerializeLogglyEventsToBytes(payload);
            
            if (_retainedInvalidPayloadsLimitBytes.HasValue)
            {
                CleanUpInvalidPayloadFiles(_retainedInvalidPayloadsLimitBytes.Value - bytesToWrite.Length, _logFolder);
            }

            //Adding this to perist WHY the invalid payload existed
            // the library is not using these files to resend data, so format is not important.
            var errorBytes = _encoding.GetBytes(string.Format(@"Error info: HTTP shipping failed with {0}: {1}", result.Code, result.Message));
            _fileSystemAdapter.WriteAllBytes(invalidPayloadFile, bytesToWrite.Concat(errorBytes).ToArray());
        }

        byte[] SerializeLogglyEventsToBytes(IEnumerable<LogglyEvent> events)
        {
            SelfLog.WriteLine("Newline to use: {0}", Environment.NewLine.Length == 2 ? "rn":"n");
            using (StringWriter writer = new StringWriter() { NewLine = Environment.NewLine })
            {
                foreach (var logglyEvent in events)
                {
                    _serializer.Serialize(writer, logglyEvent);
                    writer.Write(Environment.NewLine);
                }

                SelfLog.WriteLine("serialized events: {0}", writer.ToString());

                byte[] bytes = _encoding.GetBytes(writer.ToString());
                SelfLog.WriteLine("encoded events ending: {0} {1}", bytes[bytes.Length-2], bytes[bytes.Length-1]);
                return _encoding.GetBytes(writer.ToString());
            }
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
    }
}

