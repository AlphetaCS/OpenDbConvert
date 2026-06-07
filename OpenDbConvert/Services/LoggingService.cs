using System;
using System.IO;

namespace OpenDbConvert.Services;

public class LoggingService(ConfigurationService configurationService)
{
    private readonly string _logOutputFile = configurationService.LogOutputFile;

    public void Log(string message)
    {
        Console.WriteLine(message);
        WriteToFile(message);
    }

    public void LogError(string message)
    {
        Console.Error.WriteLine(message);
        WriteToFile($"[ERROR] {message}");
    }

    private void WriteToFile(string message)
    {
        if (string.IsNullOrWhiteSpace(_logOutputFile))
            return;

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        File.AppendAllText(_logOutputFile, line + Environment.NewLine);
    }
}
