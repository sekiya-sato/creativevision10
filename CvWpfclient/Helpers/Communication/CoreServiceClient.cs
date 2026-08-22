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
