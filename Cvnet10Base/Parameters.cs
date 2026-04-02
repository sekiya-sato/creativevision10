using Newtonsoft.Json;
using System.Runtime.Serialization;

namespace Cvnet10Base;

public interface IJsonPayload {
	Type ItemType { get; }
	string Item { get; }
	object GetItemObject();
}


public sealed class QueryOneParam {
	public string? Where { get; }
	public string[] Parameters { get; }
	public Type ItemType { get; }
	public string AddWhere() {
		var retstr =
			(!string.IsNullOrWhiteSpace(Where) ? $" where {Where}" : string.Empty);
		return retstr;
	}
	public QueryOneParam(Type itemType, string? where = null, string[]? parameters = null) {
		Where = where;
		if (parameters != null)
			Parameters = parameters;
		else
			Parameters = Array.Empty<string>();
		ItemType = itemType;
	}
}
public sealed class QuerybyIdParam {
	public long Id { get; }
	public Type ItemType { get; }
	public QuerybyIdParam(Type itemType, long id) {
		Id = id;
		ItemType = itemType;
	}
}

public class QueryListParam {
	public string? Where { get; }
	public string? Order { get; }
	public string[] Parameters { get; }
	public Type ItemType { get; }
	public int? MaxCount { get; }
	public string AddWhereOrder() {
		var retstr =
			(!string.IsNullOrWhiteSpace(Where) ? $" where {Where}" : string.Empty) +
		(!string.IsNullOrWhiteSpace(Order) ? $" order by {Order}" : string.Empty) +
		(MaxCount.HasValue && MaxCount.Value > 0 ? $" limit {MaxCount.Value}" : string.Empty);
		return retstr;
	}
	public QueryListParam(Type itemType, string? where = null, string? order = null, string[]? parameters = null, int? maxCount = null) {
		Where = where;
		Order = order;
		if (parameters != null)
			Parameters = parameters;
		else
			Parameters = Array.Empty<string>();
		ItemType = itemType;
		MaxCount = maxCount;
	}
}
public sealed class QueryListSimpleParam : QueryListParam {
	public QueryListSimpleParam(Type itemType, string? where = null, string? order = null, string[]? parameters = null, int? maxCount = null)
		: base(itemType, where, order, parameters, maxCount) {
	}
}


public sealed class QueryListSqlParam {
	public string? Sql { get; }
	public string[] Parameters { get; }
	public Type ItemType { get; }
	public QueryListSqlParam(Type itemType, string? sql = null, string[]? parameters = null) {
		Sql = sql;
		if (parameters != null)
			Parameters = parameters;
		else
			Parameters = Array.Empty<string>();
		ItemType = itemType;
	}
}

/// <summary>
/// クエリI/F : Item指定挿入パラメータ
/// </summary>

public sealed class InsertParam : IJsonPayload {
	public string Item { get; }
	public Type ItemType { get; }
	public InsertParam(Type itemType, string item) {
		Item = item;
		ItemType = itemType;
	}
	public object GetItemObject() {
		var item = JsonConvert.DeserializeObject(Item, ItemType);
		if (item == null)
			throw new SerializationException();
		return item;
	}
}
/// <summary>
/// クエリI/F : Item指定挿入パラメータ
/// </summary>

public sealed class InsertBulkParam : IJsonPayload {
	public string Item { get; }
	public Type ItemType { get; }
	public InsertBulkParam(Type itemType, string item) {
		Item = item;
		ItemType = itemType;
	}
	public object GetItemObject() {
		var item = JsonConvert.DeserializeObject(Item, ItemType);
		if (item == null)
			throw new SerializationException();
		return item;
	}
}
/// <summary>
/// クエリI/F : Item指定修正パラメータ
/// </summary>
public sealed class UpdateParam : IJsonPayload {
	public string Item { get; }
	public Type ItemType { get; }
	public UpdateParam(Type itemType, string item) {
		Item = item;
		ItemType = itemType;
	}
	public object GetItemObject() {
		var item = JsonConvert.DeserializeObject(Item, ItemType);
		if (item == null)
			throw new SerializationException();
		return item;
	}
}
/// <summary>
/// クエリI/F : Item指定削除パラメータ
/// </summary>
public sealed class DeleteParam : IJsonPayload {
	public string Item { get; }
	public Type ItemType { get; }
	public DeleteParam(Type itemType, string item) {
		Item = item;
		ItemType = itemType;
	}
	public object GetItemObject() {
		var item = JsonConvert.DeserializeObject(Item, ItemType);
		if (item == null)
			throw new SerializationException();
		return item;
	}
}
/// <summary>
/// クエリI/F : ID指定削除パラメータ
/// </summary>
public sealed class DeleteByIdParam {
	public long Id { get; }
	public Type ItemType { get; }
	public long OriginalVdu { get; }
	public DeleteByIdParam(Type itemType, long id, long originalVdu) {
		Id = id;
		ItemType = itemType;
		OriginalVdu = originalVdu;
	}
}
/// <summary>
/// データ出力I/F : HHTマスタデータ作成パラメータ
/// </summary>
public sealed class OutDataHhtMasterParam {
	public bool IsFixedLengthFormat { get; set; }
	public int ParamIntNoUse { get; set; }
	public OutDataHhtMasterParam(bool isFixedLengthFormat, int paramIntNoUse) {
		IsFixedLengthFormat = isFixedLengthFormat;
		ParamIntNoUse = paramIntNoUse;
	}
}

