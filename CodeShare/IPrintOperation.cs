using System.Runtime.Serialization;

namespace CodeShare;

/// <summary>
/// ファイル送受信メッセージ
/// </summary>
[DataContract]
public sealed class PrintOperation {
	/// <summary>
	/// メッセージ型
	/// </summary>
	[DataMember(Order = 1)]
	public Type DataType { get; set; } = typeof(string);
	/// <summary>
	/// メッセージ本体
	/// </summary>
	[DataMember(Order = 2)]
	public string DataMsg { get; set; } = string.Empty;
	/// <summary>
	/// 処理ステータス -1 はエラー
	/// [Processing status, -1 indicates an error]
	/// </summary>
	[DataMember(Order = 3)]
	public int Status { get; set; }
	/// <summary>
	/// 処理ステータスメッセージ
	/// </summary>
	[DataMember(Order = 4)]
	public string StatusString { get; set; } = string.Empty;
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
}
