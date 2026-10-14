using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using StatisticsAnalysisTool.Updater;

if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Updater handoff smoke test must run on Windows.");

var root = Path.Combine(Path.GetTempPath(), "icc-handoff-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    // Fail closed if a newer NetSparkle changes its script format.
    var strangePath = Path.Combine(root, "unknown.cmd");
    File.WriteAllText(strangePath, "@echo off\r\nrem unknown updater\r\n");
    Check(!NetSparkleHandoffScript.TryPatch(strangePath, out _) &&
          File.ReadAllText(strangePath).Contains("unknown updater"),
          "Unknown handoff must not be executed or modified");

    // A real Windows process stays alive long enough to demonstrate that the
    // detached command does NOT run the installer while the parent is open.
    using var parent = Process.Start(new ProcessStartInfo("powershell.exe")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        ArgumentList = { "-NoProfile", "-Command", "Start-Sleep -Seconds 5" }
    }) ?? throw new InvalidOperationException("Cannot start test parent");
    var marker = Path.Combine(root, "installer-ran.txt");
    var path = Path.Combine(root, "handoff.cmd");

    // NetSparkle 3.1.0 Windows script snippet, including the broken TIMEOUT.
    var original = $@"@echo off
set /A counter=0
setlocal ENABLEDELAYEDEXPANSION
:loop
set /A counter=!counter!+1
if !counter! == 90 (
    exit /b 1
)
tasklist | findstr ""\<{parent.Id}\>"" > nul
if not errorlevel 1 (
    timeout /t 1 > nul
    goto :loop
)
:install
echo installed>""{marker}""
endlocal
";
    File.WriteAllText(path, original);
    Check(NetSparkleHandoffScript.TryPatch(path, out var failure),
        "Real NetSparkle handoff shape rejected: " + failure);
    var updated = File.ReadAllText(path);
    Check(updated.Contains(NetSparkleHandoffScript.ReliableWait) &&
          updated.Contains(NetSparkleHandoffScript.NewLimit) &&
          !updated.Contains(NetSparkleHandoffScript.OriginalWait),
          "Noninteractive-safe wait and longer deadline missing");

    using var script = Process.Start(new ProcessStartInfo("cmd.exe")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        ArgumentList = { "/d", "/c", path }
    }) ?? throw new InvalidOperationException("Cannot start detached cmd");

    Thread.Sleep(750);
    Check(!parent.HasExited, "Synthetic old client already exited unexpectedly");
    Check(!File.Exists(marker), "Installer started while old process was still alive");

    Check(parent.WaitForExit(15000), "Old process failed to quit");
    Check(script.WaitForExit(15000), "Detached handoff did not finish after parent exited");
    Check(script.ExitCode == 0, "Detached handoff failed, exit=" + script.ExitCode);
    Check(File.Exists(marker), "Installer was never invoked after client shutdown");

    Console.WriteLine("PASS: NetSparkle 3.1.0 wait is noninteractive-safe and runs only after parent exits");
    Console.WriteLine("PASS: unknown script format fails closed");
}
finally
{
    try { Directory.Delete(root, true); } catch (IOException) { }
}

static void Check(bool okay, string message)
{
    if (!okay) throw new InvalidOperationException(message);
}
