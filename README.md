# Serilog.Sinks.Loggly

[![Build status](https://ci.appveyor.com/api/projects/status/x2ob36tl8brpkkjf/branch/master?svg=true)](https://ci.appveyor.com/project/serilog/serilog-sinks-loggly/branch/master) [![NuGet Version](http://img.shields.io/nuget/v/Serilog.Sinks.Loggly.svg?style=flat)](https://www.nuget.org/packages/Serilog.Sinks.Loggly/) 


[Loggly](http://www.loggly.com) is a cloud based log management service. Create a new input and specify that you want to use a http input with JSON enabled. Use the [loggly-csharp-configuration](https://github.com/neutmute/loggly-csharp) XML configuration syntax to configure the sink.

**Package** - [Serilog.Sinks.Loggly](http://nuget.org/packages/serilog.sinks.loggly)
| **Platforms** - .NET 4.5

```csharp
var log = new LoggerConfiguration()
    .WriteTo.Loggly()
    .CreateLogger();
```

Properties will be sent along to Loggly. The level is sent as a category.

To use a durable logger (that will save messages localy if the connectio nto the server is unavailable, and resend once the connection has recovered), set the `bufferBaseFilename` argument in the `Loggly()` extension method.

```csharp
var log = new LoggerConfiguration()
    .WriteTo.Loggly(bufferBaseFilename:@"C:\test\buffer")
    .CreateLogger();
```

This will write unsent messages to a `buffer-{Date}.json` file in the specified folder (`C:\test\` in the example). 

The method also takes a `retainedFileCountLimit` parameter that will allow you to control how much info to store / ship when back online. By default, the value is `null` with the intent is to send all persisted data, no matter how old. If you specify a value, only the data in the last N buffer files will be shipped back, preventing stale data to be indexed (if that info is no longer usefull).
