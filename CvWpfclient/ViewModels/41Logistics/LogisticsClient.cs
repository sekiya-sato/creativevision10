using CodeShare;
using CvAsset;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._41Logistics;

/// <summary>
/// 物流連携画面（L01〜L04）の共通呼び出し。契約は CvBase/LogisticsContracts.cs、受け口は CvServer の HandlerClassLogistics。
/// </summary>
internal static class LogisticsClient {
	/// <summary>
	/// パラメータを <see cref="CvAsset.CvFlag.Msg201_Op_Execute"/> で送り、応答を <typeparamref name="T"/> に戻す。
	/// サーバのエラー応答は例外にせず Error に入れる（通信・キャンセルの例外は呼び出し側で扱う）。
	/// </summary>
	/// <returns>成功時は Result、失敗時は Error にメッセージ</returns>
	internal static async Task<(T? Result, string Error)> ExecuteAsync<T>(object param, CancellationToken ct) where T : class {
		var reply = await CoreServiceClient.SendExecuteAsync(param, ct);
		if (reply.Code < 0) {
			// -9000 未満は例外応答で詳細が Option、それ以外は DataMsg にメッセージが入る
			var detail = reply.Code < -9000 ? reply.Option : reply.DataMsg;
			return (null, $"{(string.IsNullOrWhiteSpace(detail) ? "処理に失敗しました。" : detail)} ({reply.Code})");
		}
		var result = Common.DeserializeObject<T>(reply.DataMsg ?? string.Empty);
		return result == null ? (null, "応答を読み取れませんでした。") : (result, string.Empty);
	}
}
