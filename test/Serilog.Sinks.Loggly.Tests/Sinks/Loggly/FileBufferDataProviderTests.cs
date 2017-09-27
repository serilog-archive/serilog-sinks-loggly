using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Loggly;
using Xunit;
using NSubstitute;
using System;

namespace Serilog.Sinks.Loggly.Tests.Sinks.Loggly
{
    public class FileBufferDataProviderTests
    {
        static string BaseBufferFileName = @"c:\test\buffer";
        static Encoding _utf8Encoder = new UTF8Encoding(true);

        public class InstanceCreationTests
        {
            [Fact]
            public void CanCreateInstanceOfFileBufferDataProvider()
            {
                var mockFileSystemAdapter = Substitute.For<IFileSystemAdapter>();
                var bookmarkProvider = Substitute.For<IBookmarkProvider>();

                var instance = new FileBufferDataProvider(BaseBufferFileName, mockFileSystemAdapter, bookmarkProvider, _utf8Encoder, 10, 1024*1024);

                Assert.NotNull(instance);
            }
        }

        /// <summary>
        /// In this scenario, there is neither a bufferX.json file nor a bookmark.
        /// </summary>
        public class EmptyBufferAndBookmarkScenario
        {
            private IEnumerable<LogglyEvent> _sut;

            public EmptyBufferAndBookmarkScenario()
            {
                var bookmarkProvider = Substitute.For<IBookmarkProvider>();
                bookmarkProvider.GetCurrentBookmarkPosition().Returns(null as Bookmark);

                var mockFileSystem = Substitute.For<IFileSystemAdapter>();
                mockFileSystem.GetFiles(Arg.Any<string>(), Arg.Any<string>()).Returns(new string[] { });

                var provider = new FileBufferDataProvider(BaseBufferFileName, mockFileSystem, bookmarkProvider, _utf8Encoder, 10, 1024 * 1024);
                _sut = provider.GetNextBatchOfEvents();
            }

            [Fact]
            public void EventListShouldBeEmpty() => Assert.Empty(_sut);
        }

        /// <summary>
        /// In this scenario, there is no bufferX.json file but there is a bookmark file. The bookmark, though, 
        /// points to a file buffer file that no longer exists
        /// </summary>
        public class EmptyBufferAndOutdatedBookmarkScenario
        {
            private IEnumerable<LogglyEvent> _sut;

            public EmptyBufferAndOutdatedBookmarkScenario()
            {
                var bookmarkProvider = Substitute.For<IBookmarkProvider>();
                bookmarkProvider.GetCurrentBookmarkPosition().Returns(new Bookmark(0, @"C:\test\existent.json"));

                var mockFileSystem = Substitute.For<IFileSystemAdapter>();
                mockFileSystem.GetFiles(Arg.Any<string>(), Arg.Any<string>()).Returns(new string[] { });

                var provider = new FileBufferDataProvider(BaseBufferFileName, mockFileSystem, bookmarkProvider, _utf8Encoder, 10, 1024 * 1024);
                _sut = provider.GetNextBatchOfEvents();
            }

            [Fact]
            public void EventListShouldBeEmpty() => Assert.Empty(_sut);
        }

        /// <summary>
        /// In this scenario, there is a single Buffer.json file but no bookmark file.
        /// Results are the same as the SingleBufferFileAndSyncedBookmarkScenario as the 
        /// buffer will be initialized to the first buffer file
        /// </summary>
        public class SingleBufferFileAndNoBookmarkScenario
        {
            IEnumerable<LogglyEvent> _sut;
            IEnumerable<LogglyEvent> _reRequestBatch;

            public SingleBufferFileAndNoBookmarkScenario()
            {
                var bufferfile = @"C:\test\buffer001.json"; //any valid name here will suffice
                var batchLimit = 10;
                var eventSizeLimit = 1024 * 1024;

                var bookmarkProvider = Substitute.For<IBookmarkProvider>();
                bookmarkProvider.GetCurrentBookmarkPosition().Returns(null as Bookmark);
                IFileSystemAdapter fsAdapter = CreateFileSystemAdapter(bufferfile);

                var provider = new FileBufferDataProvider(
                    BaseBufferFileName,
                    fsAdapter,
                    bookmarkProvider,
                    _utf8Encoder,
                    batchLimit,
                    eventSizeLimit);
                _sut = provider.GetNextBatchOfEvents();

                _reRequestBatch = provider.GetNextBatchOfEvents();
            }

