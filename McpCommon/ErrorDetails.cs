using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace McpCommon;

/// <summary>機密を含む可能性のある例外全文は保持せず、診断に必要な項目だけを記録する。</summary>
static class ErrorDetails {
	static readonly object Sync = new();
	static readonly Dictionary<string, ErrorRecord> Records = new();
	static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
	const int MaxRecords = 100;

	public static string Get([Description("エラー応答に含まれる error_id。プロセス内で30分間有効。")] string errorId) {
		lock (Sync) {
			Prune();
			return errorId is not null && Records.TryGetValue(errorId, out var record)
				? JsonSerializer.Serialize(record)
				: JsonSerializer.Serialize(new { error = "エラー ID が存在しないか、有効期限が切れています。" });
		}
	}

	public static string Record(string tool, string message) {
		// DB エラーコードだけを抽出する。SQL、パラメータ、例外文は保存しない。
		var oracle = Regex.Match(message, @"^Oracle エラー \(ORA-(\d{1,6})\)", RegexOptions.CultureInvariant);
		var sqlite = Regex.Match(message, @"^SQLite エラー \((\d+)\)", RegexOptions.CultureInvariant);
		string? code = oracle.Success ? "ORA-" + oracle.Groups[1].Value : sqlite.Success ? "SQLITE-" + sqlite.Groups[1].Value : null;
		var category = sqlite.Success && message.Contains("syntax error", StringComparison.OrdinalIgnoreCase) ? "sql_syntax" :
			sqlite.Success && message.Contains("no such column", StringComparison.OrdinalIgnoreCase) ? "column_not_found" :
			sqlite.Success && message.Contains("no such table", StringComparison.OrdinalIgnoreCase) ? "table_not_found" :
			code is not null ? "database" : message.StartsWith("読み取り専用モードでは実行できません", StringComparison.Ordinal) ? "sql_rejected" : message.StartsWith("parameters[", StringComparison.Ordinal) ? "invalid_parameter" : message.Contains("存在しません。list_tables", StringComparison.Ordinal) ? "table_not_found" : "validation_or_internal";
		var advice = tool == "execute" ? "更新結果が不明な場合は再送前に DB の状態を確認してください。" : category switch {
			"sql_rejected" => "読み取り専用の単一 SQL 文を指定してください。",
			"invalid_parameter" => "パラメータには数値、文字列、真偽値または null を指定してください。",
			"table_not_found" => "list_tables で対象を確認してください。",
			"column_not_found" => "describe_table で列名を確認してください。",
			"sql_syntax" => "SQL の構文を確認してください。",
			_ => "入力、権限、接続状態を確認してください。"
		};
		var id = Guid.NewGuid().ToString("N");
		lock (Sync) {
			Prune();
			if (Records.Count >= MaxRecords)
				Records.Remove(Records.MinBy(x => x.Value.timestamp).Key);
			Records[id] = new(id, tool, category, code, DateTimeOffset.UtcNow, advice);
		}
		return id;
	}

	static void Prune() {
		var cutoff = DateTimeOffset.UtcNow - Lifetime;
		foreach (var id in Records.Where(x => x.Value.timestamp < cutoff).Select(x => x.Key).ToArray())
			Records.Remove(id);
	}

	sealed record ErrorRecord(string error_id, string tool, string category, string? db_code, DateTimeOffset timestamp, string advice);
}

sealed class DiagnosticTool(McpServerTool inner) : DelegatingMcpServerTool(inner) {
	public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default) {
		string message;
		try {
			var result = await base.InvokeAsync(request, cancellationToken);
			if (result.IsError != true)
				return result;
			message = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(x => x.Text));
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception ex) { message = ex.Message; }
		var id = ErrorDetails.Record(ProtocolTool.Name, message);
		return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = $"処理に失敗しました。error_id: {id}。get_error_details で詳細を取得できます。" }] };
	}
}
