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
}

/// <summary>
/// スケジュール追加要求
/// [Request for adding a scheduled task]
/// </summary>
[DataContract]
public sealed class AddSchedulerTaskRequest {
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
public sealed class RemoveSchedulerTaskRequest {
	[DataMember(Order = 1)]
	public string TaskId { get; set; } = string.Empty;
}

[DataContract]
public sealed class SchedulerResult {
	[DataMember(Order = 1)]
	public int Result { get; set; }
	[DataMember(Order = 2)]
	public string Detail { get; set; } = string.Empty;
	[DataMember(Order = 3)]
	public string TaskId { get; set; } = string.Empty;
}


[ServiceContract]
public interface ICvnetScheduler {
	[OperationContract]
	Task<SchedulerResult> AddOneTaskAsync(AddSchedulerTaskRequest request, CallContext context = default);

	[OperationContract]
	Task<SchedulerResult> RemoveOneTaskAsync(RemoveSchedulerTaskRequest request, CallContext context = default);

	[OperationContract]
	Task<SchedulerResult> RemoveAllTaskAsync(CallContext context = default);
}
