using CvBase;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text;

namespace CvDomainLogic.AiQuery;

/// <summary>
/// AI照会でLLMへ渡すツール定義・ツール実行・プロンプト組立（Doc/spec/2026-10-19_AI-query_最小変更設計案.md §6.2）。
/// <para>
/// 情報取得ツール（list_tables / describe_table）は cv-sqlite(McpSql) と同じ取得方法で、結果行は返さない。
/// 応答は終端ツール（Chat: reply / Generate: submit_sql）の引数JSONで受け取り、本文テキストから抽出しない。
/// AI SDK・特定ベンダーのAPI形式には依存しない（OpenAI互換の tools 形式のJSONだけを扱う）。
/// </para>
/// </summary>
public sealed class AiQueryTools(ExDatabase db) {
	public const string ListTables = "list_tables";
	public const string DescribeTable = "describe_table";
	public const string Reply = "reply";
	public const string SubmitSql = "submit_sql";

	readonly AiQuerySchemaCatalog _catalog = new(db);

	/// <summary>LLMへ渡すツール定義。終端ツールは Mode ごとに1つだけ渡す。</summary>
	public static JArray Definitions(EnumAiQueryMode mode) {
		var tools = new JArray {
			Function(ListTables, "CV10のテーブル一覧（名前と説明）を返す。必要な表の候補を探すときに使う。",
				new JObject { ["name_prefix"] = Str("テーブル名の前方一致（例 Tran, Master）。省略可") }, []),
			Function(DescribeTable, "テーブルの列（型・主キー・説明）とJSON列の内部項目を返す。SQLに使う列は必ずこれで確認する。",
				new JObject { ["table"] = Str("テーブル名") }, ["table"]),
		};
		if (mode == EnumAiQueryMode.Chat) {
			tools.Add(Function(Reply, "利用者への応答。条件が足りなければ action=ask で質問し、すべて決まったら action=confirm で条件を要約する。",
				new JObject {
					["action"] = new JObject { ["type"] = "string", ["enum"] = new JArray("ask", "confirm") },
					["message"] = Str("利用者へ表示する日本語の文。confirm では集計対象・項目・集計単位・期間（具体的な日付）・条件・出力列・並び順を箇条書きで要約する"),
					["spec"] = SpecSchema(),
					["unresolved"] = StrArray("まだ決まっていない事項。confirm のときは空配列"),
				}, ["action", "message", "spec", "unresolved"]));
		}
		else {
			tools.Add(Function(SubmitSql, "確定した条件から作成したSQLite照会SQLを提出する。",
				new JObject {
					["sql"] = Str("SQLiteの SELECT または WITH ... SELECT を1文。値は @0,@1,... のパラメータ"),
					["parameters"] = new JObject {
						["type"] = "array",
						["description"] = "@0 から順に並べたパラメータ",
						["items"] = new JObject {
							["type"] = "object",
							["properties"] = new JObject {
								["name"] = Str("英数字の短い名前"),
								["label"] = Str("日本語の表示名（例 期間開始）"),
								["type"] = new JObject { ["type"] = "string", ["enum"] = new JArray("Text", "Integer", "Decimal", "Date") },
								["value"] = Str("値。日付は yyyyMMdd"),
							},
							["required"] = new JArray("name", "label", "type", "value"),
						},
					},
					["columns"] = StrArray("出力列の名前（SQLの別名と同じ順）"),
					["title"] = Str("この照会の短いタイトル（30文字以内）"),
					["description"] = Str("集計対象・項目・条件・集計単位の日本語説明"),
					["reason"] = Str("SQLの組み立て方の簡潔な理由"),
				}, ["sql", "parameters", "columns", "title", "description", "reason"]));
		}
		return tools;
	}

