using Loggly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NSubstitute;
using Xunit;
using Serilog.Debugging;

namespace Serilog.Sinks.Loggly.Tests.Sinks.Loggly
{
    public class InvalidPayloadLoggerTests
    {
        const string LogFolder = @"C:\tests";   //any path here will do.
        static Encoding _utf8Encoder = new UTF8Encoding(true);

        public InvalidPayloadLoggerTests()
        {
            SelfLog.Enable(Console.Out);
            SelfLog.WriteLine("Newline to use (test setting): {0}", Environment.NewLine.Length == 2 ? "RN" : "N");
        }

        [Fact]
        public void CanCreateInvalidShippmentLoggerInstance()
        {
            var instance = new InvalidPayloadLogger(LogFolder, _utf8Encoder, Substitute.For<FileSystemAdapter>(), null);

            Assert.NotNull(instance);
        }

        public class InvalidPayloadPersistenceTests
        {
            readonly IFileSystemAdapter _fsAdapter;
            string _writtenData;
            string _generatedFilename;

            public InvalidPayloadPersistenceTests()
            {
                _fsAdapter = Substitute.For<IFileSystemAdapter>();
                _fsAdapter.When(x => x.WriteAllBytes(Arg.Any<string>(), Arg.Any<byte[]>()))
                    .Do(x =>
                    {
                        _generatedFilename = x.ArgAt<string>(0);
                        _writtenData = _utf8Encoder.GetString(x.ArgAt<byte[]>(1));
                    });


                //simulate the Post to Loggly failure with an error response and fixed payload.
                var response = new LogResponse() { Code = ResponseCode.Error, Message = "502 Bad Request" };
                //just need an empty event for testing
                var payload = new List<LogglyEvent>()
                {
                    new LogglyEvent()
                    {
                        Data = new MessageData() { },
                        Options = new EventOptions() { },
                        Syslog = new SyslogHeader() {MessageId = 0},
                        Timestamp = DateTimeOffset.Parse("2017-09-27T00:00:00+00:00")   //fixed date for comparisson
                    }
                };

                var instance = new InvalidPayloadLogger(LogFolder, _utf8Encoder, _fsAdapter, null);
                //exercice the method 
                instance.DumpInvalidPayload(response, payload);
            }

            [Fact]
            public void GeneratedFileHasEventsAndErrorInfoInContent()
            {
                using (var expectedFileTextStream = GetExpectedFileTextStream())
                {
#pragma warning disable SG0018 // Path traversal
                    using (var reader = new StreamReader(expectedFileTextStream, _utf8Encoder, true))
#pragma warning restore SG0018 // Path traversal
                    {
                        var expectedFileTestString = reader.ReadToEnd();
                        Assert.Equal(expectedFileTestString, _writtenData);
                    }
                }
            }

            [Fact]
            public void GeneratedFileHasExpectedname()
            {
                var expectedFileNameRegex = new Regex(@"invalid-\d{14}-Error-[a-fA-F0-9]{32}.json$");
                Assert.Matches(expectedFileNameRegex,_generatedFilename);
            }
        }

        static Stream GetExpectedFileTextStream()
        {
            var resourceNameSuffix = Environment.NewLine.Length == 2 ? "RN" : "N";
            var resourceName = $"Serilog.Sinks.Loggly.Tests.Sinks.Loggly.Expectations.expectedInvalidPayloadFile{resourceNameSuffix}.json";
            return typeof(InvalidPayloadLoggerTests)
                .GetTypeInfo()
                .Assembly
                .GetManifestResourceStream(resourceName);
        }
    }
}
