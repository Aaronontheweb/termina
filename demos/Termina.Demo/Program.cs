using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Termina.Demo;
using Termina.Demo.Pages;
using Termina.Diagnostics;
using Termina.Hosting;
using Termina.Input;
using Termina.Pages;

// Check for --test flag (used in CI/CD to run scripted test and exit)
var testMode = args.Contains("--test");

// Set up diagnostic tracing with timestamped log file in temp directory
var traceDir = Path.Combine(Path.GetTempPath(), "termina-logs");
Directory.CreateDirectory(traceDir);
var traceFile = Path.Combine(traceDir, $"trace-{DateTime.Now:yyyyMMdd-HHmmss}.log");

var builder = Host.CreateApplicationBuilder(args);

// Suppress host/console logging so it doesn't bleed into the TUI's rendered output.
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// Enable file tracing and register the path for UI display
builder.Services.AddTerminaFileTracing(traceFile, TerminaTraceCategory.All, TerminaTraceLevel.Debug);
builder.Services.AddSingleton(new TraceFileInfo(traceFile));

// Set up input source based on mode
VirtualInputSource? scriptedInput = null;
if (testMode)
{
    scriptedInput = new VirtualInputSource();
    builder.Services.AddTerminaVirtualInput(scriptedInput);
}

// Register Termina with reactive pages using route-based navigation
// Use PreserveState so state persists when navigating between pages
builder.Services.AddTermina("/counter", termina =>
{
    termina.RegisterRoute<CounterPage, CounterViewModel>("/counter", NavigationBehavior.PreserveState);
    termina.RegisterRoute<TodoListPage, TodoListViewModel>("/todos", NavigationBehavior.PreserveState);
    termina.RegisterRoute<UnicodePage, CjkDemoViewModel>("/unicode", NavigationBehavior.PreserveState);
});

var host = builder.Build();

if (testMode && scriptedInput != null)
{
    // Queue up scripted input to test basic functionality then quit
    _ = Task.Run(async () =>
    {
        await Task.Delay(500); // Wait for initial render

        // Test counter: increment twice
        scriptedInput.EnqueueKey(ConsoleKey.UpArrow);
        scriptedInput.EnqueueKey(ConsoleKey.UpArrow);

        // Navigate to todo list
        scriptedInput.EnqueueKey(ConsoleKey.T);
        await Task.Delay(200);

        // Navigate in todo list
        scriptedInput.EnqueueKey(ConsoleKey.DownArrow);
        scriptedInput.EnqueueKey(ConsoleKey.Spacebar); // Toggle item

        // Quit
        scriptedInput.EnqueueKey(ConsoleKey.Q);
        scriptedInput.Complete();
    });
}

await host.RunAsync();
