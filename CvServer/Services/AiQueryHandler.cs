using CodeShare;
using CvAsset;
using CvBase;
using CvDomainLogic.AiQuery;
using CvServer.Services.AiQuery;
using Newtonsoft.Json.Linq;
using ProtoBuf.Grpc;

namespace CvServer.Services;

/// <summary>
/// AI照会（Doc/spec/2026-10-19_AI-query_最小変更設計案.md）のサーバ処理。
/// </summary>
public partial class CoreService {

	/// <summary>
	/// Msg091: AI照会の対話1往復（§6.2）。
	/// <para>
	/// サーバは会話状態を持たない。LLMを tools + tool_choice=required で呼び、情報取得ツール（list_tables / describe_table）は
	/// サーバが実行して結果を返し、終端ツール（Chat: reply / Generate: submit_sql）の引数で応答を確定する。
	/// submit_sql は §7 の検査を通し、エラーはLLMへ返して最大 MaxRepair 回修正させる。
	/// AIの失敗・設定不備は例外応答ではなく <see cref="EnumAiQueryStatus.Failed"/> の業務応答で返し、確定条件を保持させる。
	/// </para>
	/// </summary>
	private async Task<CvMsg> HandleAiQueryChatAsync(CvMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		if (Common.DeserializeObject(request.DataMsg ?? string.Empty, typeof(AiQueryChatParam)) is not AiQueryChatParam param) {
			return CreateErrorResponse(request.Flag, CvMsgErrorCode.InvalidParameter, "パラメータが不正です", typeof(string), string.Empty);
		}
		var started = DateTime.Now;
		var result = await RunAiQueryChatAsync(param, context.CancellationToken).ConfigureAwait(false);
		_logger.LogInformation("AI照会 対話 Conversation={Conversation} Rev={Revision} Mode={Mode} 結果={Status} 所要={Elapsed}ms",
			param.ConversationId, param.Revision, param.Mode, result.Status, (int)(DateTime.Now - started).TotalMilliseconds);
		return CreateSuccessResponse(request.Flag, typeof(AiQueryChatResult), Common.SerializeObject(result));
	}

