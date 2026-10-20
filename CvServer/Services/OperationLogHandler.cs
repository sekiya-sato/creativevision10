using CodeShare;
using CvAsset;
using CvBase;
using CvDomainLogic;
using ProtoBuf.Grpc;
using System.Diagnostics;

namespace CvServer.Services;

/// <summary>
/// 操作履歴（<see cref="SysHistOperation"/>）の記録（Doc/spec/2026-10-20_メニュー利用履歴_最小変更設計案.md）。
/// <para>
/// 受信口（<see cref="QueryMsgAsync"/>・<see cref="QueryMsgStreamAsync"/>・<see cref="PrintPdfAsync"/>）の処理後に1行記録する。
/// 記録は業務処理のトランザクションとは別で、失敗しても業務応答を変えない（設計判断 H5）。
/// </para>
/// </summary>
public partial class CoreService {
	/// <summary>どの画面からの要求かを示すヘッダ。値は「画面セッション|機能Id」</summary>
	public const string ScreenHeaderName = "x-cv-screen";
	private const string ClientIdHeaderName = "x-clientid";

	/// <summary>サーバ設定 <c>OperationLog</c> 節を読む。未設定の項目は既定値</summary>
	private OperationLogOptions GetOperationLogOptions() {
		var section = _configuration.GetSection("OperationLog");
		var d = OperationLogOptions.Default;
		return new OperationLogOptions(
			section.GetValue("Level", d.Level),
			section.GetValue("SkipEmptyQuery", d.SkipEmptyQuery),
			section.GetValue("MaxConditionLength", d.MaxConditionLength),
			section.GetValue("KeepMonths", d.KeepMonths));
	}

	/// <summary>
	/// 受信した要求1件を記録する。記録しない種類・レベルなら何もしない。例外は握ってログへ出す。
	/// </summary>
	private void RecordOperation(int kind, string op, Type? dataType, string? dataMsg, CallContext context,
		int resultCode, int resultCount, Stopwatch stopwatch, string? formFile = null) {
		if (kind == 0) {
			return;
		}
		try {
			var options = GetOperationLogOptions();
			if (options.Level <= 0) {
				return;
			}
			object? param = null;
			if (dataType != null && dataType != typeof(string) && !string.IsNullOrEmpty(dataMsg)) {
				param = Common.DeserializeObject(dataMsg, dataType);
			}
			if (!OperationLogDb.ShouldRecord(options, kind, param)) {
				return;
			}
			var condition = OperationLogDb.BuildCondition(param, options.MaxConditionLength);
			if (!string.IsNullOrEmpty(formFile)) {
				// 印刷は帳票ファイル名を条件の先頭に添える
				condition = OperationLogDb.FitText($"{{\"form\":{Newtonsoft.Json.JsonConvert.ToString(formFile)},\"param\":{(condition.Length > 0 ? condition : "null")}}}", options.MaxConditionLength);
			}
			var row = CreateOperationRow(kind, op, context);
			row.DataType = dataType?.Name ?? string.Empty;
			row.Condition = condition;
			row.ResultCode = resultCode;
			row.ResultCount = resultCount;
			row.ElapsedMs = (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);
			new OperationLogDb(_db).Insert(row);
		}
		catch (Exception ex) {
			_logger.LogWarning(ex, "操作履歴の記録に失敗しました Op={Op}", op);
		}
	}

	/// <summary>利用者・時刻・IP（サーバ解決）と画面・端末（ヘッダの申告値）を入れた1行を作る</summary>
	private SysHistOperation CreateOperationRow(int kind, string op, CallContext context) {
		var (screenSession, functionId) = ParseScreenHeader(GetRequestHeader(context, ScreenHeaderName));
		var loginId = ResolveLoginId();
		return new SysHistOperation {
			OpTime = DateTime.Now.ToString("yyyyMMddHHmmssfff"),
			Kind = kind,
			Id_Login = loginId,
			Id_Shain = loginId > 0 ? _db.FirstOrDefault<SysLogin>("where Id=@0", loginId)?.Id_Shain ?? 0 : 0,
			ClientId = Cut(GetRequestHeader(context, ClientIdHeaderName), 36),
			ScreenSession = screenSession,
			FunctionId = functionId,
			Op = Cut(op, 60),
			ResultCount = -1,
			Ip = Cut(_httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(), 45),
		};
	}

