# Serilog.Sinks.Loggly

[![Build status](https://ci.appveyor.com/api/projects/status/x2ob36tl8brpkkjf/branch/master?svg=true)](https://ci.appveyor.com/project/serilog/serilog-sinks-loggly/branch/master)

[Loggly](http://www.loggly.com) is a cloud based log management service. Create a new input and specify that you want to use a http input with JSON enabled. Use the [loggly-csharp-configuration](https://github.com/neutmute/loggly-csharp) XML configuration syntax to configure the sink.

**Package** - [Serilog.Sinks.Loggly](http://nuget.org/packages/serilog.sinks.loggly)
| **Platforms** - .NET 4.5

```csharp
var log = new LoggerConfiguration()
    .WriteTo.Loggly()
    .CreateLogger();
```

Properties will be send along to Loggly. The level is send as category.
