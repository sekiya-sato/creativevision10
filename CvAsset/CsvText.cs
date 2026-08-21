using System.Text;

namespace CvAsset;

/// <summary>
/// CSV 1行ぶんの解析結果。
/// <para><see cref="LineNo"/> は 1 起点。引用符内の改行を含む行では、その行の先頭の物理行番号を持つ。</para>
/// </summary>
public sealed record CsvTextRow(int LineNo, List<string> Fields);

/// <summary>
/// RFC4180 相当のCSVテキスト解析・組み立て。
/// <para>
/// 外部CSVマスタ取込・取込レイアウト作成・残高登録処理で共有する。
/// 文字コードの判定と入出力は呼び出し側の責任とする。
/// </para>
/// </summary>
public static class CsvText {
	/// <summary>
	/// CSVテキストを行×フィールドへ分解する。引用符内のカンマ・改行はフィールドの一部として扱う。
	/// </summary>
	/// <exception cref="InvalidDataException">引用符が閉じられていない場合</exception>
	public static List<CsvTextRow> Parse(string text) {
		List<CsvTextRow> rows = [];
		List<string> fields = [];
		StringBuilder current = new();
		var inQuotes = false;
		var lineNo = 1;
		var rowStartLine = 1;

		for (var index = 0; index < text.Length; index++) {
			var ch = text[index];
			if (ch == '"') {
				if (inQuotes && index + 1 < text.Length && text[index + 1] == '"') {
					current.Append('"');
					index++;
				}
				else {
					inQuotes = !inQuotes;
				}
				continue;
			}

			if (ch == ',' && !inQuotes) {
				fields.Add(current.ToString());
				current.Clear();
				continue;
			}

			if ((ch == '\r' || ch == '\n') && !inQuotes) {
				fields.Add(current.ToString());
				current.Clear();
				rows.Add(new CsvTextRow(rowStartLine, fields));
				fields = [];
				if (ch == '\r' && index + 1 < text.Length && text[index + 1] == '\n') {
					index++;
				}
				lineNo++;
				rowStartLine = lineNo;
				continue;
			}

			if (ch == '\n') {
				lineNo++;
			}
			current.Append(ch);
		}

		if (inQuotes) {
			throw new InvalidDataException($"{rowStartLine}行目: CSVの引用符が閉じられていません。");
		}
		if (current.Length > 0 || fields.Count > 0) {
			fields.Add(current.ToString());
			rows.Add(new CsvTextRow(rowStartLine, fields));
		}

		return rows;
	}

	/// <summary>フィールド列を1行のCSV文字列へ組み立てる。</summary>
	public static string BuildLine(IEnumerable<string> fields) =>
		string.Join(",", fields.Select(EscapeField));

	/// <summary>カンマ・引用符・改行を含む場合だけ引用符で囲む。</summary>
	public static string EscapeField(string? value) {
		var text = value ?? string.Empty;
		if (text.Contains('"')) {
			text = text.Replace("\"", "\"\"");
		}

		return text.IndexOfAny([',', '"', '\r', '\n']) >= 0
			? $"\"{text}\""
			: text;
	}
}
