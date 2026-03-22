using Godot;

namespace Sts2MapMod.Utils;

/// <summary>
/// Logging. Uses game Logger (visible in getlogs) when available; otherwise Godot + Console.
/// Prefix <c>[MapColorMod]</c> keeps lines easy to grep in godot.log / getlogs.
/// </summary>
public static class LogUtil
{
    private static readonly object FileLock = new();
    private static string? _logFilePath;

    public static void Debug(string message)
    {
        try { Sts2MapMod.Entry.Logger?.Info("[DBG] " + message); } catch { /* ignore */ }
        GD.Print("[MapColorMod] [DBG] " + message);
        WriteFile("DBG", message);
    }

    public static void Info(string message)
    {
        try { Sts2MapMod.Entry.Logger?.Info(message); } catch { /* ignore */ }
        GD.Print("[MapColorMod] " + message);
        WriteFile("INF", message);
    }

    public static void Warn(string message)
    {
        try { Sts2MapMod.Entry.Logger?.Warn(message); } catch { try { Sts2MapMod.Entry.Logger?.Error(message); } catch { } }
        GD.PushWarning("[MapColorMod] WARN: " + message);
        WriteFile("WRN", message);
    }

    /// <summary>Errors and exceptions — full text goes to game logger and <see cref="GD.PrintErr"/>.</summary>
    public static void Error(string message, Exception? ex = null)
    {
        var body = ex == null ? message : $"{message}\n{ex}";
        try { Sts2MapMod.Entry.Logger?.Error(body); } catch { try { Sts2MapMod.Entry.Logger?.Warn(body); } catch { } }
        GD.PrintErr("[MapColorMod] ERROR: " + body);
        WriteFile("ERR", body);
    }

    /// <summary>Logs only when <paramref name="condition"/> is true (e.g. config.VerifyGodotPrint).</summary>
    public static void Diag(bool condition, string message)
    {
        if (!condition) return;
        Info("[Diag] " + message);
    }

    private static void WriteFile(string level, string message)
    {
        try
        {
            lock (FileLock)
            {
                _logFilePath ??= ResolveLogFilePath();
                if (string.IsNullOrEmpty(_logFilePath))
                    return;
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{System.Environment.NewLine}";
                File.AppendAllText(_logFilePath, line);
            }
        }
        catch
        {
            // Never let file logging break the mod.
        }
    }

    private static string? ResolveLogFilePath()
    {
        try
        {
            var baseDir = Path.GetDirectoryName(typeof(Sts2MapMod.Entry).Assembly.Location)
                ?? AppContext.BaseDirectory;
            return Path.Combine(baseDir, "map_color_debug.log");
        }
        catch
        {
            return null;
        }
    }
}