            IFileSystemAdapter CreateFileSystemAdapter(string bufferfile)
            {
                var fileSystemAdapter = Substitute.For<IFileSystemAdapter>();

                //get files should return the single buffer file path in this scenario
                fileSystemAdapter.GetFiles(Arg.Any<string>(), Arg.Any<string>())
                    .Returns(new string[] { bufferfile });

                //when we ask for the buffer file, simulate that it exists
                fileSystemAdapter.Exists(bufferfile).Returns(true);

                //Open() should open a stream that can return two events
                fileSystemAdapter.Open(bufferfile, Arg.Any<FileMode>(), Arg.Any<FileAccess>(),
                        Arg.Any<FileShare>())
                    .Returns(GetSingleEventLineStreamFromResources());

                return fileSystemAdapter;
            }

           [Fact]
            public void EventListShouldBeNotBeEmpty() => Assert.NotEmpty(_sut);

            [Fact]
            public void ShouldReadBatchOfEvents() => Assert.Single(_sut);

            [Fact]
            public void ReRequestingABatchShouldReturnSameUnprocessedEventsInQueue() =>
                Assert.Equal(_sut, _reRequestBatch);
        }


        /// <summary>
        /// In this scenario, there is a single Buffer.json file but a bookmark file pointing
        /// to the start of the buffer.
        /// </summary>
        public class SingleBufferFileAndSyncedBookmarkScenario
        {
            IEnumerable<LogglyEvent> _sut;
            IEnumerable<LogglyEvent> _reRequestBatch;

            public SingleBufferFileAndSyncedBookmarkScenario()
            {
                var bufferfile = @"C:\test\buffer001.json"; //any valid name here will suffice
                var batchLimit = 10;
                var eventSizeLimit = 1024 * 1024;

                var bookmarkProvider = Substitute.For<IBookmarkProvider>();
                bookmarkProvider.GetCurrentBookmarkPosition().Returns(new Bookmark(0, bufferfile));
                IFileSystemAdapter fsAdapter = CreateFileSystemAdapter(bufferfile);

                var provider = new FileBufferDataProvider(
                    BaseBufferFileName, 
                    fsAdapter, 
                    bookmarkProvider, 
                    _utf8Encoder, 
                    batchLimit, 
                    eventSizeLimit);
                _sut = provider.GetNextBatchOfEvents();

                _reRequestBatch = provider.GetNextBatchOfEvents();
            }

            IFileSystemAdapter CreateFileSystemAdapter(string bufferfile)
            {
                var fileSystemAdapter = Substitute.For<IFileSystemAdapter>();

                //get files should return the single buffer file path in this scenario
                fileSystemAdapter.GetFiles(Arg.Any<string>(), Arg.Any<string>())
                    .Returns(new string[] {bufferfile});

                //when we ask for the buffer file, simulate that it exists
                fileSystemAdapter.Exists(bufferfile).Returns(true);

                //Open() should open a stream that can return two events
                fileSystemAdapter.Open(bufferfile, Arg.Any<FileMode>(), Arg.Any<FileAccess>(),
                        Arg.Any<FileShare>())
                    .Returns(GetSingleEventLineStreamFromResources());

                return fileSystemAdapter;
            }

            

            [Fact]
            public void EventListShouldBeNotBeEmpty() => Assert.NotEmpty(_sut);

            [Fact]
            public void ShouldReadBatchOfEvents() => Assert.Single(_sut);

            [Fact]
            public void ReRequestingABatchShouldReturnSameUnprocessedEventsInQueue() =>
                Assert.Equal(_sut, _reRequestBatch);
        }

        /// <summary>
        /// buffer file contains more events than a single batch. Rereading a batch should not progress without 
        /// marking the batch as processed
        /// </summary>
        public class LongerBufferFileAndSyncedBookmarkScenario
        {
            IEnumerable<LogglyEvent> _sut;
            IEnumerable<LogglyEvent> _reRequestBatch;

            public LongerBufferFileAndSyncedBookmarkScenario()
            {
                var bufferfile = @"C:\test\buffer001.json"; //any valid name here will suffice
                var batchLimit = 10;
                var eventSizeLimit = 1024 * 1024;

                var bookmarkProvider = Substitute.For<IBookmarkProvider>();
                bookmarkProvider.GetCurrentBookmarkPosition().Returns(new Bookmark(0, bufferfile));
                IFileSystemAdapter fsAdapter = CreateFileSystemAdapter(bufferfile);

                var provider = new FileBufferDataProvider(
                    BaseBufferFileName,
                    fsAdapter,
                    bookmarkProvider,
                    _utf8Encoder,
                    batchLimit,
                    eventSizeLimit);
                _sut = provider.GetNextBatchOfEvents();

                _reRequestBatch = provider.GetNextBatchOfEvents();
            }

