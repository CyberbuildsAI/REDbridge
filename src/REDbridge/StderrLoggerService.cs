using WolvenKit.Common;
using WolvenKit.Core.Interfaces;

namespace REDbridge;

/// <summary>
/// WolvenKit's logger interface, written to stderr so stdout stays free for the protocol.
/// One line per message: a timestamp, a level, the text.
/// </summary>
public sealed class StderrLoggerService : ILoggerService
{
    private readonly object _gate = new();

    public LoggerVerbosity LoggerVerbosity { get; private set; } = LoggerVerbosity.Normal;

    public void SetLoggerVerbosity(LoggerVerbosity verbosity) => LoggerVerbosity = verbosity;

    public void Info(string s) => Write("info ", s);
    public void Warning(string s) => Write("warn ", s);
    public void Error(string msg) => Write("error", msg);
    public void Error(Exception exception) => Write("error", exception.ToString());
    public void Success(string msg) => Write("ok   ", msg);

    public void Debug(string msg)
    {
        if (LoggerVerbosity >= LoggerVerbosity.Diagnostic)
        {
            Write("debug", msg);
        }
    }

    private void Write(string level, string text)
    {
        lock (_gate)
        {
            Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{level}] {text}");
        }
    }
}
