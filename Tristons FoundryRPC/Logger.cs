// =============================================================================
//  Triston's FoundryRPC  —  Logger.cs
//  Tiny thread-safe file logger with a verbosity switch. Writes to
//  %APPDATA%\Tristons FoundryRPC\foundryrpc.log (no console; this is a WinExe).
//
//  Author:  triston-dev   ·   https://github.com/triston-dev
//  Product: Triston's FoundryRPC
//  License: MIT (see LICENSE)
// =============================================================================

using System;
using System.IO;
using System.Text;

namespace TristonsFoundryRPC;

/// <summary>Log verbosity, lowest to highest noise.</summary>
public enum LogLevel
{
    Error = 0,
    Info = 1,
    Debug = 2,
}

/// <summary>
/// Minimal append-only file logger. One instance is shared across the app.
/// Rolls the file when it grows past <see cref="MaxBytes"/> so a long-running
/// tray session never fills the disk.
/// </summary>
public sealed class Logger
{
    private const long MaxBytes = 1_000_000; // ~1 MB before we roll to .old

    private readonly object _gate = new();
    private readonly string _path;

    public LogLevel Level { get; set; }

    public Logger(string path, LogLevel level)
    {
        _path = path;
        Level = level;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        }
        catch
        {
            // If we cannot even create the folder there is nowhere to log to;
            // swallow — logging must never take the app down.
        }
    }

    public void Error(string message) => Write(LogLevel.Error, message);
    public void Info(string message) => Write(LogLevel.Info, message);
    public void Debug(string message) => Write(LogLevel.Debug, message);

    /// <summary>Log an exception at Error level with a contextual prefix.</summary>
    public void Error(string context, Exception ex) =>
        Write(LogLevel.Error, $"{context}: {ex.GetType().Name}: {ex.Message}");

    private void Write(LogLevel level, string message)
    {
        if (level > Level)
            return;

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level,-5}] {message}{Environment.NewLine}";

        lock (_gate)
        {
            try
            {
                RollIfNeeded();
                File.AppendAllText(_path, line, Encoding.UTF8);
            }
            catch
            {
                // Never throw from the logger.
            }
        }
    }

    private void RollIfNeeded()
    {
        try
        {
            var fi = new FileInfo(_path);
            if (fi.Exists && fi.Length > MaxBytes)
            {
                var old = _path + ".old";
                if (File.Exists(old))
                    File.Delete(old);
                File.Move(_path, old);
            }
        }
        catch
        {
            // Rolling is best-effort.
        }
    }

    public string FilePath => _path;
}