	/// <summary>システムプロンプトと会話履歴から messages を組み立てる。</summary>
	public JArray BuildMessages(AiQueryChatParam param, string dictionary, int maxMessages) {
		var sb = new StringBuilder();
		sb.AppendLine("あなたは販売管理システムCreativeVision10(CV10)のデータを集計するSQLite照会SQLの作成を補助する担当者です。");
		sb.AppendLine("利用者は一般の担当者でSQLを知りません。会話は日本語で行います。");
		sb.AppendLine();
		sb.AppendLine("# 規則");
		sb.AppendLine("- 実在しない表・列を使わない。SQLに使う列は describe_table で確認してから使う。表の候補は list_tables で探す。");
		sb.AppendLine("- 要求だけでは決まらない点（対象の伝票、日付の基準、税込/税抜、返品・値引の扱い、集計単位など）は推測で決めず質問する。回答済みの事項は繰り返し質問しない。");
		sb.AppendLine("- 期間は下の基準日から具体的な開始日・終了日（yyyyMMdd）に確定し、要約に書く。");
		sb.AppendLine("- 更新系SQL、PRAGMA、Sys で始まる表は使わない。");
		sb.AppendLine("- 利用者への応答は必ずツール（" + (param.Mode == EnumAiQueryMode.Chat ? Reply : SubmitSql) + "）で返す。");
		sb.AppendLine();
		sb.AppendLine("# 基準日");
		AppendPeriods(sb, param.BaseDate);
		sb.AppendLine();
		if (!string.IsNullOrWhiteSpace(dictionary)) {
			sb.AppendLine("# 業務辞書");
			sb.AppendLine(dictionary.Trim());
			sb.AppendLine();
		}
		// サーバは会話状態を持たないため、前回までに確認した表定義を毎回埋め込む（実DBから再取得）
		var known = param.KnownTables.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		if (known.Count > 0) {
			sb.AppendLine("# 確認済みのテーブル定義");
			foreach (var table in known) {
				sb.AppendLine(_catalog.DescribeTable(table));
				sb.AppendLine();
			}
		}
		if (param.Spec != null) {
			sb.AppendLine("# 現在の条件（前回までの会話で整理したもの）");
			sb.AppendLine(JsonConvert.SerializeObject(param.Spec, Formatting.Indented));
			sb.AppendLine();
		}
		if (param.BaseSql != null && !string.IsNullOrWhiteSpace(param.BaseSql.Sql)) {
			sb.AppendLine("# 修正元のSQL（保存済みの照会。利用者はこれを修正したい）");
			sb.AppendLine(param.BaseSql.Sql);
			sb.AppendLine("パラメータ: " + JsonConvert.SerializeObject(param.BaseSql.Parameters));
			sb.AppendLine();
		}

		var messages = new JArray { new JObject { ["role"] = "system", ["content"] = sb.ToString().TrimEnd() } };
		foreach (var m in param.Messages.Where(m => !string.IsNullOrWhiteSpace(m.Text)).TakeLast(Math.Max(1, maxMessages))) {
			messages.Add(new JObject { ["role"] = m.Role == EnumAiQueryRole.Assistant ? "assistant" : "user", ["content"] = m.Text });
		}
		if (param.Mode == EnumAiQueryMode.Generate) {
			messages.Add(new JObject {
				["role"] = "user",
				["content"] = "確定した条件でSQLを作成し、submit_sql で提出してください。数値のパラメータは CAST(@n AS INTEGER/REAL) で使ってください。",
			});
		}
		return messages;
	}

	/// <summary>
	/// 情報取得ツールを実行して結果文字列を返す。終端ツール・不明なツールは null。
	/// describe_table で説明を返せた表は <paramref name="knownTables"/> に加える。
	/// </summary>
	public string? RunInfoTool(string name, JObject args, List<string> knownTables) {
		switch (name) {
			case ListTables:
				return _catalog.ListTablesText(args.Value<string>("name_prefix"));
			case DescribeTable:
				var table = args.Value<string>("table");
				if (_catalog.TryDescribeTable(table, out var text) && table != null
					&& !knownTables.Contains(table, StringComparer.OrdinalIgnoreCase)) {
					knownTables.Add(table);
				}
				return text;
			default:
				return null;
		}
	}

	/// <summary>ツール引数（JSON文字列）を解析する。不正なら null。</summary>
	public static JObject? ParseArguments(string? json) {
		if (string.IsNullOrWhiteSpace(json)) {
			return new JObject();
		}
		try {
			return JToken.Parse(json) as JObject;
		}
		catch (JsonException) {
			return null;
		}
	}

	/// <summary>reply の引数から状態・表示文・条件を得る。未解決事項が残る confirm は質問扱いにする。</summary>
	public static (EnumAiQueryStatus Status, string Message, AiQuerySpec Spec) ParseReply(JObject args) {
		var spec = ParseSpec(args["spec"] as JObject);
		spec.Unresolved = Strings(args["unresolved"]);
		var confirm = string.Equals(args.Value<string>("action"), "confirm", StringComparison.OrdinalIgnoreCase) && spec.Unresolved.Count == 0;
		return (confirm ? EnumAiQueryStatus.Confirming : EnumAiQueryStatus.Asking, args.Value<string>("message") ?? string.Empty, spec);
	}

	/// <summary>submit_sql の引数を下書きにする。</summary>
	public static AiQuerySqlDraft ParseSubmit(JObject args) {
		var draft = new AiQuerySqlDraft {
			Sql = args.Value<string>("sql") ?? string.Empty,
			Columns = Strings(args["columns"]),
			Title = args.Value<string>("title") ?? string.Empty,
			Description = args.Value<string>("description") ?? string.Empty,
			Reason = args.Value<string>("reason") ?? string.Empty,
		};
		if (args["parameters"] is JArray parameters) {
			foreach (var p in parameters.OfType<JObject>()) {
				draft.Parameters.Add(new AiQueryParam {
					Name = p.Value<string>("name") ?? string.Empty,
					Label = p.Value<string>("label") ?? string.Empty,
					Type = Enum.TryParse<EnumAiQueryParamType>(p.Value<string>("type"), true, out var type) ? type : EnumAiQueryParamType.Text,
					Value = p["value"]?.ToString() ?? string.Empty,
				});
			}
		}
		return draft;
	}

