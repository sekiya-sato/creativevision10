using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace UatVm;

/// <summary>
/// 証跡をJSONLで追記する。1行1事象で、後からレポートへ機械変換できる形にする。
/// </summary>
public sealed class EvidenceWriter : IDisposable {
	private static readonly JsonSerializerOptions _json = new() {
		// 日本語をエスケープせずそのまま出す（人が読む証跡のため）。
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		WriteIndented = false,
	};

	private readonly StreamWriter _writer;
	private readonly object _sync = new();

	/// <summary>証跡ファイルのパス。</summary>
	public string FilePath { get; }

	public EvidenceWriter(string path) {
		FilePath = path;
		var dir = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		_writer = new StreamWriter(path, append: false, new UTF8Encoding(false)) { AutoFlush = true };
	}

	/// <summary>1事象を記録する。</summary>
	/// <param name="kind">事象の種別（host / view / command / dialog / check / fail など）。</param>
	/// <param name="name">事象の名前。</param>
	/// <param name="data">付随データ。匿名型でよい。</param>
	public void Write(string kind, string name, object? data = null) {
		var line = JsonSerializer.Serialize(new {
			ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
			kind,
			name,
			data,
		}, _json);
		lock (_sync) _writer.WriteLine(line);
		Console.WriteLine($"[{kind}] {name}{FormatForConsole(data)}");
	}

	private static string FormatForConsole(object? data) {
		if (data == null) return string.Empty;
		var text = JsonSerializer.Serialize(data, _json);
		return text.Length > 400 ? $" {text[..400]}…" : $" {text}";
	}

	public void Dispose() {
		lock (_sync) _writer.Dispose();
	}
}
