using Cvnet10Wpfclient.Util;
using Newtonsoft.Json;
using NLog;
using System.IO;

namespace Cvnet10Wpfclient.Services;

public sealed class SystemSettingsStore {
    private const string FileName = "systemsettings.json";
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly object _sync = new();

    public string FilePath { get; }

    /// <summary>
    /// システム設定ファイルの標準パスを取得します。
    /// </summary>
    public static string SettingsFilePath => Path.Combine(ClientLib.GetDataDir(), FileName);

    public SystemSettingsStore(string? filePath = null) {
        FilePath = string.IsNullOrWhiteSpace(filePath) ? SettingsFilePath : filePath!;
    }

    /// <summary>
    /// システム設定ファイルを読み込みます。
    /// </summary>
    public SystemSettingsDocument Load() {
        lock (_sync) {
            if (!File.Exists(FilePath)) {
                return new SystemSettingsDocument();
            }

            try {
                var content = File.ReadAllText(FilePath);
                if (string.IsNullOrWhiteSpace(content)) {
                    return new SystemSettingsDocument();
                }

                return JsonConvert.DeserializeObject<SystemSettingsDocument>(content) ?? new SystemSettingsDocument();
            }
            catch (JsonException ex) {
                Logger.Warn(ex, "systemsettings.json の読み込みに失敗したため初期値を使用します。");
                return new SystemSettingsDocument();
            }
        }
    }

    /// <summary>
    /// システム設定ファイルを保存します。
    /// </summary>
    public void Save(SystemSettingsDocument settings) {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
        lock (_sync) {
            File.WriteAllText(FilePath, json);
        }
    }
}

public sealed class SystemSettingsDocument {
    public ConnectionStringsSettings ConnectionStrings { get; set; } = new();
    public ParametersSettings Parameters { get; set; } = new();
}

public sealed class ConnectionStringsSettings {
    public string? Url { get; set; }
}

public sealed class ParametersSettings {
    public string? LoginId { get; set; }
    public string? LoginPass { get; set; }
}
