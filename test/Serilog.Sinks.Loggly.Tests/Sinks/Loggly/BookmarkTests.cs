using Xunit;

namespace Serilog.Sinks.Loggly.Tests.Sinks.Loggly
{
    public class BookmarkTests
    {
        [Fact]
        public void CanCreateBookmarkInstance()
        {
            var bookmark = new Bookmark(0, @"C:\test");

            Assert.NotNull(bookmark);
        }
    }
}
