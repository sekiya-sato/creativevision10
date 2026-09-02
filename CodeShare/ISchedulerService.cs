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
	/// <summary>実行する/しないフラグ</summary>
	[DataMember(Order = 6)]
	public bool IsEnabled { get; set; } = true;
	/// <summary>起動間隔の下限チェック対象かどうか</summary>
	[DataMember(Order = 7)]
	public bool CheckMinInterval { get; set; }
	/// <summary>起動間隔の下限（分）。0はチェックなし</summary>
	[DataMember(Order = 8)]
	public int MinIntervalMinutes { get; set; }
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
}
