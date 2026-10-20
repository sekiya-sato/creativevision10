using CodeShare;
using CvBase;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Reflection;

namespace CvDomainLogic;

/// <summary>
/// 操作履歴（<see cref="SysHistOperation"/>）の記録設定。サーバ appsettings の <c>OperationLog</c> 節。
/// </summary>
/// <param name="Level">0:記録しない 1:画面起動・終了のみ 2:＋更新・処理実行・印刷 3:＋照会</param>
/// <param name="SkipEmptyQuery">条件もパラメータも無い照会（コンボ用の全件取得など）を記録しない</param>
/// <param name="MaxConditionLength">条件JSONの最大文字数（列長 4000 が上限）</param>
/// <param name="KeepMonths">保持月数（期間削除タスクが使う）</param>
public sealed record OperationLogOptions(int Level = 3, bool SkipEmptyQuery = true, int MaxConditionLength = 4000, int KeepMonths = 13) {
	/// <summary>条件JSONの列長</summary>
	public const int ConditionColumnLength = 4000;
	/// <summary>既定値</summary>
	public static OperationLogOptions Default { get; } = new();
}

/// <summary>
/// 操作履歴（<see cref="SysHistOperation"/>）の記録・期間削除。
/// <para>
/// 条件はサーバが受けたパラメータから組み立てる。汎用の追加・更新・削除はレコード本体を残さず型・件数・Idだけにする（設計判断 H3）。
/// 記録は業務処理とは別に行い、失敗しても業務の結果を変えない（H5）。呼出し側で例外を握る。
/// </para>
/// </summary>
public sealed class OperationLogDb {
	/// <summary>Idを残す最大件数</summary>
	const int MaxIds = 100;
	/// <summary>文字列値1つの最大文字数</summary>
	const int MaxStringValueLength = 2000;
	/// <summary>配列として残す最大要素数</summary>
	const int MaxArrayItems = 100;
	/// <summary>AI照会の入力文の最大文字数</summary>
	const int MaxAiInputLength = 1000;

	readonly ExDatabase _db;
	readonly ILogger<OperationLogDb> _logger;

	public OperationLogDb(ExDatabase db) {
		_db = db;
		_logger = new NLogExtender<OperationLogDb>();
	}

	/// <summary>
	/// 1行を記録する。<see cref="SysHistOperation.OpTime"/> が空ならサーバ時刻を入れる。
	/// </summary>
	public void Insert(SysHistOperation row) {
		if (string.IsNullOrEmpty(row.OpTime)) {
			row.OpTime = DateTime.Now.ToString("yyyyMMddHHmmssfff");
		}
		var vdate = CvAsset.Common.GetVdate();
		row.Vdc = vdate;
		row.Vdu = vdate;
		_db.Insert(row);
	}

	/// <summary>
	/// 保持月数より古い履歴を削除する。
	/// </summary>
	/// <param name="today">基準日（通常は今日）</param>
	/// <param name="keepMonths">保持月数。0以下なら削除しない</param>
	/// <returns>削除した行数</returns>
	public int Purge(DateTime today, int keepMonths) {
		if (keepMonths <= 0) {
			return 0;
		}
		var limit = today.Date.AddMonths(-keepMonths).ToString("yyyyMMdd");
		var cnt = _db.ExecuteDialect($"DELETE FROM {nameof(SysHistOperation)} WHERE OpTime < @0", limit);
		_logger.LogInformation("操作履歴 期間削除 基準日={Limit} 削除行数={Count}", limit, cnt);
		return cnt;
	}

	/// <summary>
	/// 単発要求（QueryMsgAsync）の種類を決める。記録しない要求は0を返す。
	/// </summary>
	public static int ClassifyKind(CvFlag flag) => flag switch {
		CvFlag.Msg001_CopyReply or CvFlag.Msg002_GetVersion or CvFlag.Msg003_GetEnv or CvFlag.Msg004_GetConnectionStatus => 0,
		CvFlag.Msg700_Test_Start or CvFlag.Msg701_TestCase001 or CvFlag.Msg702_TestCase002 => 0,
		// 画面の起動・終了はハンドラ自身が記録する
		CvFlag.Msg093_OperationLog => 0,
		CvFlag.Msg101_Op_Query => OperationKind.Query,
		CvFlag.Msg201_Op_Execute => OperationKind.Update,
		_ => OperationKind.Execute,
	};

