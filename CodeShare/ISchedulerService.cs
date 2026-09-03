using ProtoBuf.Grpc;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace CodeShare;

[DataContract]
public enum SchedulerTaskType {
	[EnumMember]
	Unknown = 0,
	[EnumMember]
	LogOnly = 1,
	[EnumMember]
	RunSummary = 2,
	[EnumMember]
	MasterShohinMeishoRebuild = 3,
	[EnumMember]
	MasterVColumnResync = 4,
	[EnumMember]
	TranTaxRebuild = 5,
}

/// <summary>
/// スケジュール追加要求
/// [Request for adding a scheduled task]
/// </summary>
[DataContract]
public sealed record class AddSchedulerTaskRequest {
	[DataMember(Order = 1)]
	public string CronExpression { get; set; } = "* * * * *";
	[DataMember(Order = 2)]
	public SchedulerTaskType TaskType { get; set; } = SchedulerTaskType.Unknown;
	[DataMember(Order = 3)]
	public string TaskName { get; set; } = string.Empty;
	[DataMember(Order = 4)]
	public string Payload { get; set; } = string.Empty;
	/// <summary>実行結果メールを送信するかどうか。動的追加タスクの既定は送信しない。</summary>
	[DataMember(Order = 5)]
	public bool IsSendMail { get; set; }
}

/// <summary>
/// スケジュール削除要求
/// [Request for removing a scheduled task]
/// </summary>
[DataContract]
public sealed record class RemoveSchedulerTaskRequest {
	[DataMember(Order = 1)]
	public string TaskId { get; set; } = string.Empty;
}

[DataContract]
public sealed record class SchedulerResult {
	[DataMember(Order = 1)]
	public int Result { get; set; }
	[DataMember(Order = 2)]
	public string Detail { get; set; } = string.Empty;
	[DataMember(Order = 3)]
	public string TaskId { get; set; } = string.Empty;
}

/// <summary>
/// スケジュールタスク情報
/// [Scheduler task information for listing]
/// </summary>
[DataContract]
public sealed record class SchedulerTaskInfo {
	[DataMember(Order = 1)]
	public string TaskId { get; set; } = string.Empty;
	[DataMember(Order = 2)]
	public string TaskName { get; set; } = string.Empty;
	[DataMember(Order = 3)]
	public string CronExpression { get; set; } = string.Empty;
	[DataMember(Order = 4)]
	public DateTime? NextOccurrence { get; set; }
	[DataMember(Order = 5)]
	public bool IsSystemTask { get; set; }
	/// <summary>
	/// 実行する/しないフラグ。protobuf-net は bool の false をワイヤに載せないため、既定値 true の初期化子を付けるとサーバが返した false が受信側で true のままになる。サーバは常に明示的に値を設定する。
	/// </summary>
	[DataMember(Order = 6)]
	public bool IsEnabled { get; set; }
	/// <summary>起動間隔の下限チェック対象かどうか</summary>
	[DataMember(Order = 7)]
	public bool CheckMinInterval { get; set; }
	/// <summary>起動間隔の下限（分）。0はチェックなし</summary>
	[DataMember(Order = 8)]
	public int MinIntervalMinutes { get; set; }
	[DataMember(Order = 9)]
	public bool IsSendMail { get; set; }
}

/// <summary>
/// スケジュールタスク一覧取得レスポンス
/// [Response for getting all scheduler tasks]
/// </summary>
[DataContract]
public sealed record class GetSchedulerTasksResponse {
	[DataMember(Order = 1)]
	public int Result { get; set; }
	[DataMember(Order = 2)]
	public string Detail { get; set; } = string.Empty;
	[DataMember(Order = 3)]
	public List<SchedulerTaskInfo> Tasks { get; set; } = [];
}

/// <summary>
/// スケジュールタスク更新要求
/// [Request for updating a scheduler task]
/// </summary>
[DataContract]
public sealed record class UpdateSchedulerTaskRequest {
	[DataMember(Order = 1)]
	public string TaskId { get; set; } = string.Empty;
	[DataMember(Order = 2)]
	public string CronExpression { get; set; } = string.Empty;
}


/// <summary>
/// スケジュールタスクの実行フラグ設定要求
/// [Request for enabling/disabling a scheduled task]
/// </summary>
[DataContract]
public sealed record class SetSchedulerTaskEnabledRequest {
	[DataMember(Order = 1)]
	public string TaskId { get; set; } = string.Empty;
	[DataMember(Order = 2)]
	public bool IsEnabled { get; set; }
}

/// <summary>
/// スケジュールタスクのメール送信フラグ設定要求
/// [Request for enabling/disabling scheduler task mail notifications]
/// </summary>
[DataContract]
public sealed record class SetSchedulerTaskSendMailRequest {
	[DataMember(Order = 1)]
	public string TaskId { get; set; } = string.Empty;
	[DataMember(Order = 2)]
	public bool IsSendMail { get; set; }
}

