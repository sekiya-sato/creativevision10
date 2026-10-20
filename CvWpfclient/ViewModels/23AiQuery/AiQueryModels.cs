using CommunityToolkit.Mvvm.ComponentModel;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;
using System.IO;
using System.Text;

namespace CvWpfclient.ViewModels._23AiQuery;

/// <summary>会話履歴の1行</summary>
public sealed class AiQueryChatLine(EnumAiQueryRole role, string text, bool isError = false) {
	public EnumAiQueryRole Role { get; } = role;
	public string Text { get; } = text;
	public bool IsError { get; } = isError;
	public bool IsUser => Role == EnumAiQueryRole.User;
	public string Speaker => IsUser ? "あなた" : "AI";
}

/// <summary>パラメータ表の1行。値と実行時入力だけを画面で変更できる</summary>
public sealed partial class AiQueryParamRow : ObservableObject {
	public int Index { get; init; }
	public string Name { get; init; } = string.Empty;
	public string Label { get; init; } = string.Empty;
	public EnumAiQueryParamType Type { get; init; }
	public string Placeholder => $"@{Index}";
	public string TypeText => Type switch {
		EnumAiQueryParamType.Integer => "整数",
		EnumAiQueryParamType.Decimal => "小数",
		EnumAiQueryParamType.Date => "日付(yyyyMMdd)",
		_ => "文字",
	};

	[ObservableProperty]
	public partial string Value { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool AskOnRun { get; set; }

	public AiQueryParam ToParam() => new() { Name = Name, Label = Label, Type = Type, Value = Value, AskOnRun = AskOnRun };

	public static AiQueryParamRow From(AiQueryParam p, int index) => new() {
		Index = index, Name = p.Name, Label = p.Label, Type = p.Type, Value = p.Value, AskOnRun = p.AskOnRun,
	};
}

/// <summary>
/// 個人保存（PC内）の1件。共有保存（<see cref="SysAiQuery"/>）と同じ7項目だけを持つ。
/// Doc/spec/2026-10-19_AI-query_最小変更設計案.md §5.4
/// </summary>
public sealed record class AiQuerySaved {
	public string SaveDate { get; set; } = string.Empty;
	public string Title { get; set; } = string.Empty;
	public string SqlText { get; set; } = string.Empty;
	public string SqlParams { get; set; } = string.Empty;
	public long Id_Login { get; set; }
	public string Description { get; set; } = string.Empty;
	/// <summary>確定した条件（AIの要約）。旧ファイルには無く空文字になる</summary>
	public string SpecSummary { get; set; } = string.Empty;

	public string SaveDateText => FormatDay(SaveDate);

	public static AiQuerySaved From(SysAiQuery q) => new() {
		SaveDate = q.SaveDate, Title = q.Title, SqlText = q.SqlText, SqlParams = q.SqlParams, Id_Login = q.Id_Login, Description = q.Description,
		SpecSummary = q.SpecSummary,
	};

	internal static string FormatDay(string day) =>
		DateTime.TryParseExact(day, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
			? d.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : day;
}

/// <summary>
/// 個人保存のファイル（%LocalAppData%\CreativeVision10\AiQuery\personal.json）。
/// 自動共有しない。端末移行はエクスポート／インポートで行う。
/// </summary>
public static class AiQueryPersonalStore {
	/// <summary>保存ファイル。UATでは利用者の実ファイルを汚さないよう一時フォルダへ差し替える</summary>
	public static string FilePath { get; set; } = Path.Combine(ClientLib.GetDataDir(), "AiQuery", "personal.json");

	/// <summary>読み込む。ファイルが無ければ空。壊れていれば例外（呼び出し側で表示し、上書きしない）</summary>
	public static List<AiQuerySaved> Load(string? path = null) {
		path ??= FilePath;
		if (!File.Exists(path)) {
			return [];
		}
		return Common.DeserializeObject<List<AiQuerySaved>>(File.ReadAllText(path, Encoding.UTF8)) ?? [];
	}

	/// <summary>一時ファイルへ書いてから置き換え、書込み途中で既存の保存を壊さない</summary>
	public static void Save(IEnumerable<AiQuerySaved> items, string? path = null) {
		path ??= FilePath;
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		var temp = path + ".tmp";
		File.WriteAllText(temp, Common.SerializeObject(items.ToList()), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		File.Move(temp, path, overwrite: true);
	}
}
