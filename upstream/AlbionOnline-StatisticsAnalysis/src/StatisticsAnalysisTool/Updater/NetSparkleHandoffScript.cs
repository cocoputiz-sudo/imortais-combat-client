using System;
using System.IO;
using System.Text;

namespace StatisticsAnalysisTool.Updater;

/// <summary>
/// NetSparkle 3.1.0 generates a hidden Windows .cmd with "timeout /t 1" to
/// poll for its parent to exit. Windows TIMEOUT can fail immediately when
/// the command script has no interactive stdin, exhausting all 90 retries
/// before WPF has shut down. This guards and patches ONLY that exact pattern.
/// The downloaded signed EXE and its installer arguments are never changed.
/// </summary>
internal static class NetSparkleHandoffScript
{
    internal const string OriginalWait = "timeout /t 1 > nul";
    internal const string ReliableWait = "ping -n 2 127.0.0.1 > nul";
    internal const string OriginalLimit = "if !counter! == 90 (";
    internal const string NewLimit = "if !counter! == 120 (";

    internal static bool TryPatch(string? batchPath, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(batchPath) ||
            !string.Equals(Path.GetExtension(batchPath), ".cmd", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(batchPath))
        {
            error = "NetSparkle handoff script missing or not .cmd.";
            return false;
        }

        try
        {
            var original = File.ReadAllText(batchPath);
            if (Count(original, OriginalWait) != 1 ||
                Count(original, OriginalLimit) != 1 ||
                !original.Contains("tasklist | findstr", StringComparison.Ordinal) ||
                !original.Contains(":install", StringComparison.Ordinal))
            {
                error = "Unknown NetSparkle .cmd handoff format. Refusing unsafe installation.";
                return false;
            }

            var patched = original.Replace(OriginalWait, ReliableWait, StringComparison.Ordinal)
                                  .Replace(OriginalLimit, NewLimit, StringComparison.Ordinal);
            File.WriteAllText(batchPath, patched, new UTF8Encoding(false));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = e.GetType().Name + ": " + e.Message;
            return false;
        }
    }

    private static int Count(string text, string term)
    {
        var count = 0;
        var position = 0;
        while ((position = text.IndexOf(term, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += term.Length;
        }
        return count;
    }
}
