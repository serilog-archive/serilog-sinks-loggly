using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Loggly;
using Xunit;
using NSubstitute;
using System;
using Serilog.Sinks.Loggly.Durable;

namespace Serilog.Sinks.Loggly.Tests.Sinks.Loggly
{
    public class FileBufferDataProviderTests
    {
        static readonly string ResourceNamespace = $"Serilog.Sinks.Loggly.Tests.Sinks.Loggly";
        static readonly string BaseBufferFileName = @"c:\test\buffer";
        static readonly Encoding Utf8Encoder = new UTF8Encoding(true);
        static readonly string Bufferfile = @"C:\test\buffer001.json"; //any valid name here will suffice
        static readonly int BatchLimit = 10;
        static readonly int EventSizeLimit = 1024 * 1024;

        public class InstanceCreationTests
        {
            [Fact]
            public void CanCreateInstanceOfFileBufferDataProvider()
            {
                var mockFileSystemAdapter = Substitute.For<IFileSystemAdapter>();
                var bookmarkProvider = Substitute.For<IBookmarkProvider>();

                var instance = new FileBufferDataProvider(BaseBufferFileName, mockFileSystemAdapter, bookmarkProvider, Utf8Encoder, 10, 1024*1024, null);

                Assert.NotNull(instance);
            }
        }

        /// <summary>
        /// In this scenario, there is neither a bufferX.json file nor a bookmark.
        /// </summary>
        public class EmptyBufferAndBookmarkScenario
        {
            readonly IEnumerable<LogglyEvent> _sut;

