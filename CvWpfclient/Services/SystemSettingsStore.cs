/*
# file name
SystemSettingsStore.cs

# description
ローカル設定ファイル (clientsettings.json) の読み書きを行うクラスを提供します。

*/
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.IO;

namespace CvWpfclient.Services;

public sealed class ClientSettingsStore {
	private const string FileName = "clientsettings.json";
	private readonly ILogger _logger;
	private readonly object _sync = new();

	public string FilePath { get; }

	/// <summary>
	/// システム設定ファイルの標準パスを取得します。
	/// ToDo: リリース段階ではClientLib.GetDataDir() に変更する
	/// </summary>
	public static string SettingsFilePath => Path.Combine(Helpers.ClientLib.GetDataDir(), FileName);

	public ClientSettingsStore(string? filePath = null) {
		_logger = App.AppHost!.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(ClientSettingsStore));
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
				_logger.LogWarning(ex, "clientsettings.json の読み込みに失敗したため初期値を使用します。");
				return new ClientSettingsDocument();
			}
		}
	}

	/// <summary>
	/// システム設定ファイルを保存します。
	/// </summary>
	public void Save(ClientSettingsDocument settings) {
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

public sealed class ClientSettingsDocument {
	public ClientConnectionString ConnectionStrings { get; set; } = new();
	public ClientParameters Parameters { get; set; } = new();
	public ClientApplication Application { get; set; } = new();
	public ClientJapanPos JapanPostBiz { get; set; } = new();
}

public sealed class ClientConnectionString {
	public string Url { get; set; } = "https://localhost:5012";
}

public sealed class ClientParameters {
	public string LoginId { get; set; } = string.Empty;
	/// <summary>
	/// ToDo: リリース時には暗号化するか、保存しないようにする
	/// </summary>
	public string LoginPass { get; set; } = string.Empty;
	/// <summary>
	/// ToDo: リリース時には暗号化するか、保存しないようにする
	/// </summary>
	public string LoginJwt { get; set; } = string.Empty;

}
public sealed class ClientApplication {
	public string OpenWeatherApiKey { get; set; } = string.Empty;
	public string WeatherRegion { get; set; } = string.Empty;
	public string FitPosition { get; set; } = string.Empty;
}

public sealed class ClientJapanPos {
	public string ClientId { get; set; } = string.Empty;
	public string SecretKey { get; set; } = string.Empty;
}
