namespace Serilog.Sinks.Loggly
{
    internal interface IBookmarkProvider
    {
        Bookmark GetCurrentBookmarkPosition();

        void UpdateBookmark(Bookmark newBookmark);
    }
}