            public EmptyBufferAndBookmarkScenario()
            {
                var bookmarkProvider = Substitute.For<IBookmarkProvider>();
                bookmarkProvider.GetCurrentBookmarkPosition().Returns(null as FileSetPosition);

                var mockFileSystem = Substitute.For<IFileSystemAdapter>();
                mockFileSystem.GetFiles(Arg.Any<string>(), Arg.Any<string>()).Returns(new string[] { });

                var provider = new FileBufferDataProvider(BaseBufferFileName, mockFileSystem, bookmarkProvider, Utf8Encoder, 10, 1024 * 1024, null);
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
            readonly IEnumerable<LogglyEvent> _sut;

            public EmptyBufferAndOutdatedBookmarkScenario()
            {
                var bookmarkProvider = Substitute.For<IBookmarkProvider>();
                bookmarkProvider.GetCurrentBookmarkPosition().Returns(new FileSetPosition(0, @"C:\test\existent.json"));

                var mockFileSystem = Substitute.For<IFileSystemAdapter>();
                mockFileSystem.GetFiles(Arg.Any<string>(), Arg.Any<string>()).Returns(new string[] { });

                var provider = new FileBufferDataProvider(BaseBufferFileName, mockFileSystem, bookmarkProvider, Utf8Encoder, 10, 1024 * 1024, null);
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
                var bookmarkProvider = Substitute.For<IBookmarkProvider>();
                bookmarkProvider.GetCurrentBookmarkPosition().Returns(null as FileSetPosition);
                IFileSystemAdapter fsAdapter = CreateFileSystemAdapter(Bufferfile);

                var provider = new FileBufferDataProvider(
                    BaseBufferFileName,
                    fsAdapter,
                    bookmarkProvider,
                    Utf8Encoder,
                    BatchLimit,
                    EventSizeLimit,
                    null);
                _sut = provider.GetNextBatchOfEvents();

                _reRequestBatch = provider.GetNextBatchOfEvents();
            }

            IFileSystemAdapter CreateFileSystemAdapter(string bufferfile)
            {
                var fileSystemAdapter = Substitute.For<IFileSystemAdapter>();

                //get files should return the single buffer file path in this scenario
                fileSystemAdapter.GetFiles(Arg.Any<string>(), Arg.Any<string>())
                    .Returns(new[] { bufferfile });

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
            readonly IEnumerable<LogglyEvent> _sut;
            readonly IEnumerable<LogglyEvent> _reRequestBatch;

            public SingleBufferFileAndSyncedBookmarkScenario()
            {
                var bookmarkProvider = Substitute.For<IBookmarkProvider>();
                bookmarkProvider.GetCurrentBookmarkPosition().Returns(new FileSetPosition(0, Bufferfile));
                IFileSystemAdapter fsAdapter = CreateFileSystemAdapter(Bufferfile);

                var provider = new FileBufferDataProvider(
                    BaseBufferFileName, 
                    fsAdapter, 
                    bookmarkProvider, 
                    Utf8Encoder, 
                    BatchLimit, 
                    EventSizeLimit,
                    null);
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
            readonly IEnumerable<LogglyEvent> _sut;
            readonly IEnumerable<LogglyEvent> _reRequestBatch;

            public LongerBufferFileAndSyncedBookmarkScenario()
            {
                var bookmarkProvider = Substitute.For<IBookmarkProvider>();
                bookmarkProvider.GetCurrentBookmarkPosition().Returns(new FileSetPosition(0, Bufferfile));
                IFileSystemAdapter fsAdapter = CreateFileSystemAdapter(Bufferfile);

                var provider = new FileBufferDataProvider(
                    BaseBufferFileName,
                    fsAdapter,
                    bookmarkProvider,
                    Utf8Encoder,
                    BatchLimit,
                    EventSizeLimit,
                    null);
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
            readonly IEnumerable<LogglyEvent> _firstBatchRead;
            readonly IEnumerable<LogglyEvent> _reRequestBatch;
            readonly IEnumerable<LogglyEvent> _lastBatch;

            public AdvanceThroughBufferScenario()
            {
                var bookmarkProvider = Substitute.For<IBookmarkProvider>();
                bookmarkProvider.GetCurrentBookmarkPosition().Returns(new FileSetPosition(0, Bufferfile));
                IFileSystemAdapter fsAdapter = CreateFileSystemAdapter(Bufferfile);

                var provider = new FileBufferDataProvider(
                    BaseBufferFileName,
                    fsAdapter,
                    bookmarkProvider,
                    Utf8Encoder,
                    BatchLimit,
                    EventSizeLimit,
                    null);

                _firstBatchRead = provider.GetNextBatchOfEvents();
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
                    .Returns(new[] { bufferfile });

                //when we ask for the buffer file, simulate that it exists
                fileSystemAdapter.Exists(bufferfile).Returns(true);

                //Open() should open a stream that can return two events
                fileSystemAdapter.Open(bufferfile, Arg.Any<FileMode>(), Arg.Any<FileAccess>(),
                        Arg.Any<FileShare>())
                    .Returns(x => Get20LineStreamFromResources());    //use this form to reexecute the get stream for a new stream

                return fileSystemAdapter;
            }

            [Fact]
            public void EventListShouldBeNotBeEmpty() => Assert.NotEmpty(_firstBatchRead);

            [Fact]
            public void ShouldReadBatchOfEventsLimitedToBatchCount() => Assert.Equal(10, _firstBatchRead.Count());

            [Fact]
            public void ReRequestingABatchShouldReturnSameUnprocessedEventsInQueue() =>
                Assert.NotEqual(_firstBatchRead, _reRequestBatch);

            [Fact]
            public void LastBatchShouldBeEmpty() => Assert.Empty(_lastBatch);
        }

        /// <summary>
        /// In this scenario, the app may have been offline / disconnected for a few days (desktop clients, for instance)
        /// and multiple files may have accumulated. We may or may not want data from all the days offline,
        /// depending on retainedFileCountLimit's value, and should work the bookmark acordingly
        /// </summary>
        public class MultipleBufferFilesScenario
        {
            /// <summary>
            /// When less then the limit, bookmark should point to initial file if current bookmark is invalid
            /// </summary>
            public class LessThenLimitnumberOfBufferFiles
            {
                const int NumberOfFilesToRetain = 5;
                FileSetPosition _sut;
                
                public LessThenLimitnumberOfBufferFiles()
                {
                    var bookmarkProvider = Substitute.For<IBookmarkProvider>();
                    bookmarkProvider
                        .GetCurrentBookmarkPosition()
                        .Returns(new FileSetPosition(0, @"c:\unknown.json")); //should force fileset analysis
                    bookmarkProvider
                        .When(x => x.UpdateBookmark(Arg.Any<FileSetPosition>()))
                        .Do(x => _sut = x.ArgAt<FileSetPosition>(0));

                    IFileSystemAdapter fsAdapter = CreateFileSystemAdapter(Bufferfile);

                    var provider = new FileBufferDataProvider(
                        BaseBufferFileName,
                        fsAdapter,
                        bookmarkProvider,
                        Utf8Encoder,
                        BatchLimit,
                        EventSizeLimit,
                        NumberOfFilesToRetain);

                    provider.GetNextBatchOfEvents();
                    provider.MarkCurrentBatchAsProcessed();
                }

                IFileSystemAdapter CreateFileSystemAdapter(string bufferfile)
                {
                    var fileSystemAdapter = Substitute.For<IFileSystemAdapter>();

                    //get files should return the single buffer file path in this scenario
                    fileSystemAdapter.GetFiles(Arg.Any<string>(), Arg.Any<string>())
                        .Returns(new[] {bufferfile});

                    //when we ask for the buffer file (and only that file), simulate that it exists; for others return false
                    fileSystemAdapter.Exists(Arg.Any<string>()).Returns(false);
                    fileSystemAdapter.Exists(bufferfile).Returns(true);

                    //Open() should open a stream that can return two events
                    fileSystemAdapter.Open(bufferfile, Arg.Any<FileMode>(), Arg.Any<FileAccess>(),
                            Arg.Any<FileShare>())
                        .Returns(x =>
                            GetSingleEventLineStreamFromResources()); //use this form to reexecute the get stream for a new stream

                    return fileSystemAdapter;
                }

                /// <summary>
                /// If we have an event, then the bookmark moved to the correct file. bookmark is private to the provider, 
                /// and doesn't get updated in the file 
                /// </summary>
                [Fact]
                public void ShouldReadFromFirstFileInSetOfExistingFiles() => Assert.Equal(Bufferfile, _sut.File);
            }

            public class EqualToLimitNumberOfBufferFiles
            {
                const int NumberOfFilesToRetain = 1;
                FileSetPosition _sut;

                public EqualToLimitNumberOfBufferFiles()
                {
                    var bookmarkProvider = Substitute.For<IBookmarkProvider>();
                    bookmarkProvider
                        .GetCurrentBookmarkPosition()
                        .Returns(new FileSetPosition(0, @"c:\unknown.json")); //should force fileset analysis
                    bookmarkProvider
                        .When(x => x.UpdateBookmark(Arg.Any<FileSetPosition>()))
                        .Do(x => _sut = x.ArgAt<FileSetPosition>(0));

                    IFileSystemAdapter fsAdapter = CreateFileSystemAdapter(Bufferfile);

                    var provider = new FileBufferDataProvider(
                        BaseBufferFileName,
                        fsAdapter,
                        bookmarkProvider,
                        Utf8Encoder,
                        BatchLimit,
                        EventSizeLimit,
                        NumberOfFilesToRetain);

                    provider.GetNextBatchOfEvents();
                    provider.MarkCurrentBatchAsProcessed();
                }

                IFileSystemAdapter CreateFileSystemAdapter(string bufferfile)
                {
                    var fileSystemAdapter = Substitute.For<IFileSystemAdapter>();

                    //get files should return the single buffer file path in this scenario
                    fileSystemAdapter.GetFiles(Arg.Any<string>(), Arg.Any<string>())
                        .Returns(new[] { bufferfile });

                    //when we ask for the buffer file (and only that file), simulate that it exists; for others return false
                    fileSystemAdapter.Exists(Arg.Any<string>()).Returns(false);
                    fileSystemAdapter.Exists(bufferfile).Returns(true);

                    //Open() should open a stream that can return two events
                    fileSystemAdapter.Open(bufferfile, Arg.Any<FileMode>(), Arg.Any<FileAccess>(),
                            Arg.Any<FileShare>())
                        .Returns(x =>
                            GetSingleEventLineStreamFromResources()); //use this form to reexecute the get stream for a new stream

                    return fileSystemAdapter;
                }

                /// <summary>
                /// If we have an event, then the bookmark moved to the correct file. bookmark is private to the provider, 
                /// and doesn't get updated in the file 
                /// </summary>
                [Fact]
                public void ShouldReadFromFirstFileInSetOfExistingFiles() => Assert.Equal(Bufferfile, _sut.File);
            }

            public class MoreThenTheLimitNumberOfBufferFiles
            {
                const string UnknownJsonFileName = @"c:\a\unknown.json";    // \a\ to guarantee ordering
                const int NumberOfFilesToRetain = 1;
                FileSetPosition _sut;

                public MoreThenTheLimitNumberOfBufferFiles()
                {
                    var bookmarkProvider = Substitute.For<IBookmarkProvider>();
                    bookmarkProvider
                        .GetCurrentBookmarkPosition()
                        .Returns(new FileSetPosition(0, UnknownJsonFileName)); //should force fileset analysis
                    bookmarkProvider
                        .When(x => x.UpdateBookmark(Arg.Any<FileSetPosition>()))
                        .Do(x => _sut = x.ArgAt<FileSetPosition>(0));

                    IFileSystemAdapter fsAdapter = CreateFileSystemAdapter(Bufferfile);

                    var provider = new FileBufferDataProvider(
                        BaseBufferFileName,
                        fsAdapter,
                        bookmarkProvider,
                        Utf8Encoder,
                        BatchLimit,
                        EventSizeLimit,
                        NumberOfFilesToRetain);

                    provider.GetNextBatchOfEvents();
                    provider.MarkCurrentBatchAsProcessed();
                }

                IFileSystemAdapter CreateFileSystemAdapter(string bufferfile)
                {
                    var fileSystemAdapter = Substitute.For<IFileSystemAdapter>();

                    //get files should return the single buffer file path in this scenario at the end, 
                    // and equal to the number of retained files; unkowns should be ignored
                    fileSystemAdapter.GetFiles(Arg.Any<string>(), Arg.Any<string>())
                        .Returns(new[] { UnknownJsonFileName, UnknownJsonFileName, bufferfile });

                    //when we ask for the buffer file (and only that file), simulate that it exists; for others return false
                    fileSystemAdapter.Exists(Arg.Any<string>()).Returns(false);
                    fileSystemAdapter.Exists(bufferfile).Returns(true);

                    //Open() should open a stream that can return two events
                    fileSystemAdapter.Open(bufferfile, Arg.Any<FileMode>(), Arg.Any<FileAccess>(),
                            Arg.Any<FileShare>())
                        .Returns(x =>
                            GetSingleEventLineStreamFromResources()); //use this form to reexecute the get stream for a new stream

                    return fileSystemAdapter;
                }

                /// <summary>
                /// If we have an event, then the bookmark moved to the correct file. bookmark is private to the provider, 
                /// and doesn't get updated in the file 
                /// </summary>
                [Fact]
                public void ShouldReadFromFirstFileInSetOfExistingFiles() => Assert.Equal(Bufferfile, _sut.File);
            }
        }




        static Stream Get20LineStreamFromResources()
        {
            var resourceNameSuffix = Environment.NewLine.Length == 2 ? "RN" : "N";
            var resourceName = $"{ResourceNamespace}.SampleBuffers.20Events{resourceNameSuffix}.json";
            return GetStreamFromResources(resourceName);
        }

        static Stream GetSingleEventLineStreamFromResources()
        {
            var resourceName = $"{ResourceNamespace}.SampleBuffers.singleEvent.json";
            return GetStreamFromResources(resourceName);
        }

        static Stream GetStreamFromResources(string resourceName)
        {
            MemoryStream ms = new MemoryStream();
            typeof(FileBufferDataProviderTests)
                .GetTypeInfo()
                .Assembly
                .GetManifestResourceStream(resourceName)
                ?.CopyTo(ms);
            return ms;
        }
    }
}
