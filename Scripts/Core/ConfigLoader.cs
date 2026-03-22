using System.Text.Json;
using System.Text.Json.Serialization;
using Sts2MapMod.Utils;

namespace Sts2MapMod.Core;

/// <summary>
/// Loads config once at startup and caches. No disk access during map rendering.
/// </summary>
public static class ConfigLoader
{
    private static MapColorConfig? _config;
    private static readonly object Lock = new();

    /// <summary>Path of the config file last read (or hot-reloaded).</summary>
    private static string? _trackedConfigPath;

    private static long _trackedWriteUtcTicks;
    private static long _lastHotReloadCheckTick;
    private const long HotReloadMinIntervalMs = 1200;

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static MapColorConfig Config
    {
        get
        {
            if (_config != null) return _config;
            lock (Lock)
            {
                _config ??= Load();
            }
            return _config;
        }
    }

    /// <summary>Populate cache from disk if empty (use at mod init).</summary>
    public static MapColorConfig EnsureLoaded()
    {
        lock (Lock)
        {
            _config ??= Load();
            return _config;
        }
    }

    /// <summary>Serialize cached config to <c>map_color_config.json.txt</c> beside the mod DLL.</summary>
    public static void SaveToDisk()
    {
        lock (Lock)
        {
            try
            {
                var cfg = _config ?? new MapColorConfig();
                foreach (var warning in MapConfigValidator.ValidateAndNormalize(cfg))
                    LogUtil.Warn($"Config: validation adjusted value before save - {warning}");
                var baseDir = Path.GetDirectoryName(typeof(ConfigLoader).Assembly.Location)
                    ?? AppContext.BaseDirectory;
                var path = Path.Combine(baseDir, "map_color_config.json.txt");
                var json = JsonSerializer.Serialize(cfg, JsonWriteOptions);
                File.WriteAllText(path, json);
                try
                {
                    _trackedConfigPath = path;
                    _trackedWriteUtcTicks = File.GetLastWriteTimeUtc(path).Ticks;
                }
                catch
                {
                    /* ignore */
                }

                LogUtil.Info($"Config: SaveToDisk OK path={path} bytes={json.Length} Enabled={cfg.Enabled}");
            }
            catch (Exception ex)
            {
                LogUtil.Error("Config: SaveToDisk failed (config not written).", ex);
            }
        }
    }

    /// <summary>
    /// Load config from disk (call only at mod init). Uses default path next to mod DLL.
    /// </summary>
    /// <remarks>
    /// STS2's mod manager treats every <c>*.json</c> in the mod folder as a mod manifest (must have <c>id</c>).
    /// Ship config as <c>map_color_config.json.txt</c> (JSON body) so the game does not try to load it as a mod.
    /// </remarks>
    public static MapColorConfig Load()
    {
        var baseDir = Path.GetDirectoryName(typeof(ConfigLoader).Assembly.Location)
            ?? AppContext.BaseDirectory;
        LogUtil.Info($"Config: Load() baseDir={baseDir}");

        try
        {
            foreach (var name in new[] { "map_color_config.json.txt", "map_color_config.json" })
            {
                var path = Path.Combine(baseDir, name);
                if (!File.Exists(path))
                {
                    LogUtil.Info($"Config: skip missing file name={name} fullPath={path}");
                    continue;
                }

                LogUtil.Info($"Config: reading file={name} fullPath={path}");
                string json;
                try
                {
                    json = File.ReadAllText(path);
                }
                catch (Exception ex)
                {
                    LogUtil.Error($"Config: File.ReadAllText failed for {path}", ex);
                    continue;
                }

                try
                {
                    var loaded = JsonSerializer.Deserialize<MapColorConfig>(json, JsonReadOptions);
                    if (loaded != null)
                    {
                        foreach (var warning in MapConfigValidator.ValidateAndNormalize(loaded))
                            LogUtil.Warn($"Config: validation adjusted loaded value - {warning}");
                        _trackedConfigPath = path;
                        try
                        {
                            _trackedWriteUtcTicks = File.GetLastWriteTimeUtc(path).Ticks;
                        }
                        catch
                        {
                            _trackedWriteUtcTicks = 0;
                        }

                        LogUtil.Info(
                            $"Config: deserialize OK from {name} Enabled={loaded.Enabled} ColorNodeIcon={loaded.ColorNodeIcon} VerifyGodotPrint={loaded.VerifyGodotPrint}");
                        return loaded;
                    }

                    LogUtil.Warn($"Config: deserialize returned null for {path} (using defaults).");
                }
                catch (Exception ex)
                {
                    LogUtil.Error($"Config: JSON deserialize failed for {path}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            LogUtil.Error("Config: Load() unexpected failure before/after file loop.", ex);
        }

        LogUtil.Warn("Config: using built-in defaults (no valid config file loaded).");
        return new MapColorConfig();
    }

    /// <summary>
    /// Reset cached config (e.g. for tests or config reload).
    /// </summary>
    public static void Reset()
    {
        lock (Lock)
        {
            _config = null;
            _trackedConfigPath = null;
            _trackedWriteUtcTicks = 0;
        }
    }

    /// <summary>
    /// If <paramref name="allow"/> and the on-disk config changed, replaces <see cref="Config"/> (throttled ~1.2s).
    /// Call from map refresh postfix so JSON tweaks apply without restarting the game.
    /// </summary>
    public static void TryHotReloadConfigFromDiskThrottled(bool allow)
    {
        if (!allow)
            return;

        var now = Environment.TickCount64;
        if (_lastHotReloadCheckTick != 0 && now - _lastHotReloadCheckTick < HotReloadMinIntervalMs)
            return;
        _lastHotReloadCheckTick = now;

        lock (Lock)
        {
            var baseDir = Path.GetDirectoryName(typeof(ConfigLoader).Assembly.Location)
                ?? AppContext.BaseDirectory;
            var path = _trackedConfigPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                path = null;
                foreach (var name in new[] { "map_color_config.json.txt", "map_color_config.json" })
                {
                    var p = Path.Combine(baseDir, name);
                    if (!File.Exists(p))
                        continue;
                    path = p;
                    break;
                }
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            long ticks;
            try
            {
                ticks = File.GetLastWriteTimeUtc(path).Ticks;
            }
            catch
            {
                return;
            }

            if (string.Equals(path, _trackedConfigPath, StringComparison.Ordinal) &&
                ticks == _trackedWriteUtcTicks)
                return;

            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<MapColorConfig>(json, JsonReadOptions);
                if (loaded == null)
                    return;
                foreach (var warning in MapConfigValidator.ValidateAndNormalize(loaded))
                    LogUtil.Warn($"Config: validation adjusted hot-reloaded value - {warning}");
                _config = loaded;
                _trackedConfigPath = path;
                _trackedWriteUtcTicks = ticks;
                LogUtil.Info($"Config: hot-reload applied path={path} Enabled={loaded.Enabled}");
            }
            catch (Exception ex)
            {
                LogUtil.Error($"Config: hot-reload failed path={path}", ex);
            }
        }
    }
}
