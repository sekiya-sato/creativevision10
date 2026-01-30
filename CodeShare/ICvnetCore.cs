using ProtoBuf.Grpc;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace CodeShare;

/// <summary>
/// Contract:共通メッセージClass
/// </summary>
[DataContract]
public class CvnetMsg {
	/// <summary>
	/// メッセージ種別
	/// </summary>
	[DataMember(Order = 1)]
	public required CvnetFlag Flag { get; set; }
	/// <summary>
	/// コード（リターンコード、その他）
	/// </summary>
	[DataMember(Order = 2)]
	public int Code;
	/// <summary>
	/// メッセージ型
	/// </summary>
	[DataMember(Order = 3)]
	public Type DataType { get; set; } = typeof(string);
	/// <summary>
	/// メッセージ本体
	/// </summary>
	[DataMember(Order = 4)]
	public string? DataMsg { get; set; }

}
/// <summary>
/// 共通メッセージフラグ
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
	/// データベースの変換
	/// </summary>
	MSg004_ConvertDb = 4,
	/// <summary>
	/// テスト
	/// </summary>
	MSg005_Test = 5,
	/// <summary>
	/// DBデータを取得する Message=QueryDbDef
	/// [Get DB data Message=QueryDbDef]
	/// </summary>
	Msg101_GetDbQuerySingle = 101,
	/// <summary>
	/// DBデータを操作する Message=QueryDbDef
	/// [Operate on DB data Message=QueryDbDef]
	/// </summary>
	Msg102_GetDbExecuteSingle = 102,
	/// <summary>
	/// DBデータを複数取得する Message=List＜QueryDbHeader＞
	/// [Retrieve multiple DB data entries. Message = List＜QueryDbHeader＞]
	/// </summary>
	Msg103_GetDbQueryMulti = 103,
	/// <summary>
	/// DBデータの取得の結果 Message=QueryDbResult
	/// [Result of retrieving DB data. Message=QueryDbResult]
	/// </summary>
	Msg201_RetDbQuerySingle = 201,
	/// <summary>
	/// DBデータを操作した結果 Message=QueryDbResult
	/// [Result of operating on DB data. Message=QueryDbResult]
	/// </summary>
	Msg202_RetDbExecuteSingle = 202,
	/// <summary>
	/// DBデータの取得の結果 Message=List＜ResponseDbHeader＞
	/// [Result of retrieving DB data. Message=List＜ResponseDbHeader＞]
	/// </summary>
	Msg203_RetDbQueryMulti = 203,
	/// <summary>
	/// ファイルダウンロード
	/// [File download]
	/// </summary>
	Msg301_FileDownload = 301,
	/// <summary>
	/// ファイルアップロード
	/// [File upload]
	/// </summary>
	Msg302_FileUpload = 302,
	/// <summary>
	/// JSONベースのクエリリクエスト
	/// [JSON-based query request]
	/// </summary>
	Msg401_JsonRequest = 401,
	/// <summary>
	/// JSONベースのレスポンス
	/// [JSON-based response]
	/// </summary>
	Msg402_JsonResponse = 402,
	/// <summary>
	/// JSONベースのクエリリクエスト
	/// [JSON-based query request]
	/// </summary>
	Msg403_JsonQuery = 403,
	/// <summary>
	/// JSONベースのレスポンス
	/// [JSON-based response]
	/// </summary>
	Msg404_JsonQueryResponse = 403,
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
/// ファイル送受信メッセージ
/// </summary>
[DataContract]
public class FileOperation {
	/// <summary>
	/// ファイル名
	/// [File name]
	/// </summary>
	[DataMember(Order = 1)]
	public string? FileName { get; set; }
	/// <summary>
	/// ファイルデータ(100MBまで)
	/// [File data (up to 100MB)]
	/// </summary>
	[DataMember(Order = 2)]
	public byte[]? FileData { get; set; }
	/// <summary>
	/// ファイルサイズ
	/// [File size]
	/// </summary>
	[DataMember(Order = 3)]
	public long FileSize { get; set; }
	/// <summary>
	/// 処理ステータス -1 はエラー
	/// [Processing status, -1 indicates an error]
	/// </summary>
	[DataMember(Order = 4)]
	public int Status { get; set; }
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

	/*
	[OperationContract]
	Task<CvnetMsg> QueryJsonAsync(CvnetMsg request, CallContext context = default);
	/// <summary>
	/// ファイル操作リクエストを送信する
	/// [Send file operation request]
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	[OperationContract]
	Task<BinaryMsg> FileOperationAsync(BinaryMsg request, CallContext context = default);
	*/
}
