/*
# description
CoreServiceClient は、CvWpfclient 内で共通する ICoreService の一覧照会と実行要求を提供します。
 */
using CodeShare;
using CvAsset;
using CvBase;
using System.Collections;

namespace CvWpfclient.Helpers;

internal static class CoreServiceClient {
	internal static Task<List<T>> QuerySqlListAsync<T>(string sql, IEnumerable<string> parameters, CancellationToken ct) =>
		QueryListCoreAsync<T>(
			new QueryListSqlParam(typeof(T), sql, [.. parameters]),
			typeof(QueryListSqlParam),
			ct);

	internal static Task<List<T>> QueryListAsync<T>(string where, string order, CancellationToken ct) =>
		QueryListCoreAsync<T>(
			new QueryListParam(typeof(T), where, order),
			typeof(QueryListParam),
			ct);

	internal static Task<CvMsg> SendExecuteAsync(object parameter, CancellationToken ct) {
		var message = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg201_Op_Execute,
			DataType = parameter.GetType(),
			DataMsg = Common.SerializeObject(parameter),
		};
		return SendAsync(message, ct);
	}

	/// <summary>
	/// Id指定の一括削除。洗い替え登録（既存行を消してから入れ直す）で使う。
	/// <para>
	/// 1行ずつ <see cref="DeleteByIdParam"/> を送ると通信回数が行数に比例し、途中で失敗すると
	/// 一部だけ消えた状態が残る。<see cref="DeleteBulkParam"/> は1往復・1トランザクションで、
	/// 1件でも競合すればサーバ側で何も削除されない。失敗は例外にして呼び出し元に再取得させる。
	/// </para>
	/// </summary>
	/// <param name="itemType">対象テーブル型</param>
	/// <param name="rows">削除する行（Idと一覧取得時点のVduを使う）</param>
	/// <param name="label">エラーメッセージに出す対象の呼び名</param>
	/// <param name="ct">キャンセルトークン</param>
	/// <returns>削除した行数</returns>
	internal static async Task<int> DeleteBulkAsync(Type itemType, IEnumerable<BaseDbClass> rows, string label, CancellationToken ct) {
		DeleteBulkRow[] targets = [.. rows.Where(x => x.Id > 0).Select(x => new DeleteBulkRow(x.Id, x.Vdu))];
		if (targets.Length == 0) {
			return 0;
		}
		var reply = await SendExecuteAsync(new DeleteBulkParam(itemType, targets), ct);
		if (reply.Code < 0) {
			var detail = string.IsNullOrEmpty(reply.Option) ? reply.DataMsg : reply.Option;
			throw new InvalidOperationException(
				$"{label}の削除に失敗しました（{targets.Length:N0} 件）。他端末で更新された可能性があります。再取得してください。{detail}");
		}
		return Common.DeserializeObject(reply.DataMsg ?? string.Empty, typeof(DeleteBulkResult)) is DeleteBulkResult result
			? result.DeletedCount
			: targets.Length;
	}

	static async Task<List<T>> QueryListCoreAsync<T>(object parameter, Type parameterType, CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var message = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = parameterType,
			DataMsg = Common.SerializeObject(parameter),
		};
		var reply = await SendAsync(message, ct);
		ct.ThrowIfCancellationRequested();
		if (reply.Code < 0 && reply.Code != -1) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "サーバQueryでエラーが発生しました");
		}
		return Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list
			? list.Cast<T>().ToList()
			: [];
	}

	static Task<CvMsg> SendAsync(CvMsg message, CancellationToken ct) {
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		return coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(ct));
	}
}