	/// <summary>
	/// 記録レベル・空条件の除外に照らして記録するかを決める。
	/// </summary>
	public static bool ShouldRecord(OperationLogOptions options, int kind, object? param) {
		var required = kind switch {
			OperationKind.MenuOpen or OperationKind.MenuClose => 1,
			OperationKind.Update or OperationKind.Execute or OperationKind.Print => 2,
			OperationKind.Query => 3,
			_ => int.MaxValue,
		};
		if (options.Level < required) {
			return false;
		}
		if (kind == OperationKind.Query && options.SkipEmptyQuery && IsEmptyQuery(param)) {
			return false;
		}
		return true;
	}

	static bool IsEmptyQuery(object? param) => param switch {
		QueryListParam p => string.IsNullOrWhiteSpace(p.Where) && p.Parameters.Length == 0,
		QueryOneParam p => string.IsNullOrWhiteSpace(p.Where) && p.Parameters.Length == 0,
		_ => false,
	};

	/// <summary>
	/// パラメータから条件JSONを作る。最大文字数を超える場合は <c>{"truncated":true,"head":…}</c> に包み、常に有効なJSONにする。
	/// </summary>
	public static string BuildCondition(object? param, int maxLength = OperationLogOptions.ConditionColumnLength) {
		if (param is null) {
			return string.Empty;
		}
		var token = Summarize(param);
		return Fit(token.ToString(Formatting.None), maxLength);
	}

	/// <summary>
	/// 記録済みの文字列（クライアントの起動パラメータ等）を最大文字数に収める。JSONでなければ文字列値として包む。
	/// </summary>
	public static string FitText(string? text, int maxLength = OperationLogOptions.ConditionColumnLength) {
		if (string.IsNullOrEmpty(text)) {
			return string.Empty;
		}
		string json;
		try {
			json = JToken.Parse(text).ToString(Formatting.None);
		}
		catch (JsonException) {
			json = JsonConvert.SerializeObject(new JObject { ["text"] = text });
		}
		return Fit(json, maxLength);
	}

	static string Fit(string json, int maxLength) {
		maxLength = Math.Clamp(maxLength, 100, OperationLogOptions.ConditionColumnLength);
		if (json.Length <= maxLength) {
			return json;
		}
		// 包んだ後のエスケープで伸びる分を見込んで、収まるまで先頭部分を縮める
		var headLength = maxLength - 40;
		while (true) {
			var wrapped = new JObject { ["truncated"] = true, ["head"] = json[..headLength] }.ToString(Formatting.None);
			if (wrapped.Length <= maxLength || headLength <= 0) {
				return wrapped.Length <= maxLength ? wrapped : "{\"truncated\":true}";
			}
			headLength = Math.Max(0, headLength - (wrapped.Length - maxLength) - 1);
		}
	}

	static JToken Summarize(object param) => param switch {
		IJsonPayload p => SummarizeWrite(p.ItemType, p.Item),
		DeleteByIdParam p => new JObject { ["type"] = p.ItemType.Name, ["count"] = 1, ["ids"] = new JArray(p.Id) },
		DeleteBulkParam p => SummarizeIds(p.ItemType, p.Rows.Select(r => r.Id), p.Rows.Length),
		PartialUpdateParam p => AddColumns(SummarizeIds(p.ItemType, p.Rows.Select(r => r.Id), p.Rows.Length), p.Columns),
		PrintByCsvParam p => new JObject { ["csvLines"] = CountLines(p.CsvData) },
		AiQueryChatParam p => SummarizeAiChat(p),
		_ => SummarizeObject(param, depth: 0),
	};

