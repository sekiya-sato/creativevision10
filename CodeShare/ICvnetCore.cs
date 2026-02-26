using ProtoBuf.Grpc;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace CodeShare;

/// <summary>
/// Contract:共通メッセージClass
/// </summary>
[DataContract]
public sealed class CvnetMsg {
	/// <summary>
	/// メッセージ種別
	/// </summary>
	[DataMember(Order = 1)]
	public required CvnetFlag Flag { get; set; }
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
	public required CvnetFlag Flag { get; init; }
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
public enum CvnetFlag {
	/// <summary>
	/// サーバーに送信されたメッセージをそのまま返す Message=送信メッセージ
	/// [Return the message sent to the server as it is. Message=Sent message]
	/// </summary>
	Msg001_CopyReply = 1,
	/// <summary>
	/// サーバーのバージョン情報を返す Message=CommonEnvのJSON文字列
	/// [Return the server version information. Message=JSON string of CommonEnv]
	/// </summary>
	Msg002_GetVersion = 2,
	/// <summary>
	/// サーバーの環境情報を返す Message=環境変数配列のJSON文字列
	/// [Return the server environment information. Message=JSON string of environment variable array]
	/// </summary>
	Msg003_GetEnv = 3,
	/// <summary>
	/// データベースの変換(テーブル初期化なし)
	/// </summary>
	MSg040_ConvertDb = 40,
	/// <summary>
	/// データベースの変換(テーブル初期化あり)
	/// </summary>
	MSg041_ConvertDbInit = 41,
	/// <summary>
	/// テスト
	/// </summary>
	MSg050_Test = 50,
	MSg060_StreamingTest = 60,
	/// <summary>
	/// DBデータを取得する
	/// </summary>
	Msg101_Op_Query = 101,
	/// <summary>
	/// DBデータを操作
	/// </summary>
	Msg201_Op_Execute = 201,
	/// <summary>
	/// テスト用メッセージ開始値
	/// </summary>
	Msg700_Test_Start = 7700,
	Msg701_TestCase001 = 7701,
	Msg702_TestCase002 = 7702,
	/// <summary>
	/// Abs()がこの値より大きいものはエラー
	/// [Values where Abs() exceeds this value are errors]
	/// </summary>
	Msg800_Error_Start = 9800,
	/// <summary>
	/// 未実装エラー QueryDbResult
	/// [Unimplemented error QueryDbResult]
	/// </summary>
	Msg801_Error_Unimplemented = 9801,
	/// <summary>
	/// Exceptionエラー QueryDbResult
	/// [Exception error QueryDbResult]
	/// </summary>
	Msg802_Error_ExceptionOccured = 9802,
	/// <summary>
	/// 未実装エラー
	/// [Not implemented error]
	/// </summary>
	Msg901_Error_Unimplemented = 9901,
	/// <summary>
	/// Exceptionエラー
	/// [Exception error]
	/// </summary>
	Msg902_Error_ExceptionOccured = 9902,
	/// <summary>
	/// 最大値4桁 9000以降はエラー等
	/// [Maximum value 4 digits 9000 and later are errors etc.]
	/// </summary>
	Msg999_Zetc = 9999
}
/// <summary>
/// Contract:gRPC公開サービス
/// [Contract: gRPC Public Service]
/// </summary>
[ServiceContract]
public interface ICvnetCoreService {
	/// <summary>
	/// 一般リクエストを送信する
	/// [Send general request]
	/// </summary>
	/// <param name="request">パラメータは1つのみ</param>
	/// <param name="context"></param>
	/// <returns></returns>
	[OperationContract]
	Task<CvnetMsg> QueryMsgAsync(CvnetMsg request, CallContext context = default);

	/// <summary>
	/// ストリーミングで一般リクエストを送信する
	/// </summary>
	/// <param name="request">パラメータは1つのみ</param>
	/// <param name="context"></param>
	/// <returns></returns>
	[OperationContract]
	IAsyncEnumerable<StreamMsg> QueryMsgStreamAsync(CvnetMsg request, CallContext context = default);
}
