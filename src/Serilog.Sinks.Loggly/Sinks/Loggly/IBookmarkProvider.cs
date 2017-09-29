using System;
using Serilog.Sinks.Loggly.Durable;

namespace Serilog.Sinks.Loggly
{
    interface IBookmarkProvider : IDisposable
    {
        FileSetPosition GetCurrentBookmarkPosition();

        void UpdateBookmark(FileSetPosition newBookmark);
    }
}