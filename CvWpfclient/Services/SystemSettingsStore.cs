using CvBase;
using CvWpfclient.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace CvWpfclient.Services;

public sealed class ClientSettingsStore {
	private const string FileName = "clientsettings.json";
	private static readonly ILogger<ClientSettingsStore> _bootstrapLogger = new NLogExtender<ClientSettingsStore>();
	private readonly object _sync = new();

	public string FilePath { get; }

	/// <summary>
	/// システム設定ファイルの標準パスを取得します。
	/// ToDo: リリース段階ではClientLib.GetDataDir() に変更する
	/// </summary>
	public static string SettingsFilePath => Path.Combine(Helpers.ClientLib.GetDataDir(), FileName);

	public ClientSettingsStore(string? filePath = null) {
		FilePath = string.IsNullOrWhiteSpace(filePath) ? SettingsFilePath : filePath!;
	}

	/// <summary>
	/// ローカル設定ファイルを読み込みます。
	/// </summary>
	public ClientSettingsDocument Load() {
		lock (_sync) {
			if (!File.Exists(FilePath)) {
				return new ClientSettingsDocument();
			}

			try {
				var content = File.ReadAllText(FilePath);
				if (string.IsNullOrWhiteSpace(content)) {
					return new ClientSettingsDocument();
				}

				return JsonConvert.DeserializeObject<ClientSettingsDocument>(content) ?? new ClientSettingsDocument();
			}
			catch (JsonException ex) {
				_bootstrapLogger.LogWarning(ex, "clientsettings.json の読み込みに失敗したため初期値を使用します。");
				return new ClientSettingsDocument();
			}
		}
	}

	/// <summary>
	/// システム設定ファイルを保存します。
	/// </summary>
	public void Save(ClientSettingsDocument settings) {
		ArgumentNullException.ThrowIfNull(settings);
		var overrides = ToConfigurationOverrides(settings);
		AddIfNotWhiteSpace(overrides, "Application:Theme", settings.Application.Theme);
		AddIfNotWhiteSpace(overrides, "Application:MainTheme", settings.Application.MainTheme);
		SaveConfigurationOverrides(overrides);
	}

	/// <summary>
	/// 指定された構成キーのみを既存の設定ファイルへ反映します。
	/// </summary>
	public void SaveConfigurationOverrides(IReadOnlyDictionary<string, string?> overrides) {
		ArgumentNullException.ThrowIfNull(overrides);
		if (overrides.Count == 0) {
			return;
		}

		lock (_sync) {
			var root = LoadWritableRoot();
			var hasChanges = false;
			foreach (var pair in overrides) {
				if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null) {
					continue;
				}

				SetValue(root, pair.Key, pair.Value);
				hasChanges = true;
			}

			if (!hasChanges) {
				return;
			}

			EnsureDirectoryExists();
			WriteJsonAtomically(root.ToString(Formatting.Indented));
		}
	}
	/// <summary>
	/// 設定が空白でないものをキーと値のペアとして返します。
	/// </summary>
	/// <param name="settings">クライアント設定ドキュメント</param>
	/// <returns>キーと値のペアの辞書</returns>
	public Dictionary<string, string?> ToConfigurationOverrides(ClientSettingsDocument settings) {
		var overrides = new Dictionary<string, string?>();
		AddIfNotWhiteSpace(overrides, "ConnectionStrings:Url", settings.ConnectionStrings.Url);
		AddIfNotWhiteSpace(overrides, "Parameters:LoginId", settings.Parameters.LoginId);
		AddIfNotWhiteSpace(overrides, "Parameters:LoginPass", settings.Parameters.LoginPass);
		AddIfNotWhiteSpace(overrides, "Parameters:LoginJwt", settings.Parameters.LoginJwt);
		AddIfNotWhiteSpace(overrides, "Application:WeatherRegion", settings.Application.WeatherRegion);
		AddIfNotWhiteSpace(overrides, "Application:FitPosition", settings.Application.FitPosition);
		AddIfNotWhiteSpace(overrides, "Application:MainTheme", settings.Application.MainTheme);
		return overrides;
	}
	static void AddIfNotWhiteSpace(IDictionary<string, string?> map, string key, string? value) {
		if (!string.IsNullOrWhiteSpace(value)) {
			map[key] = value;
		}
	}

	JObject LoadWritableRoot() {
		if (!File.Exists(FilePath)) {
			return new JObject();
		}

		var content = File.ReadAllText(FilePath);
		if (string.IsNullOrWhiteSpace(content)) {
			return new JObject();
		}

		try {
			var token = JToken.Parse(content);
			if (token is JObject root) {
				return root;
			}

			throw new JsonException("clientsettings.json のルート要素がオブジェクトではありません。");
		}
		catch (JsonException ex) {
			_bootstrapLogger.LogWarning(ex, "clientsettings.json の形式が不正なため保存を中止します。");
			throw new InvalidOperationException("clientsettings.json の形式が不正なため保存できません。内容を確認してください。", ex);
		}
	}

	void SetValue(JObject root, string keyPath, string value) {
		var segments = keyPath.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (segments.Length == 0) {
			return;
		}

		JObject current = root;
		for (var i = 0; i < segments.Length - 1; i++) {
			var segment = segments[i];
			if (current[segment] is null) {
				var created = new JObject();
				current[segment] = created;
				current = created;
				continue;
			}

			if (current[segment] is not JObject child) {
				throw new InvalidOperationException($"clientsettings.json の '{segment}' にオブジェクト以外の値があるため保存できません。");
			}

			current = child;
		}

		current[segments[^1]] = value;
	}

	void EnsureDirectoryExists() {
		var directory = Path.GetDirectoryName(FilePath);
		if (!string.IsNullOrWhiteSpace(directory)) {
			Directory.CreateDirectory(directory);
		}
	}

	void WriteJsonAtomically(string json) {
		var directory = Path.GetDirectoryName(FilePath) ?? Directory.GetCurrentDirectory();
		var tempFilePath = Path.Combine(directory, $"{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
		try {
			File.WriteAllText(tempFilePath, json);
			if (File.Exists(FilePath)) {
				File.Replace(tempFilePath, FilePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
			}
			else {
				File.Move(tempFilePath, FilePath);
			}
		}
		finally {
			if (File.Exists(tempFilePath)) {
				File.Delete(tempFilePath);
			}
		}
	}
}