/// <summary>
/// 自動実行結果メールの設定値。値は MasterConfig に入る文字列そのままを運ぶ。
/// パスワードはこの型に含めない（サーバから返さない）。
/// </summary>
[DataContract]
public sealed record class AutoExecMailConfig {
	/// <summary>SMTPサーバーのIPアドレスまたはホスト名</summary>
	[DataMember(Order = 1)]
	public string Server { get; set; } = string.Empty;
	/// <summary>SMTPポート番号（文字列。不正値もそのまま往復させ、検証はサーバ側で行う）</summary>
	[DataMember(Order = 2)]
	public string Port { get; set; } = string.Empty;
	/// <summary>暗号化方式。指定できる値は <see cref="GetAutoExecMailConfigResponse.SecurityValues"/></summary>
	[DataMember(Order = 3)]
	public string Security { get; set; } = string.Empty;
	/// <summary>認証方式。指定できる値は <see cref="GetAutoExecMailConfigResponse.AuthModeValues"/></summary>
	[DataMember(Order = 4)]
	public string AuthMode { get; set; } = string.Empty;
	/// <summary>SMTP認証ユーザーID。認証方式が None なら空でよい</summary>
	[DataMember(Order = 5)]
	public string UserId { get; set; } = string.Empty;
	/// <summary>送信元アドレス</summary>
	[DataMember(Order = 6)]
	public string FromAddress { get; set; } = string.Empty;
	/// <summary>送信元表示名。任意。空ならアドレスのみで送信する</summary>
	[DataMember(Order = 7)]
	public string FromName { get; set; } = string.Empty;
	/// <summary>送信先アドレス。カンマまたはセミコロン区切りで複数指定できる</summary>
	[DataMember(Order = 8)]
	public string ToAddress { get; set; } = string.Empty;
}

/// <summary>
/// 自動実行結果メール設定の取得レスポンス
/// </summary>
[DataContract]
public sealed record class GetAutoExecMailConfigResponse {
	[DataMember(Order = 1)]
	public int Result { get; set; }
	[DataMember(Order = 2)]
	public string Detail { get; set; } = string.Empty;
	[DataMember(Order = 3)]
	public AutoExecMailConfig Config { get; set; } = new();
	/// <summary>
	/// パスワードが登録済みかどうか。値自体は返さない。
	/// protobuf-net は bool の false をワイヤに載せないため、既定値 true の初期化子は付けない。
	/// </summary>
	[DataMember(Order = 4)]
	public bool HasCredential { get; set; }
	/// <summary>現在保存されている設定でメール送信できるかどうか</summary>
	[DataMember(Order = 5)]
	public bool IsValid { get; set; }
	/// <summary>設定の検証結果の説明。<see cref="IsValid"/> が false のときの理由</summary>
	[DataMember(Order = 6)]
	public string ValidationDetail { get; set; } = string.Empty;
	/// <summary>暗号化方式に指定できる値の一覧（画面の選択肢用）</summary>
	[DataMember(Order = 7)]
	public List<string> SecurityValues { get; set; } = [];
	/// <summary>認証方式に指定できる値の一覧（画面の選択肢用）</summary>
	[DataMember(Order = 8)]
	public List<string> AuthModeValues { get; set; } = [];
}

/// <summary>
/// 自動実行結果メール設定の保存要求
/// </summary>
[DataContract]
public sealed record class SetAutoExecMailConfigRequest {
	[DataMember(Order = 1)]
	public AutoExecMailConfig Config { get; set; } = new();
	/// <summary>
	/// 新しいパスワード。空文字なら保存済みの値を変更しない。
	/// </summary>
	[DataMember(Order = 2)]
	public string Credential { get; set; } = string.Empty;
	/// <summary>保存済みのパスワードを消去する</summary>
	[DataMember(Order = 3)]
	public bool ClearCredential { get; set; }
}

[ServiceContract]
public interface ISchedulerService {
	[OperationContract]
	Task<SchedulerResult> AddTaskAsync(AddSchedulerTaskRequest request, CallContext context = default);

	[OperationContract]
	Task<SchedulerResult> RemoveTaskAsync(RemoveSchedulerTaskRequest request, CallContext context = default);

	[OperationContract]
	Task<SchedulerResult> RemoveAllTasksAsync(CallContext context = default);

	[OperationContract]
	Task<GetSchedulerTasksResponse> GetTasksAsync(CallContext context = default);

	[OperationContract]
	Task<SchedulerResult> UpdateTaskAsync(UpdateSchedulerTaskRequest request, CallContext context = default);

	/// <summary>スケジュールタスクの実行する/しないフラグを設定する。</summary>
	[OperationContract]
	Task<SchedulerResult> SetTaskEnabledAsync(SetSchedulerTaskEnabledRequest request, CallContext context = default);

	/// <summary>スケジュールタスクのメール送信する/しないフラグを設定する。</summary>
	[OperationContract]
	Task<SchedulerResult> SetTaskSendMailAsync(SetSchedulerTaskSendMailRequest request, CallContext context = default);

	/// <summary>自動実行結果メールの設定を取得する。パスワードは返さず、登録済みかどうかだけを返す。</summary>
	[OperationContract]
	Task<GetAutoExecMailConfigResponse> GetAutoExecMailConfigAsync(CallContext context = default);

	/// <summary>自動実行結果メールの設定を保存する。</summary>
	[OperationContract]
	Task<SchedulerResult> SetAutoExecMailConfigAsync(SetAutoExecMailConfigRequest request, CallContext context = default);

	/// <summary>保存済みの設定でテストメールを実際に送信し、結果を返す。</summary>
	[OperationContract]
	Task<SchedulerResult> TestSendAutoExecMailAsync(CallContext context = default);
}