	private async Task<AiQueryChatResult> RunAiQueryChatAsync(AiQueryChatParam param, CancellationToken ct) {
		var result = new AiQueryChatResult {
			ConversationId = param.ConversationId,
			Revision = param.Revision,
			Spec = param.Spec,
			KnownTables = [.. param.KnownTables],
		};
		AiQueryChatResult Fail(string message, IEnumerable<string>? errors = null) {
			result.Status = EnumAiQueryStatus.Failed;
			result.AssistantText = message;
			result.Errors.AddRange(errors ?? []);
			return result;
		}

		var options = AiQueryOptions.From(_configuration);
		if (!options.Enabled || _aiQueryLlm == null) {
			return Fail("AI照会は設定されていません（サーバ設定 AiQuery:Enabled）。保存済みSQLの実行は利用できます。");
		}
		if (!AiQuerySqlGuard.IsSqlite(_db)) {
			return Fail("AI照会はSQLite接続のサーバだけで利用できます。");
		}
		if (param.Mode == EnumAiQueryMode.Generate && param.Spec?.Unresolved.Count > 0) {
			return Fail("未確定の事項があるためSQLを作成できません。会話で条件を確定してください。", param.Spec.Unresolved);
		}

		var tools = new AiQueryTools(_db);
		var messages = tools.BuildMessages(param, ReadAiQueryDictionary(options), options.MaxMessages);
		var definitions = AiQueryTools.Definitions(param.Mode);
		var repairs = 0;
		try {
			for (var round = 0; round < Math.Max(1, options.MaxToolRounds); round++) {
				var message = await _aiQueryLlm.ChatAsync(messages, definitions, ct).ConfigureAwait(false);
				messages.Add(message);
				if (message["tool_calls"] is not JArray calls || calls.Count == 0) {
					messages.Add(new JObject { ["role"] = "user", ["content"] = "応答は必ずツールで返してください。" });
					continue;
				}
				foreach (var call in calls.OfType<JObject>()) {
					var name = call.SelectToken("function.name")?.ToString() ?? string.Empty;
					var args = AiQueryTools.ParseArguments(call.SelectToken("function.arguments")?.ToString());
					string content;
					if (args == null) {
						content = "引数がJSONとして解析できません。正しいJSONで再度呼び出してください。";
					}
					else if (tools.RunInfoTool(name, args, result.KnownTables) is string info) {
						content = info;
					}
					else if (name == AiQueryTools.Reply && param.Mode == EnumAiQueryMode.Chat) {
						var (status, text, spec) = AiQueryTools.ParseReply(args);
						result.Status = status;
						result.AssistantText = text;
						result.Spec = spec;
						return result;
					}
					else if (name == AiQueryTools.SubmitSql && param.Mode == EnumAiQueryMode.Generate) {
						var draft = AiQueryTools.ParseSubmit(args);
						var validation = new AiQuerySqlGuard(_db).Validate(draft.Sql, draft.Parameters);
						result.Draft = draft;
						if (validation.IsValid) {
							result.Status = EnumAiQueryStatus.Generated;
							result.AssistantText = draft.Description;
							result.Errors.Clear();
							return result;
						}
						result.Errors = validation.Errors;
						if (++repairs > options.MaxRepair) {
							// 最後のSQLとエラー（result.Errors）は保持して返す
							return Fail("SQLを作成しましたが検査に通りませんでした。条件を見直すか、時間をおいて再度作成してください。");
						}
						content = "SQLが検査に通りませんでした。次のエラーを直して submit_sql で再提出してください。\n" + string.Join("\n", validation.Errors);
					}
					else {
						content = $"ツール {name} は現在使えません。";
					}
					messages.Add(new JObject { ["role"] = "tool", ["tool_call_id"] = call.Value<string>("id") ?? string.Empty, ["content"] = content });
				}
			}
			return Fail("AIの応答が規定回数内にまとまりませんでした。要求を短くして再度送信してください。");
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested) {
			throw;
		}
		catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException or Newtonsoft.Json.JsonException) {
			_logger.LogWarning(ex, "AI照会 LLM呼出し失敗 Conversation={Conversation}", param.ConversationId);
			return Fail($"AIに接続できませんでした: {ex.Message}");
		}
	}

	/// <summary>業務辞書を要求ごとに読む（追記を再起動なしで反映するため）。無ければ空。</summary>
	private string ReadAiQueryDictionary(AiQueryOptions options) {
		var path = Path.IsPathRooted(options.DictionaryPath)
			? options.DictionaryPath
			: Path.Combine(AppContext.BaseDirectory, options.DictionaryPath);
		if (!File.Exists(path)) {
			_logger.LogWarning("AI照会 業務辞書がありません Path={Path}", path);
			return string.Empty;
		}
		return File.ReadAllText(path);
	}

	/// <summary>
	/// Msg092: SQL検査のみ（AIを呼ばない）。保存SQLの再実行・手修正SQLの実行前に使う。
	/// 検査に通らないことは業務エラーとして <see cref="AiQueryValidateResult"/> で返し、例外応答にしない。
	/// </summary>
	private CvMsg HandleAiQueryValidate(CvMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		if (Common.DeserializeObject(request.DataMsg ?? string.Empty, typeof(AiQueryValidateParam)) is not AiQueryValidateParam param) {
			return CreateErrorResponse(request.Flag, CvMsgErrorCode.InvalidParameter, "パラメータが不正です", typeof(string), string.Empty);
		}
		LogDetailedRequest("パラメータ AiQueryValidateParam SQL={Sql}", () => [param.Sql]);
		var result = new AiQuerySqlGuard(_db).Validate(param.Sql, param.Parameters);
		_logger.LogInformation("AI照会 SQL検査 結果={IsValid} 参照表={Tables}", result.IsValid, string.Join(",", result.Tables));
		return CreateSuccessResponse(request.Flag, typeof(AiQueryValidateResult), Common.SerializeObject(result));
	}

	/// <summary>
	/// 共有保存（<see cref="SysAiQuery"/>）の保存日と保存者をサーバで打刻する。
	/// クライアントの申告値は使わない。対象外の型では何もしない。
	/// </summary>
	private void StampAiQuerySave(object item) {
		if (item is not SysAiQuery query) {
			return;
		}
		query.SaveDate = DateTime.Now.ToString("yyyyMMdd");
		query.Id_Login = ResolveLoginId();
	}

	/// <summary>
	/// <see cref="SysAiQuery"/> 宛ての一括追加・部分更新・一括削除を拒否する。
	/// これらは打刻（<see cref="StampAiQuerySave"/>）を通らないため、画面が使う単件の追加・更新・削除だけを許す。
	/// </summary>
	/// <returns>拒否する場合はメッセージ、許可する場合は null</returns>
	private static string? RejectAiQueryBulkWrite(Type? writeType, object? param) {
		if (writeType != typeof(SysAiQuery)) {
			return null;
		}
		return param is InsertBulkParam or PartialUpdateParam or DeleteBulkParam
			? "AI照会の保存SQLは一括追加・部分更新・一括削除できません。1件ずつ保存してください。"
			: null;
	}
}
