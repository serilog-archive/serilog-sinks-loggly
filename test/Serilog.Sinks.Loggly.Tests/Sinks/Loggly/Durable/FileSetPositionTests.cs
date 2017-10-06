using Serilog.Sinks.Loggly.Durable;
using Xunit;

namespace Serilog.Sinks.Loggly.Tests.Sinks.Loggly.Durable
{
    public class FileSetPositionTests
    {
        [Fact]
        public void CanCreateBookmarkInstance()
        {
            var marker = new FileSetPosition(0, @"C:\test");
            Assert.NotNull(marker);
        }

        [Fact]
        public void CanCreateEmptyBookmark()
        {
            var marker = FileSetPosition.None;
            Assert.Null(marker);
        }
    }
}
