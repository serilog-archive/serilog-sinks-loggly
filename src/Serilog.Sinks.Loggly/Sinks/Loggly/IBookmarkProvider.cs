namespace Serilog.Sinks.Loggly
{
    interface IBookmarkProvider
    {
        Bookmark GetCurrentBookmarkPosition();

        void UpdateBookmark(Bookmark newBookmark);
    }
}