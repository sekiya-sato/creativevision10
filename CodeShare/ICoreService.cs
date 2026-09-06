using ProtoBuf.Grpc;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace CodeShare;

/// <summary>
/// Contract:共通メッセージClass
/// </summary>
[DataContract]
public sealed class CvMsg {
	/// <summary>
	/// メッセージ種別
	/// </summary>
	[DataMember(Order = 1)]
	public required CvFlag Flag { get; set; }
	/// <summary>
	/// コード（リターンコード、その他）
	/// </summary>
	[DataMember(Order = 2)]
	public int Code { get; set; }
	/// <summary>
	/// メッセージ型
	/// </summary>
	[DataMember(Order = 3)]
	public Type DataType { get; set; } = typeof(string);
	/// <summary>
	/// メッセージ本体
	/// </summary>
	[DataMember(Order = 4)]
	public string DataMsg { get; set; } = string.Empty;

	[DataMember(Order = 5)]
	public string Option { get; set; } = string.Empty;
}

/// <summary>
/// 共通メッセージのエラーコード
/// </summary>
public static class CvMsgErrorCode {
	/// <summary>
	/// 他端末で更新され、クライアントが保持する更新日時と一致しない
	/// </summary>
	public const int ConcurrentUpdate = -9901;

	/// <summary>
	/// 想定外のサーバー処理エラー
	/// </summary>
	public const int Unexpected = -9902;

	/// <summary>
	/// 出荷指示確定で有効在庫が不足している（1件も確定していない）。
	/// DataType に <c>ShippingShortageDto[]</c> を載せて割れたSKUを返す。
	/// </summary>
	public const int ShippingUnavailable = -9903;

	/// <summary>
	/// パラメータが業務条件を満たしていない（画面で直せる入力エラー）。
	/// Option にそのまま表示できるメッセージを載せる。
	/// </summary>
	public const int InvalidParameter = -9904;

	/// <summary>
	/// 条件に一致するデータが1件も無い。エラーではなく「0件」を表す。
	/// <para>
	/// 一覧系の応答は 0 件でも <c>Code</c> が負になるため、呼び出し側は本コードを
	/// 障害と区別すること。区別しないと DataMsg の <c>"[]"</c> がそのまま
	/// エラーメッセージとして画面に出る。
	/// </para>
	/// </summary>
	public const int NotFound = -1;
}

/// <summary>
/// ストリーミング応答メッセージ
/// </summary>
[DataContract]
public sealed record class StreamMsg {
	/// <summary>
	/// メッセージ種別
	/// </summary>
	[DataMember(Order = 1)]
	public required CvFlag Flag { get; init; }
	/// <summary>
	/// コード（リターンコード、その他）
	/// </summary>
	[DataMember(Order = 2)]
	public int Code { get; init; }
	/// <summary>
	/// メッセージ型
	/// </summary>
	[DataMember(Order = 3)]
	public Type DataType { get; set; } = typeof(string);
	/// <summary>
	/// メッセージ本体
	/// </summary>
	[DataMember(Order = 4)]
	public string DataMsg { get; set; } = string.Empty;
	/// <summary>
	/// 進捗（0-100）
	/// </summary>
	[DataMember(Order = 5)]
	public int Progress { get; init; }
	/// <summary>
	/// 完了フラグ
	/// </summary>
	[DataMember(Order = 6)]
	public bool IsCompleted { get; init; }
	/// <summary>
	/// エラーフラグ
	/// </summary>
	[DataMember(Order = 7)]
	public bool IsError { get; init; }
}



