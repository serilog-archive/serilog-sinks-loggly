using System.IO;
using System.Text;
using NSubstitute;
using Xunit;

namespace Serilog.Sinks.Loggly.Tests.Sinks.Loggly
{
    public class FileBasedBookmarkProviderTests
    {
        static Encoding Encoder = new UTF8Encoding(false);
        static string BaseBufferFileName = @"c:\test\buffer";
        const string ExpectedBufferFilePath = @"C:\test\buffer-20170926.json";
        const long ExpectedBytePosition = 123;

        public class InstanceTests
        {
            readonly IBookmarkProvider _sut = new FileBasedBookmarkProvider(BaseBufferFileName, Substitute.For<IFileSystemAdapter>(), Encoder);

            [Fact]
            public void InstanceIsValid() => Assert.NotNull(_sut);
        }

        public class ReadBookmarkTests
        {
            public class ValidBookmarkFileOnDisk
            {
                readonly Bookmark _sut;
                readonly Bookmark _reread;

                public ValidBookmarkFileOnDisk()
                {
                    var fileSystemAdapter = Substitute.For<IFileSystemAdapter>();
                    fileSystemAdapter
                        .Open(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                        .Returns(new MemoryStream(
                            Encoding.UTF8.GetBytes($"{ExpectedBytePosition}:::{ExpectedBufferFilePath}\r\n")));

                    var provider = new FileBasedBookmarkProvider(BaseBufferFileName, fileSystemAdapter, Encoder);

                    _sut = provider.GetCurrentBookmarkPosition();
                    _reread = provider.GetCurrentBookmarkPosition();
                }

                [Fact]
                public void ShouldHaveValidBookmark() => Assert.NotNull(_sut);

                [Fact]
                public void BookmarkPostionShouldBeCorrect() => Assert.Equal(ExpectedBytePosition, _sut.Position);

                [Fact]
                public void BookmarkBufferFilePathShouldBeCorrect() =>
                    Assert.Equal(ExpectedBufferFilePath, _sut.FileName);

                [Fact]
                public void RereadingtheBookmarkGivesSameValue() => Assert.Equal(_sut.Position, _reread.Position);

            }

            /// <summary>
            /// This case represents some observed behaviour in the bookmark file. Since writes are normally done from the start of the file, 
            /// some garbage may remain in the file if the new written bytes are shorter than what existed.
            /// </summary>
            public class StrangeBookmarkFileOnDisk
            {
                readonly Bookmark _sut;

                public StrangeBookmarkFileOnDisk()
                {
                    var fileSystemAdapter = Substitute.For<IFileSystemAdapter>();
                    fileSystemAdapter
                        .Open(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                        .Returns(new MemoryStream(Encoding.UTF8.GetBytes(
                            $"{ExpectedBytePosition}:::{ExpectedBufferFilePath}\r\nsome invalid stuff in the file\n\n\n")));

                    var provider = new FileBasedBookmarkProvider(BaseBufferFileName, fileSystemAdapter, Encoder);

                    _sut = provider.GetCurrentBookmarkPosition();
                }

                [Fact]
                public void ShouldHaveValidBookmark() => Assert.NotNull(_sut);

                [Fact]
                public void BookmarkPostionShouldBeCorrect() => Assert.Equal(ExpectedBytePosition, _sut.Position);

                [Fact]
                public void BookmarkBufferFilePathShouldBeCorrect() =>
                    Assert.Equal(ExpectedBufferFilePath, _sut.FileName);

            }

            /// <summary>
            /// An inexistent bookmark file will create a new, empty one, and the returned stream will be empty when trying to read
            /// </summary>
            public class InexistentBookmarkFileOnDisk
            {
                Bookmark _sut;