	/// <summary>汎用の追加・更新・削除。本体は残さず型・件数・Idだけにする（H3）</summary>
	static JObject SummarizeWrite(Type itemType, string item) {
		List<long> ids = [];
		var count = 0;
		try {
			var token = JToken.Parse(item);
			IEnumerable<JToken> rows = token is JArray array ? array : [token];
			foreach (var row in rows) {
				count++;
				if (row is JObject obj && obj.TryGetValue("Id", StringComparison.Ordinal, out var id) && id.Type == JTokenType.Integer) {
					ids.Add(id.Value<long>());
				}
			}
		}
		catch (JsonException) {
			count = -1;
		}
		return SummarizeIds(itemType, ids, count);
	}

	static JObject SummarizeIds(Type itemType, IEnumerable<long> ids, int count) =>
		new() { ["type"] = itemType.Name, ["count"] = count, ["ids"] = new JArray(ids.Take(MaxIds).Cast<object>().ToArray()) };

	static JObject AddColumns(JObject obj, string[] columns) {
		obj["columns"] = new JArray(columns.Cast<object>().ToArray());
		return obj;
	}

	/// <summary>AI照会。利用者の最後の入力だけを残し、AIの応答本文は残さない</summary>
	static JObject SummarizeAiChat(AiQueryChatParam p) {
		var input = p.Messages.LastOrDefault(m => m.Role == EnumAiQueryRole.User)?.Text ?? string.Empty;
		return new JObject {
			["conversationId"] = p.ConversationId,
			["revision"] = p.Revision,
			["mode"] = p.Mode.ToString(),
			["input"] = Cut(input, MaxAiInputLength),
		};
	}

	static int CountLines(string? text) {
		if (string.IsNullOrEmpty(text)) {
			return 0;
		}
		var lines = text.Count(c => c == '\n');
		return text.EndsWith('\n') ? lines : lines + 1;
	}

	/// <summary>
	/// 公開プロパティを要約する。文字列・数値・真偽・列挙・型は値、配列は先頭 <see cref="MaxArrayItems"/> 件、
	/// 入れ子のオブジェクトは2段まで残す。
	/// </summary>
	static JToken SummarizeObject(object value, int depth) {
		if (TrySummarizeScalar(value, out var scalar)) {
			return scalar;
		}
		if (value is IEnumerable enumerable) {
			var array = new JArray();
			var count = 0;
			foreach (var item in enumerable) {
				if (count++ < MaxArrayItems && depth < 2) {
					array.Add(item is null ? JValue.CreateNull() : SummarizeObject(item, depth + 1));
				}
			}
			return count <= MaxArrayItems ? array : new JObject { ["count"] = count, ["head"] = array };
		}
		var obj = new JObject();
		if (depth >= 2) {
			return obj;
		}
		foreach (var prop in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
			if (!prop.CanRead || prop.GetIndexParameters().Length > 0 || prop.Name == "EqualityContract") {
				continue;
			}
			var propValue = prop.GetValue(value);
			if (propValue is null) {
				continue;
			}
			obj[prop.Name] = SummarizeObject(propValue, depth + 1);
		}
		return obj;
	}

	static bool TrySummarizeScalar(object value, out JToken token) {
		switch (value) {
			case string s:
				token = Cut(s, MaxStringValueLength);
				return true;
			case Type t:
				token = t.Name;
				return true;
			case Enum e:
				token = e.ToString();
				return true;
			case bool or byte or short or int or long or float or double or decimal:
				token = new JValue(value);
				return true;
			case DateTime d:
				token = d.ToString("yyyyMMddHHmmss");
				return true;
			default:
				token = JValue.CreateNull();
				return false;
		}
	}

	static string Cut(string s, int max) => s.Length <= max ? s : s[..max];

	/// <summary>
	/// 照会結果（JSON配列）の件数を数える。配列でなければ -1。
	/// </summary>
	public static int CountResultRows(string? dataMsg) {
		if (string.IsNullOrEmpty(dataMsg) || dataMsg[0] != '[') {
			return -1;
		}
		try {
			using var reader = new JsonTextReader(new StringReader(dataMsg));
			if (!reader.Read() || reader.TokenType != JsonToken.StartArray) {
				return -1;
			}
			var count = 0;
			while (reader.Read() && reader.TokenType != JsonToken.EndArray) {
				count++;
				reader.Skip();
			}
			return count;
		}
		catch (JsonException) {
			return -1;
		}
	}
}
