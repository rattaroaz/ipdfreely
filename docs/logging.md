# Logging pipeline

This app treats **`ILoggingService`** as the **single source of truth** for log
output. Every log line — from app code or from the host / framework — ends up
in the same on-disk file and the same in-memory ring buffer.

```
┌──────────────┐     ┌────────────────────────────┐
│  app code    │────▶│                            │
└──────────────┘     │                            │
                     │   ILoggingService          │
┌──────────────┐     │   (LoggingService)         │
│ MAUI / host  │     │   ├─ file writer (chan)    │
│ libraries    │───▶ │   ├─ ring buffer (100)     │
│ (ILogger)    │     │   ├─ rotation / retention  │
└──────┬───────┘     │   └─ level filter          │
       │             └────────────────────────────┘
       │                         ▲
       │                         │ forwards
       ▼                         │
┌────────────────────────────────┴──────────┐
│ LoggingServiceForwardingLoggerProvider    │
│  (registered as ILoggerProvider)          │
└───────────────────────────────────────────┘
```

## Wiring

`MauiProgram.CreateMauiApp()` calls `AppServices.Register(IServiceCollection)`
which makes the graph explicit and testable:

```csharp
services.AddSingleton<ILoggingService, LoggingService>();
services.AddSingleton<ILoggerProvider, LoggingServiceForwardingLoggerProvider>();
```

`LoggingServiceForwardingLoggerProvider` takes the single `ILoggingService`
instance and exposes an `ILogger` for every category name. Anything that logs
through `Microsoft.Extensions.Logging.ILogger<T>` — MAUI infrastructure, HTTP
handlers, library code — is routed into the same pipeline.

## Level filtering

There are two levels to be aware of:

| Level                                  | Controls                                              |
|----------------------------------------|-------------------------------------------------------|
| `LoggingService.MinimumLevel`          | The app-level filter applied to both paths.           |
| `ILoggerFactory.SetMinimumLevel(...)`  | The M.E.L factory filter applied to `ILogger` callers. |

The bridge honours `LoggingService.MinimumLevel` inside `IsEnabled(...)`, so a
call like `logger.LogDebug(...)` is dropped if `MinimumLevel > Debug`, *even if*
the host factory would otherwise allow it. The reverse is also true: if the
host factory is set to `Information`, a `LogTrace` won't reach the bridge in
the first place — raise the factory floor to `Trace`/`Debug` when you need
low-level diagnostics.

## What is persisted

* A **log file** under `FileSystem.AppDataDirectory/Logs/` (falls back to
  `%TEMP%/ipdfreely_tests/Logs/` when MAUI essentials aren't available, e.g.
  in unit tests). Files are rotated at 10 MB and retained for 7 days.
* A **100-entry in-memory ring buffer** reachable via
  `ILoggingService.GetRecentLogs(count)`. Used in tests and could be exposed
  in the UI as a diagnostics pane.

## Scopes

`ILogger.BeginScope(state)` currently returns a no-op disposable. Category
name and `EventId` *are* preserved in the output. If you need scope state in
the log lines, that's a focused follow-up on
`LoggingServiceForwardingLoggerProvider` — not a limitation of
`LoggingService` itself.

## Testing

* `LoggingServiceTests` — format, levels, rotation-safe behaviour,
  concurrency, dispose.
* `LoggingServiceForwardingLoggerProviderTests` — `IsEnabled` matrix, level
  mapping, `EventId`, empty category/message, scope disposability.
* `AppServicesCompositionTests` — verifies `ILoggingService` and
  `ILoggerProvider` resolve as singletons and an `ILogger` pulled from the
  container writes to the same `LoggingService` instance.
