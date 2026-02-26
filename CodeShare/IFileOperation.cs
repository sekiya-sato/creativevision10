using ProtoBuf.Grpc;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace CodeShare;

/// <summary>
/// ファイル送受信メッセージ
/// </summary>
[DataContract]
public sealed class FileOperation {
	/// <summary>
	/// ファイル名
	/// [File name]
	/// </summary>
	[DataMember(Order = 1)]
	public required string FileName { get; set; }
	/// <summary>
	/// ファイルデータ(100MBまで)
	/// [File data (up to 100MB)]
	/// </summary>
	[DataMember(Order = 2)]
	public byte[] FileData { get; set; } = Array.Empty<byte>();
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
	/// <summary>
	/// エラーメッセージ
	/// </summary>
	[DataMember(Order = 5)]
	public string ErroMessage { get; set; } = string.Empty;
}
/// <summary>
/// Contract:gRPC公開サービス
/// </summary>
[ServiceContract]
public interface IFileOperation {
	/// <summary>
	/// ファイル操作リクエストを送信する
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	[OperationContract]
	Task<FileOperation> FileOperationAsync(FileOperation request, CallContext context = default);
}
