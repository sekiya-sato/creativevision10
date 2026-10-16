using System.Text.Json;

namespace UatVm.Seed;

/// <summary>
/// 開発DB（<c>CvServer/appsettings.Development.json</c> の <c>ConnectionStrings:sqlite</c>）の所在を設定から求める。
/// <para>
/// DBファイル名をコードに固定しないためのヘルパー。appsettings はコメントを含むため、コメント・末尾カンマを許容して読む。
/// 値はCvServerをカレントにして起動したときの相対パスなので、<c>CvServer</c> フォルダ基準で解決する。
/// UatVmSeed 以外（uat20260926 の ReportRunner 等）からはソースをリンクして使う。
/// </para>
/// </summary>
public static class DevDb {
	/// <summary>開発DBの接続文字列名</summary>
	public const string SqliteKey = "sqlite";
	/// <summary>旧開発DBの接続文字列名</summary>
	public const string OldSqliteKey = "old-sqlite";

	private static readonly JsonDocumentOptions JsonOptions = new() {
		CommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	/// <summary>開発DBのフルパスを返す。設定が読めない場合は例外。</summary>
	/// <param name="cvServerDir"><c>CvServer</c> フォルダ</param>
	public static string GetPath(string cvServerDir) {
		var settings = Path.Combine(cvServerDir, "appsettings.Development.json");
		var name = ReadConnectionString(settings, SqliteKey)
			?? throw new InvalidOperationException($"{settings} に ConnectionStrings:{SqliteKey} がありません。");
		return Path.GetFullPath(Path.Combine(cvServerDir, name));
	}

	/// <summary>リポジトリルートから開発DBのフルパスを返す</summary>
	public static string GetPathFromRepoRoot(string repoRoot) => GetPath(Path.Combine(repoRoot, "CvServer"));

	/// <summary>
	/// 指定DBが <c>CvServer</c> フォルダの appsettings*.json に開発DB・旧開発DBとして定義されたファイルか判定する。
	/// 親フォルダが <c>CvServer</c> で設定を読めない場合は、安全側に倒して true を返す。
	/// </summary>
	public static bool IsDevDb(string dbPath) {
		var full = Path.GetFullPath(dbPath);
		var dir = Path.GetDirectoryName(full);
		if (dir is null || !string.Equals(Path.GetFileName(dir), "CvServer", StringComparison.OrdinalIgnoreCase)) return false;
		try {
			var settingsFiles = Directory.GetFiles(dir, "appsettings*.json");
			if (settingsFiles.Length == 0) return true;
			foreach (var settings in settingsFiles) {
				foreach (var key in new[] { SqliteKey, OldSqliteKey }) {
					var name = ReadConnectionString(settings, key);
					if (name is null) continue;
					if (string.Equals(Path.GetFullPath(Path.Combine(dir, name)), full, StringComparison.OrdinalIgnoreCase)) return true;
				}
			}
			return false;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) {
			return true;
		}
	}

	/// <summary>appsettings の ConnectionStrings から値を読む。キーがなければ null。</summary>
	private static string? ReadConnectionString(string settingsPath, string key) {
		using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath), JsonOptions);
		if (!doc.RootElement.TryGetProperty("ConnectionStrings", out var cs) || cs.ValueKind != JsonValueKind.Object) return null;
		if (!cs.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String) return null;
		var text = value.GetString();
		return string.IsNullOrWhiteSpace(text) ? null : text;
	}
}