	/// <summary>要求ヘッダを読む。サーバ文脈の無い呼出し（テスト等）では RequestHeaders が例外になるので null を返す</summary>
	private static string? GetRequestHeader(CallContext context, string name) {
		try {
			return context.RequestHeaders?.GetValue(name);
		}
		catch (Exception) {
			return null;
		}
	}

	/// <summary>「画面セッション|機能Id」を分ける。不正・未指定は空文字</summary>
	public static (string ScreenSession, string FunctionId) ParseScreenHeader(string? value) {
		if (string.IsNullOrWhiteSpace(value)) {
			return (string.Empty, string.Empty);
		}
		var sep = value.IndexOf('|');
		return sep < 0
			? (Cut(value.Trim(), 36), string.Empty)
			: (Cut(value[..sep].Trim(), 36), Cut(value[(sep + 1)..].Trim(), 100));
	}

	private static string Cut(string? s, int max) => string.IsNullOrEmpty(s) ? string.Empty : s.Length <= max ? s : s[..max];

	/// <summary>
	/// 画面の起動・終了の記録（Msg093_OperationLog）。利用者・時刻・IPはサーバで解決する。
	/// </summary>
	private CvMsg HandleOperationLog(CvMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		if (Common.DeserializeObject(request.DataMsg ?? string.Empty, typeof(OperationLogParam)) is not OperationLogParam param
			|| param.Kind is not (OperationKind.MenuOpen or OperationKind.MenuClose)) {
			return CreateErrorResponse(request.Flag, CvMsgErrorCode.InvalidParameter, "パラメータが不正です", typeof(string), string.Empty);
		}
		try {
			var options = GetOperationLogOptions();
			if (OperationLogDb.ShouldRecord(options, param.Kind, param)) {
				var row = CreateOperationRow(param.Kind, param.Kind == OperationKind.MenuOpen ? "MenuOpen" : "MenuClose", context);
				// 起動・終了はヘッダより本文の画面・機能を正とする（起動時点ではまだヘッダが切り替わっていない）
				row.ScreenSession = Cut(param.ScreenSession, 36);
				row.FunctionId = Cut(param.FunctionId, 100);
				row.MenuPath = Cut(param.MenuPath, 200);
				row.DataType = nameof(OperationLogParam);
				row.Condition = OperationLogDb.FitText(param.Condition, options.MaxConditionLength);
				row.ResultCode = 0;
				new OperationLogDb(_db).Insert(row);
			}
		}
		catch (Exception ex) {
			_logger.LogWarning(ex, "操作履歴の記録に失敗しました Op=Msg093_OperationLog");
		}
		// 記録の成否で画面の動作を変えないため常に成功を返す
		return CreateSuccessResponse(request.Flag, typeof(string), string.Empty);
	}

	/// <summary>
	/// <see cref="SysHistOperation"/> への汎用の追加・更新・削除を拒否する。記録はサーバだけが行う。
	/// </summary>
	/// <returns>拒否する場合はメッセージ、許可する場合は null</returns>
	private static string? RejectOperationLogWrite(Type? writeType) =>
		writeType == typeof(SysHistOperation) ? "操作履歴は変更・削除できません。" : null;

	/// <summary>
	/// ストリーム応答を中継し、終了時（正常・例外・キャンセル）に1行記録する。
	/// </summary>
	private async IAsyncEnumerable<T> WithOperationLogAsync<T>(IAsyncEnumerable<T> source, int kind, string op, Type? dataType, string? dataMsg,
		CallContext context, Func<T, int> getCode, string? formFile = null) {
		var stopwatch = Stopwatch.StartNew();
		var code = 0;
		var completed = false;
		try {
			await foreach (var item in source.WithCancellation(context.CancellationToken)) {
				var itemCode = getCode(item);
				if (itemCode < 0) {
					code = itemCode;
				}
				yield return item;
			}
			completed = true;
		}
		finally {
			// 途中で例外・キャンセル・切断になった場合は CvMsgErrorCode.Unexpected 相当として残す
			RecordOperation(kind, op, dataType, dataMsg, context, completed ? code : CvMsgErrorCode.Unexpected, -1, stopwatch, formFile);
		}
	}
}