/// <summary>
/// メッセージ種別
/// [Common message flag]
/// </summary>
[DataContract]
public enum CvFlag {
	/// <summary>
	/// サーバーに送信されたメッセージをそのまま返す Message=送信メッセージ
	/// [Return the message sent to the server as it is. Message=Sent message]
	/// </summary>
	[EnumMember]
	Msg001_CopyReply = 1,
	/// <summary>
	/// サーバーのバージョン情報を返す Message=CommonEnvのJSON文字列
	/// [Return the server version information. Message=JSON string of CommonEnv]
	/// </summary>
	[EnumMember]
	Msg002_GetVersion = 2,
	/// <summary>
	/// サーバーの環境情報を返す Message=環境変数配列のJSON文字列
	/// [Return the server environment information. Message=JSON string of environment variable array]
	/// </summary>
	[EnumMember]
	Msg003_GetEnv = 3,
	/// <summary>
	/// サーバー側の接続定義の配列を返す Message=接続定義の配列 例 "Sqlite,MariaDB,PostgreSQL,Oracle"
	/// </summary>
	[EnumMember]
	Msg004_GetConnectionStatus = 4,
	[EnumMember]
	Msg030_ConvertSqlite2Mariadb = 31,
	[EnumMember]
	Msg030_ConvertSqlite2Postgre = 32,
	/// <summary>
	/// データベースの変換
	/// </summary>
	[EnumMember]
	Msg040_ConvertDb = 40,
	/// <summary>
	/// DB変換タスクリストの取得
	/// </summary>
	[EnumMember]
	Msg041_ConvertList = 41,
	/// <summary>
	/// テーブル一覧と件数の取得
	/// </summary>
	[EnumMember]
	Msg042_GetTableList = 42,
	/// <summary>
	/// 商品マスタのId_Col=0,Id_Siz=0のデータから名称マスタを再構築する
	/// </summary>
	[EnumMember]
	Msg046_MasterShohinMeishoRebuild = 46,
	/// <summary>
	/// Master系のV*列(CodeNameView)とJSON内の名称スナップショットを参照先マスタの現在値で再同期する
	/// </summary>
	[EnumMember]
	Msg047_MasterVColumnResync = 47,
	/// <summary>
	/// 集計処理
	/// </summary>
	[EnumMember]
	Msg050_Summary = 50,
	/// <summary>
	/// リアル在庫集計処理
	/// </summary>
	[EnumMember]
	Msg051_SummaryRealStock = 51,
	/// <summary>
	/// 売掛集計処理
	/// </summary>
	[EnumMember]
	Msg052_SummaryUriKake = 52,
	/// <summary>
	/// 買掛集計処理
	/// </summary>
	[EnumMember]
	Msg053_SummaryKaiKake = 53,
	/// <summary>
	/// 棚卸開始処理（対象年月末時点の帳簿在庫を凍結する）
	/// </summary>
	[EnumMember]
	Msg054_StocktakeStart = 54,
	/// <summary>
	/// 棚卸確定処理（実棚数と帳簿在庫の差を在庫調整伝票へ起こす）
	/// </summary>
	[EnumMember]
	Msg055_StocktakeFix = 55,
	/// <summary>
	/// 請求残計算処理
	/// </summary>
	[EnumMember]
	Msg056_SummaryUriSei = 56,
	/// <summary>
	/// 支払残計算処理
	/// </summary>
	[EnumMember]
	Msg057_SummaryKaiShi = 57,
	/// <summary>
	/// HHTデータ更新処理（TranVulcanHht を Tran系各テーブルへ展開する）
	/// </summary>
	[EnumMember]
	Msg058_HhtDataUpdate = 58,
	/// <summary>
	/// 伝票税額再更新（明細Taxが未設定のTran系伝票へ明細別消費税を投入する一時的な管理者処理）
	/// </summary>
	[EnumMember]
	Msg059_TranTaxRebuild = 59,
	/// <summary>
	/// 棚卸の店舗別状況照会（棚卸日・計上月・開始済み／確定済み／再確定要と、基準日以外の棚卸入力の内訳）
	/// </summary>
	[EnumMember]
	Msg060_StocktakeStatus = 60,
	/// <summary>
	/// マニュアル排他制御の状態照会（SysSeqType=1の行を照会する。読み取りのみ）
	/// </summary>
	[EnumMember]
	Msg061_ManualLockStatus = 61,
	/// <summary>
	/// マニュアル排他制御の強制クリア（SysSeqType=1の行を全件削除し履歴を残す）
	/// </summary>
	[EnumMember]
	Msg062_ManualLockClear = 62,
	/// <summary>
	/// POS: バーコードから商品を検索する
	/// </summary>
	[EnumMember]
	Msg070_PosLookupProduct = 70,
	/// <summary>
	/// POS: 売上または返品を確定する
	/// </summary>
	[EnumMember]
	Msg071_PosCheckout = 71,
	/// <summary>
	/// POS: 売上を取り消す
	/// </summary>
	[EnumMember]
	Msg072_PosCancelSale = 72,
	/// <summary>
	/// POS: 日次精算を保存する
	/// </summary>
	[EnumMember]
	Msg073_PosSaveSeisan = 73,
	/// <summary>
	/// DBデータを取得する
	/// </summary>
	[EnumMember]
	Msg101_Op_Query = 101,
	/// <summary>
	/// DBデータを操作
	/// </summary>
	[EnumMember]
	Msg201_Op_Execute = 111,
	/// <summary>
	/// データ出力: DataTypeにより処理分岐
	/// </summary>
	[EnumMember]
	Msg300_Op_OutData = 121,
	/// <summary>
	/// テスト用メッセージ開始値
	/// </summary>
	[EnumMember]
	Msg700_Test_Start = 7700,
	[EnumMember]
	Msg701_TestCase001 = 7701,
	[EnumMember]
	Msg702_TestCase002 = 7702,
	/// <summary>
	/// ストリーミングテスト
	/// </summary>
	[EnumMember]
	Msg710_StreamingTest = 7710,
	/// <summary>
	/// Abs()がこの値より大きいものはエラー
	/// [Values where Abs() exceeds this value are errors]
	/// </summary>
	[EnumMember]
	Msg800_Error_Start = 9800,
	/// <summary>
	/// 未実装エラー QueryDbResult
	/// [Unimplemented error QueryDbResult]
	/// </summary>
	[EnumMember]
	Msg801_Error_Unimplemented = 9801,
	/// <summary>
	/// Exceptionエラー QueryDbResult
	/// [Exception error QueryDbResult]
	/// </summary>
	[EnumMember]
	Msg802_Error_ExceptionOccurred = 9802,
	/// <summary>
	/// 未実装エラー
	/// [Not implemented error]
	/// </summary>
	[EnumMember]
	Msg901_Error_Unimplemented = 9901,
	/// <summary>
	/// Exceptionエラー
	/// [Exception error]
	/// </summary>
	[EnumMember]
	Msg902_Error_ExceptionOccurred = 9902,
	/// <summary>
	/// 最大値4桁 9000以降はエラー等
	/// [Maximum value 4 digits 9000 and later are errors etc.]
	/// </summary>
	[EnumMember]
	Msg999_Zetc = 9999
}
/// <summary>
/// Contract:gRPC公開サービス
/// [Contract: gRPC Public Service]
/// </summary>
[ServiceContract]
public interface ICoreService {
	/// <summary>
	/// MSG種別に応じたリクエストを送信する
	/// [Send general request]
	/// </summary>
	/// <param name="request">パラメータは1つのみ</param>
	/// <param name="context"></param>
	/// <returns></returns>
	[OperationContract]
	Task<CvMsg> QueryMsgAsync(CvMsg request, CallContext context = default);
	/// <summary>
	/// ストリーミングでMSG種別に応じたリクエストを送信する
	/// </summary>
	/// <param name="request">パラメータは1つのみ</param>
	/// <param name="context"></param>
	/// <returns></returns>
	[OperationContract]
	IAsyncEnumerable<StreamMsg> QueryMsgStreamAsync(CvMsg request, CallContext context = default);
	/// <summary>
	/// ストリーミングで印刷操作リクエストを送信する
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	[OperationContract]
	IAsyncEnumerable<PrintOperation> PrintPdfAsync(PrintOperation request, CallContext context = default);
}
