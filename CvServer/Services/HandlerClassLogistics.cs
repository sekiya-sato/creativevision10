using CodeShare;
using CvAsset;
using CvBase;
using CvDomainLogic;

namespace CvServer.Services;

// 物流連携（WMS）の受け口。処理本体は CvDomainLogic/LogisticsLinkDb（自動実行からも同じ本体を呼ぶ）。
// 仕様は `Doc/spec/2026-10-05_WMS連携_旧AMS連携調査と仮実装仕様.md` 6章。
public partial class CoreService {
	/// <summary>物流連携の設定照会</summary>
	private CvMsg HandleLogisticsSettings(CvFlag flag) {
		try {
			var info = new LogisticsLinkDb(_db).QuerySettings();
			return CreateSuccessResponse(flag, typeof(LogisticsSettingsInfo), Common.SerializeObject(info));
		}
		catch (Exception ex) {
			return CreateExceptionResponse(flag, ex, typeof(string), ex.Message);
		}
	}

	/// <summary>
	/// 物流連携の処理を実行する共通部。設定不備・入力違反・他処理の実行中は画面で対処できるのでメッセージだけ返す。
	/// トランザクションは処理本体が必要な単位で張る（受信反映は伝票単位）。
	/// </summary>
	private CvMsg RunLogistics<TResult>(CvFlag flag, string name, Func<LogisticsLinkDb, long, TResult> body) where TResult : notnull {
		_logger.LogInformation("物流連携 {Name}", name);
		try {
			var result = body(new LogisticsLinkDb(_db), ResolveLoginShainId());
			return CreateSuccessResponse(flag, typeof(TResult), Common.SerializeObject(result));
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) {
			return CreateErrorResponse(flag, CvMsgErrorCode.InvalidParameter, ex.Message, typeof(string), ex.Message);
		}
		catch (Exception ex) {
			return CreateExceptionResponse(flag, ex, typeof(string), ex.Message);
		}
	}

	/// <summary>L01 マスタデータ作成</summary>
	private CvMsg HandleLogisticsMaster(CvFlag flag, LogisticsMasterParam param) =>
		RunLogistics(flag, $"マスタデータ作成 {string.Join(",", param.Kinds ?? [])} 全件={param.IsFull} プレビュー={param.PreviewOnly}",
			(logistics, loginShain) => logistics.CreateMasterFiles(param, param.IdShain > 0 ? param.IdShain : loginShain));

	/// <summary>L02 送信対象の照会</summary>
	private CvMsg HandleLogisticsSendQuery(CvFlag flag, LogisticsSendQueryParam param) =>
		RunLogistics(flag, $"送信対象照会 {param.Kind} {param.ToDay}", (logistics, _) => logistics.QuerySendCandidates(param));

	/// <summary>L02 送信ファイル作成</summary>
	private CvMsg HandleLogisticsSend(CvFlag flag, LogisticsSendParam param) =>
		RunLogistics(flag, $"送信 {param.Kind} {param.ToDay} 選択={param.RefIds?.Length ?? 0}",
			(logistics, loginShain) => logistics.CreateSendBatch(param, param.IdShain > 0 ? param.IdShain : loginShain));

	/// <summary>L04 送信バッチの再出力・取消</summary>
	private CvMsg HandleLogisticsBatchAction(CvFlag flag, LogisticsBatchActionParam param) =>
		RunLogistics(flag, $"送信バッチ操作 {param.Action} {param.BatchId}",
			(logistics, loginShain) => logistics.ExecuteBatchAction(param, param.IdShain > 0 ? param.IdShain : loginShain));

	/// <summary>L04 バッチ一覧</summary>
	private CvMsg HandleLogisticsBatchQuery(CvFlag flag, LogisticsBatchQueryParam param) =>
		RunLogistics(flag, "バッチ照会", (logistics, _) => logistics.QueryBatches(param));

	/// <summary>L04 行一覧</summary>
	private CvMsg HandleLogisticsLineQuery(CvFlag flag, LogisticsLineQueryParam param) =>
		RunLogistics(flag, $"行照会 {param.BatchId}", (logistics, _) => logistics.QueryLines(param));

	/// <summary>L03 受信フォルダのファイル一覧</summary>
	private CvMsg HandleLogisticsReceiveFiles(CvFlag flag) =>
		RunLogistics(flag, "受信ファイル照会", (logistics, _) => logistics.QueryReceiveFiles());

	/// <summary>L03 取込・検査</summary>
	private CvMsg HandleLogisticsReceiveImport(CvFlag flag, LogisticsReceiveImportParam param) =>
		RunLogistics(flag, $"受信取込 フォルダ={param.FileNames?.Length ?? 0} 選択={param.Uploads?.Length ?? 0}",
			(logistics, loginShain) => logistics.ImportReceiveFiles(param, param.IdShain > 0 ? param.IdShain : loginShain));

	/// <summary>L03/L04 再検査</summary>
	private CvMsg HandleLogisticsRecheck(CvFlag flag, LogisticsRecheckParam param) =>
		RunLogistics(flag, $"受信再検査 {param.BatchId}",
			(logistics, loginShain) => logistics.RecheckBatch(param, param.IdShain > 0 ? param.IdShain : loginShain));
}