                public InexistentBookmarkFileOnDisk()
                {
                    var fileSystemAdapter = Substitute.For<IFileSystemAdapter>();
                    fileSystemAdapter
                        .Open(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                        .Returns(new MemoryStream(new byte[] { }));

                    var provider = new FileBasedBookmarkProvider(BaseBufferFileName, fileSystemAdapter, Encoding.UTF8);

                    _sut = provider.GetCurrentBookmarkPosition();
                }

                [Fact]
                public void BookmarkShouldBeNull() => Assert.Null(_sut);
            }
        }

        public class WriteBookmarkTests
        {
            public class WriteToAnEmptyBookmarkStream
            {
                readonly MemoryStream _sut = new MemoryStream(new byte[128]); //make it big enough to take in new content, as a file stream would
                readonly string _expectedFileContent = $"{ExpectedBytePosition}:::{ExpectedBufferFilePath}\r\n".PadRight(128, '\0');
                readonly byte[] _expectedBytes;
                readonly byte[] _actualBytes;

                public WriteToAnEmptyBookmarkStream()
                {
                    var fileSystemAdapter = Substitute.For<IFileSystemAdapter>();
                    fileSystemAdapter
                        .Open(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                        .Returns(_sut);

                    var provider = new FileBasedBookmarkProvider(BaseBufferFileName, fileSystemAdapter, Encoder);
                    provider.UpdateBookmark(new Bookmark(ExpectedBytePosition, ExpectedBufferFilePath));

                    _expectedBytes = Encoder.GetBytes(_expectedFileContent);
                    _actualBytes = _sut.ToArray();
                }

                //compare on bytes and string - if Encoding.UTF8 is used, a BOM set of bytes may be added.
                //it is therefore useful to use an encoder created by the UTF8Encoding constructor as in the container class
                
                [Fact]
                public void StreamShouldHaveBookmarkWritten() => Assert.Equal(_expectedBytes, _actualBytes);

                [Fact]
                public void StreamContentShouldConvertToExpectedText() => Assert.Equal(_expectedFileContent, Encoder.GetString(_sut.ToArray()));

            }

            public class WriteToAnBookmarkStreamWithExistingContent
            {
                readonly MemoryStream _sut = new MemoryStream(new byte[128]); //make it big enough to take in new content, as a file stream would
                
                //contrary to the empty files, there will be sopme "garbage in the expected stream, since we are not clearing it
                //but just wirting on top of it. Line endings determine the truly significant info in the file
                //the following reflects this:
                readonly string _expectedFileContent = $"{ExpectedBytePosition}:::{ExpectedBufferFilePath}\r\nn{ExpectedBufferFilePath}\r\n".PadRight(128, '\0');
                readonly byte[] _expectedBytes;
                readonly byte[] _actualBytes;

                public WriteToAnBookmarkStreamWithExistingContent()
                {
                    //simulate a stream with existing content that may be larger or shorter than what is written. 
                    // newline detection ends up being iportant in this case 
                    var initialContent = Encoder.GetBytes($"100000:::{ExpectedBufferFilePath}{ExpectedBufferFilePath}\r\n");
                    _sut.Write(initialContent, 0, initialContent.Length);

                    var fileSystemAdapter = Substitute.For<IFileSystemAdapter>();
                    fileSystemAdapter
                        .Open(Arg.Any<string>(), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                        .Returns(_sut);

                    var provider = new FileBasedBookmarkProvider(BaseBufferFileName, fileSystemAdapter, Encoder);
                    provider.UpdateBookmark(new Bookmark(ExpectedBytePosition, ExpectedBufferFilePath));

                    _expectedBytes = Encoder.GetBytes(_expectedFileContent);
                    _actualBytes = _sut.ToArray();
                }

                //compare on bytes and string - if Encoding.UTF8 is used, a BOM set of bytes may be added.
                //it is therefore useful to use an encoder created by the UTF8Encoding constructor as in the container class

                [Fact]
                public void StreamShouldHaveBookmarkWritten() => Assert.Equal(_expectedBytes, _actualBytes);

                [Fact]
                public void StreamContentShouldConvertToExpectedText() => Assert.Equal(_expectedFileContent, Encoder.GetString(_sut.ToArray()));

            }
        }
    }
}