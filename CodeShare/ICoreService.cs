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
	/// データベースの変換(テーブル初期化なし)
	/// </summary>
	[EnumMember]
	Msg040_ConvertDb = 40,
	/// <summary>
	/// データベースの変換(テーブル初期化あり)
	/// </summary>
	[EnumMember]
	Msg041_ConvertDbInit = 41,
	/// <summary>
	/// テーブル一覧と件数の取得
	/// </summary>
	[EnumMember]
	Msg042_GetTableList = 42,
	/// <summary>
	/// タスクリストの取得
	/// </summary>
	[EnumMember]
	Msg043_ConvertList = 43,
	/// <summary>
	/// データベースの変換(選択されたタスクのみ)
	/// </summary>
	[EnumMember]
	Msg044_ConvertSelected = 44,
	[EnumMember]
	Msg045_ConvertSelectedInit = 45,
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
	/// DBデータを取得する
	/// </summary>
	[EnumMember]
	Msg101_Op_Query = 101,
	/// <summary>
	/// DBデータを操作
	/// </summary>
	[EnumMember]
	Msg201_Op_Execute = 201,
	/// <summary>
	/// データ出力: DataTypeにより処理分岐
	/// </summary>
	[EnumMember]
	Msg300_Op_OutData = 300,
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
	Msg802_Error_ExceptionOccured = 9802,
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
	Msg902_Error_ExceptionOccured = 9902,
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