	static AiQuerySpec ParseSpec(JObject? spec) {
		if (spec == null) {
			return new AiQuerySpec();
		}
		string S(string key) => spec[key]?.Type is JTokenType.String or JTokenType.Integer ? spec[key]!.ToString() : string.Empty;
		return new AiQuerySpec {
			Target = S(nameof(AiQuerySpec.Target)),
			Metrics = Strings(spec[nameof(AiQuerySpec.Metrics)]),
			GroupBy = Strings(spec[nameof(AiQuerySpec.GroupBy)]),
			DateBasis = S(nameof(AiQuerySpec.DateBasis)),
			PeriodFrom = S(nameof(AiQuerySpec.PeriodFrom)),
			PeriodTo = S(nameof(AiQuerySpec.PeriodTo)),
			Filters = Strings(spec[nameof(AiQuerySpec.Filters)]),
			TaxMode = S(nameof(AiQuerySpec.TaxMode)),
			ReturnMode = S(nameof(AiQuerySpec.ReturnMode)),
			OutputColumns = Strings(spec[nameof(AiQuerySpec.OutputColumns)]),
			OrderBy = Strings(spec[nameof(AiQuerySpec.OrderBy)]),
		};
	}

	/// <summary>小さなモデルは配列の代わりに文字列を返すことがあるため両方を受ける。</summary>
	static List<string> Strings(JToken? token) => token switch {
		JArray array => [.. array.Select(x => x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x))],
		JValue { Type: JTokenType.String } value when !string.IsNullOrWhiteSpace(value.ToString()) => [value.ToString()],
		_ => [],
	};

	static void AppendPeriods(StringBuilder sb, string baseDate) {
		if (!DateTime.TryParseExact(baseDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) {
			day = DateTime.Today;
		}
		var month = new DateTime(day.Year, day.Month, 1);
		static string Range(DateTime from, DateTime to) => $"{from:yyyyMMdd}〜{to:yyyyMMdd}";
		sb.AppendLine($"- 基準日（今日）: {day:yyyyMMdd}");
		sb.AppendLine($"- 当月: {Range(month, month.AddMonths(1).AddDays(-1))}");
		sb.AppendLine($"- 先月: {Range(month.AddMonths(-1), month.AddDays(-1))}");
		sb.AppendLine($"- 前年同月: {Range(month.AddYears(-1), month.AddYears(-1).AddMonths(1).AddDays(-1))}");
	}

	static JObject Function(string name, string description, JObject properties, string[] required) => new() {
		["type"] = "function",
		["function"] = new JObject {
			["name"] = name,
			["description"] = description,
			["parameters"] = new JObject {
				["type"] = "object",
				["properties"] = properties,
				["required"] = new JArray(required),
			},
		},
	};

	static JObject Str(string description) => new() { ["type"] = "string", ["description"] = description };

	static JObject StrArray(string description) => new() {
		["type"] = "array", ["description"] = description, ["items"] = new JObject { ["type"] = "string" },
	};

	static JObject SpecSchema() => new() {
		["type"] = "object",
		["description"] = "これまでに整理した条件",
		["properties"] = new JObject {
			[nameof(AiQuerySpec.Target)] = Str("対象（例 店舗売上 Tran01Tenuri）"),
			[nameof(AiQuerySpec.Metrics)] = StrArray("集計項目（例 純売上金額(税抜)）"),
			[nameof(AiQuerySpec.GroupBy)] = StrArray("集計単位（例 店舗）"),
			[nameof(AiQuerySpec.DateBasis)] = Str("日付の基準（例 計上日 DenDay）"),
			[nameof(AiQuerySpec.PeriodFrom)] = Str("期間開始 yyyyMMdd"),
			[nameof(AiQuerySpec.PeriodTo)] = Str("期間終了 yyyyMMdd"),
			[nameof(AiQuerySpec.Filters)] = StrArray("抽出条件"),
			[nameof(AiQuerySpec.TaxMode)] = Str("税込/税抜"),
			[nameof(AiQuerySpec.ReturnMode)] = Str("返品・値引の扱い"),
			[nameof(AiQuerySpec.OutputColumns)] = StrArray("出力列"),
			[nameof(AiQuerySpec.OrderBy)] = StrArray("並び順"),
		},
	};
}
