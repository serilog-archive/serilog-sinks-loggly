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
using System.Threading.Tasks;
using Loggly;

#if HRESULTS
using System.Runtime.InteropServices;
#endif

namespace Serilog.Sinks.Loggly
{
    class HttpLogShipper : IDisposable
    {
        readonly int _batchPostingLimit;
        private readonly int? _retainedFileCountLimit;
        readonly ExponentialBackoffConnectionSchedule _connectionSchedule;

        readonly object _stateLock = new object();
        readonly PortableTimer _timer;
        readonly ControlledLevelSwitch _controlledSwitch;
        volatile bool _unloading;

        readonly LogglyClient _logglyClient;
        readonly IFileSystemAdapter _fileSystemAdapter = new FileSystemAdapter();
        readonly FileBufferDataProvider _bufferDataProvider;
        readonly InvalidPayloadLogger _invalidPayloadLogger;
        
        public HttpLogShipper(
            string bufferBaseFilename, 
            int batchPostingLimit, 
            TimeSpan period, long? 
            eventBodyLimitBytes, 
            LoggingLevelSwitch levelControlSwitch, 
            long? retainedInvalidPayloadsLimitBytes, 
            Encoding encoding, 
            int? retainedFileCountLimit)
        {
            _batchPostingLimit = batchPostingLimit;
            _retainedFileCountLimit = retainedFileCountLimit;

            _controlledSwitch = new ControlledLevelSwitch(levelControlSwitch);
            _connectionSchedule = new ExponentialBackoffConnectionSchedule(period);

            _logglyClient = new LogglyClient(); //we'll use the loggly client instead of HTTP directly

            //create necessary path elements
            var candidateSearchPath = Path.GetFileName(bufferBaseFilename) + "*.json";
            var logFolder = Path.GetDirectoryName(candidateSearchPath);

            //Filebase is currently the only option available so we will stick with it directly (for now)
            var encodingToUse = encoding;
            var bookmarkProvider = new FileBasedBookmarkProvider(bufferBaseFilename, _fileSystemAdapter, encoding);
            _bufferDataProvider = new FileBufferDataProvider(bufferBaseFilename, _fileSystemAdapter, bookmarkProvider, encodingToUse, batchPostingLimit, eventBodyLimitBytes, retainedFileCountLimit);
			_invalidPayloadLogger = new InvalidPayloadLogger(logFolder, encodingToUse, _fileSystemAdapter, retainedInvalidPayloadsLimitBytes);

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
                //we'll use this to control the number of events read per cycle. If the batch limit is reached,
                //then there is probably more events queued and we should continue to read them. Otherwise,
                // we can wait for the next timer tick moment to see if anything new is available.
                int numberOfEventsRead; 
                do
                {
                    //this should consistently return the same batch of events until 
                    //a MarkAsProcessed message is sent to the provider. Never return a null, please...
                    var payload = _bufferDataProvider.GetNextBatchOfEvents();
                    numberOfEventsRead = payload.Count();

                    if (numberOfEventsRead > 0)
                    {
                        //send the loggly events through the bulk API
                        var result = await _logglyClient.Log(payload).ConfigureAwait(false);

                        if (result.Code == ResponseCode.Success)
                        {
                            _connectionSchedule.MarkSuccess();
                            _bufferDataProvider.MarkCurrentBatchAsProcessed();
                        }
                        else if (result.Code == ResponseCode.Error)
                        {
                            // The connection attempt was successful - the payload we sent was the problem.
                            _connectionSchedule.MarkSuccess();
                            _bufferDataProvider.MarkCurrentBatchAsProcessed();  //move foward

                            _invalidPayloadLogger.DumpInvalidPayload(result, payload);
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

                        // not getting any batch may mean our marker is off, or at the end of the current, old file. 
                        // Try to move foward and cleanup
                        _bufferDataProvider.MoveBookmarkForward();
                    }
                } while (numberOfEventsRead == _batchPostingLimit);  
                //keep sending as long as we can retrieve a full batch. If not, wait for next tick
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
    }
}



