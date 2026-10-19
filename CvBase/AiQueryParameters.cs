namespace CvBase;

// AI照会（Doc/spec/2026-10-19_AI-query_最小変更設計案.md §5.3）のパラメータ。
// 既存パラメータと同じく CvMsg.DataMsg にJSONで載せる。gRPC契約は増やさない。
// Common.SerializeObject は既定値を省略するため、列挙の0値・false・空リストはプロパティ初期値と一致させる。

/// <summary>対話の種類</summary>
public enum EnumAiQueryMode {
	/// <summary>質問・条件確認の対話</summary>
	Chat = 0,
	/// <summary>確定条件からSQLを生成</summary>
	Generate = 1,
}

/// <summary>対話応答の状態</summary>
public enum EnumAiQueryStatus {
	/// <summary>失敗（確定条件・最後のSQLは保持）</summary>
	Failed = 0,
	/// <summary>不足条件を質問中</summary>
	Asking = 1,
	/// <summary>条件の確認待ち</summary>
	Confirming = 2,
	/// <summary>SQL生成・検査済み</summary>
	Generated = 3,
}

/// <summary>会話メッセージの発言者</summary>
public enum EnumAiQueryRole {
	User = 0,
	Assistant = 1,
}

/// <summary>SQLパラメータの型。数値型はSQL側で CAST(@n AS …) して使う</summary>
public enum EnumAiQueryParamType {
	Text = 0,
	Integer = 1,
	Decimal = 2,
	/// <summary>yyyyMMdd 文字列</summary>
	Date = 3,
}

/// <summary>会話の1発言</summary>
public sealed record class AiQueryMessage {
	public EnumAiQueryRole Role { get; set; } = EnumAiQueryRole.User;
	public string Text { get; set; } = string.Empty;
}

/// <summary>
/// 対話で確定する照会条件。LLMが更新し、画面は確認要約の表示に使う。
/// SQLの完全な構造化表現ではない（基本設計 §5.3）。
/// </summary>
public sealed record class AiQuerySpec {
	public string Target { get; set; } = string.Empty;
	public List<string> Metrics { get; set; } = [];
	public List<string> GroupBy { get; set; } = [];
	public string DateBasis { get; set; } = string.Empty;
	public string PeriodFrom { get; set; } = string.Empty;
	public string PeriodTo { get; set; } = string.Empty;
	public List<string> Filters { get; set; } = [];
	public string TaxMode { get; set; } = string.Empty;
	public string ReturnMode { get; set; } = string.Empty;
	public List<string> OutputColumns { get; set; } = [];
	public List<string> OrderBy { get; set; } = [];
	public List<string> Unresolved { get; set; } = [];
}

/// <summary>SQLパラメータ1件。リストの並び順が @0,@1,… に対応する</summary>
public sealed record class AiQueryParam {
	public string Name { get; set; } = string.Empty;
	public string Label { get; set; } = string.Empty;
	public EnumAiQueryParamType Type { get; set; } = EnumAiQueryParamType.Text;
	public string Value { get; set; } = string.Empty;
	/// <summary>保存SQLの再実行時に値の入力を促す</summary>
	public bool AskOnRun { get; set; }
}

/// <summary>生成されたSQLと付随情報</summary>
public sealed record class AiQuerySqlDraft {
	public string Sql { get; set; } = string.Empty;
	public List<AiQueryParam> Parameters { get; set; } = [];
	public List<string> Columns { get; set; } = [];
	public string Title { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string Reason { get; set; } = string.Empty;
}

/// <summary>Msg091_AiQueryChat の要求</summary>
public sealed record class AiQueryChatParam {
	public string ConversationId { get; set; } = string.Empty;
	public int Revision { get; set; }
	/// <summary>会話開始日 yyyyMMdd（「先月」等の基準）</summary>
	public string BaseDate { get; set; } = string.Empty;
	public EnumAiQueryMode Mode { get; set; } = EnumAiQueryMode.Chat;
	public List<AiQueryMessage> Messages { get; set; } = [];
	public AiQuerySpec? Spec { get; set; }
	public List<string> KnownTables { get; set; } = [];
	/// <summary>保存SQLを会話で修正する時の出発点</summary>
	public AiQuerySqlDraft? BaseSql { get; set; }
}

/// <summary>Msg091_AiQueryChat の応答</summary>
public sealed record class AiQueryChatResult {
	public string ConversationId { get; set; } = string.Empty;
	public int Revision { get; set; }
	public EnumAiQueryStatus Status { get; set; } = EnumAiQueryStatus.Failed;
	public string AssistantText { get; set; } = string.Empty;
	public AiQuerySpec? Spec { get; set; }
	public List<string> KnownTables { get; set; } = [];
	public AiQuerySqlDraft? Draft { get; set; }
	public List<string> Errors { get; set; } = [];
}

/// <summary>Msg092_AiQueryValidate の要求</summary>
public sealed record class AiQueryValidateParam {
	public string Sql { get; set; } = string.Empty;
	public List<AiQueryParam> Parameters { get; set; } = [];
}

/// <summary>Msg092_AiQueryValidate の応答</summary>
public sealed record class AiQueryValidateResult {
	public bool IsValid { get; set; }
	public List<string> Errors { get; set; } = [];
	/// <summary>SQLが参照する表</summary>
	public List<string> Tables { get; set; } = [];
}