            private IFileSystemAdapter CreateFileSystemAdapter(string bufferfile)
            {
                var fileSystemAdapter = Substitute.For<IFileSystemAdapter>();

                //get files should return the single buffer file path in this scenario
                fileSystemAdapter.GetFiles(Arg.Any<string>(), Arg.Any<string>())
                    .Returns(new string[] { bufferfile });

                //when we ask for the buffer file, simulate that it exists
                fileSystemAdapter.Exists(bufferfile).Returns(true);

                //Open() should open a stream that can return two events
                fileSystemAdapter.Open(bufferfile, Arg.Any<FileMode>(), Arg.Any<FileAccess>(),
                        Arg.Any<FileShare>())
                    .Returns(Get20LineStreamFromResources());

                return fileSystemAdapter;
            }

            [Fact]
            public void EventListShouldBeNotBeEmpty() => Assert.NotEmpty(_sut);

            [Fact]
            public void ShouldReadBatchOfEventsLimitedToBatchCount() => Assert.Equal(10, _sut.Count());

            [Fact]
            public void ReRequestingABatchShouldReturnSameUnprocessedEventsInQueue() =>
                Assert.Equal(_sut, _reRequestBatch);
        }

        /// <summary>
        /// Gets two batches, having marked the first as processed to move through the buffer
        /// After reading the second batch, there should no longer be anything in the buffer, so 
        /// the last batch should be empty
        /// </summary>
        public class AdvanceThroughBufferScenario
        {
            IEnumerable<LogglyEvent> _sut;
            IEnumerable<LogglyEvent> _reRequestBatch;
            IEnumerable<LogglyEvent> _lastBatch;

            public AdvanceThroughBufferScenario()
            {
                var bufferfile = @"C:\test\buffer001.json"; //any valid name here will suffice
                var batchLimit = 10;
                var eventSizeLimit = 1024 * 1024;

                var bookmarkProvider = Substitute.For<IBookmarkProvider>();
                bookmarkProvider.GetCurrentBookmarkPosition().Returns(new Bookmark(0, bufferfile));
                IFileSystemAdapter fsAdapter = CreateFileSystemAdapter(bufferfile);

                var provider = new FileBufferDataProvider(
                    BaseBufferFileName,
                    fsAdapter,
                    bookmarkProvider,
                    _utf8Encoder,
                    batchLimit,
                    eventSizeLimit);

                _sut = provider.GetNextBatchOfEvents();
                //after getting first batch, simulate moving foward
                provider.MarkCurrentBatchAsProcessed();
                //request next batch
                _reRequestBatch = provider.GetNextBatchOfEvents();
                //after getting second batch, simulate moving foward
                provider.MarkCurrentBatchAsProcessed();
                //should have no events available to read
                _lastBatch = provider.GetNextBatchOfEvents();

            }

            IFileSystemAdapter CreateFileSystemAdapter(string bufferfile)
            {
                var fileSystemAdapter = Substitute.For<IFileSystemAdapter>();

                //get files should return the single buffer file path in this scenario
                fileSystemAdapter.GetFiles(Arg.Any<string>(), Arg.Any<string>())
                    .Returns(new string[] { bufferfile });

                //when we ask for the buffer file, simulate that it exists
                fileSystemAdapter.Exists(bufferfile).Returns(true);

                //Open() should open a stream that can return two events
                fileSystemAdapter.Open(bufferfile, Arg.Any<FileMode>(), Arg.Any<FileAccess>(),
                        Arg.Any<FileShare>())
                    .Returns(x => Get20LineStreamFromResources());    //use this form to reexecute the get stream for a new stream

                return fileSystemAdapter;
            }

            [Fact]
            public void EventListShouldBeNotBeEmpty() => Assert.NotEmpty(_sut);

            [Fact]
            public void ShouldReadBatchOfEventsLimitedToBatchCount() => Assert.Equal(10, _sut.Count());

            [Fact]
            public void ReRequestingABatchShouldReturnSameUnprocessedEventsInQueue() =>
                Assert.NotEqual(_sut, _reRequestBatch);

            [Fact]
            public void LastBatchShouldBeEmpty() => Assert.Empty(_lastBatch);
        }

        static Stream Get20LineStreamFromResources()
        {
            var resourceNameSuffix = Environment.NewLine.Length == 2 ? "RN" : "N";
            var resourceName = $"Serilog.Sinks.Loggly.Tests.Sinks.Loggly.SampleBuffers.20Events{resourceNameSuffix}.json";
            return GetStreamFromResources(resourceName);
        }

        static Stream GetSingleEventLineStreamFromResources()
        {
            var resourceName = "Serilog.Sinks.Loggly.Tests.Sinks.Loggly.SampleBuffers.singleEvent.json";
            return GetStreamFromResources(resourceName);
        }

        static Stream GetStreamFromResources(string resourceName)
        {
            MemoryStream ms = new MemoryStream();
            typeof(FileBufferDataProviderTests)
                .GetTypeInfo()
                .Assembly
                .GetManifestResourceStream(resourceName)
                .CopyTo(ms);
            return ms;
        }

        
    }
}
