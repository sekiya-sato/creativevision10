using CodeShare;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Base.Share;
using Cvnet10DomainLogic;
using ProtoBuf.Grpc;
using System.Collections;

namespace Cvnet10Server.Services;

public partial class CvnetCoreService {
	private const int NotFoundCode = -1;
	private const int ConcurrentUpdateCode = -9901;
	private const int UnexpectedErrorCode = -9902;
	private const string ConcurrentUpdateMessage = "他で更新されています";

	private CvnetMsg HandleCopyReply(CvnetMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogDebug("HandleCopyReply invoked Flag:{Flag}", request.Flag);

		return CreateSuccessResponse(request.Flag, request.DataType, request.DataMsg);
	}

	private CvnetMsg HandleGetVersion(CvnetMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogDebug("HandleGetVersion invoked Flag:{Flag}", request.Flag);

		return CreateSuccessResponse(request.Flag, typeof(VersionInfo), Common.SerializeObject(new AppGlobal().VerInfo));
	}

	private CvnetMsg HandleGetEnv(CvnetMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogDebug("HandleGetEnv invoked Flag:{Flag}", request.Flag);

		return CreateSuccessResponse(request.Flag, typeof(Dictionary<string, string>), Common.SerializeObject(GetEnvironmentVariables()));
	}

	private CvnetMsg HandlerGetTableCounts(CvnetMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogInformation("HandleGetTableCounts invoked Flag:{Flag}", request.Flag);
		var resultData = new List<Tuple<string, long>>();
		try {
			resultData = _db.GetTableCounts();
		}
		catch (Exception ex) {
			_logger.LogError(ex, "HandleGetTableCounts error");
			return CreateExceptionResponse(request.Flag, ex, typeof(string), ex.Message);
		}
		return CreateSuccessResponse(request.Flag, typeof(List<Tuple<string, long>>), Common.SerializeObject(resultData));
	}

	/// <summary>
	/// Query系の処理
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	/// <exception cref="NotImplementedException"></exception>
	CvnetMsg HandleOpQuery(CvnetMsg request, CallContext context = default) {
		ArgumentNullException.ThrowIfNull(request);

		var param = Common.DeserializeObject(request.DataMsg ?? string.Empty, request.DataType);
		return param switch {
			QueryOneParam queryOne => HandleQueryOne(request.Flag, queryOne),
			QuerybyIdParam queryById => HandleQueryById(request.Flag, queryById),
			QueryListSqlParam querySql => HandleQueryListSql(request.Flag, querySql),
			QueryListParam queryList => HandleQueryList(request.Flag, queryList),
			_ => throw new NotImplementedException(),
		};
	}

	/// <summary>
	/// Execute系の処理
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	/// <exception cref="NotImplementedException"></exception>
	CvnetMsg HandleOpExecute(CvnetMsg request, CallContext context = default) {
		ArgumentNullException.ThrowIfNull(request);

		var param = Common.DeserializeObject(request.DataMsg ?? string.Empty, request.DataType);
		return param switch {
			InsertParam insert => HandleInsert(request.Flag, insert),
			InsertBulkParam insertBulk => HandleBulkInsert(request.Flag, insertBulk),
			UpdateParam update => HandleUpdate(request.Flag, update),
			DeleteParam delete => HandleDelete(request.Flag, delete),
			DeleteByIdParam deleteById => HandleDeleteById(request.Flag, deleteById),
			_ => throw new NotImplementedException(),
		};
	}

	CvnetMsg HandleOpHhtMaster(CvnetMsg request, CallContext context = default) {
		ArgumentNullException.ThrowIfNull(request);

		var param = Common.DeserializeObject(request.DataMsg ?? string.Empty, request.DataType);
		if (param is not Tuple<bool, int> createMasterParam) {
			throw new NotImplementedException();
		}

		_logger.LogInformation("パラメータ HhtMaster isFix={IsFix} outMasterMei={OutMasterMei} 内容={Payload}", createMasterParam.Item1, createMasterParam.Item2, Common.SerializeObject(createMasterParam));

		try {
			var list = new HhtProcess(_db).CreateMaster(createMasterParam.Item1, createMasterParam.Item2);
			return CreateSuccessResponse(request.Flag, typeof(List<string>), Common.SerializeObject(list));
		}
		catch (Exception ex) {
			return CreateExceptionResponse(request.Flag, ex, typeof(List<string>), request.DataMsg);
		}
	}

	CvnetMsg HandleOpHhtReceive(CvnetMsg request, CallContext context = default) {
		ArgumentNullException.ThrowIfNull(request);

		var param = Common.DeserializeObject(request.DataMsg ?? string.Empty, request.DataType);
		if (param is not List<TranHhtdata> createMasterParam) {
			throw new NotImplementedException();
		}
		try {
			var hhtdata = param as List<TranHhtdata> ?? new List<TranHhtdata>();

			var cnt = new HhtProcess(_db).ReceiveHhtdata(hhtdata);
			return CreateSuccessResponse(request.Flag, typeof(int), Common.SerializeObject(cnt));
		}
		catch (Exception ex) {
			return CreateExceptionResponse(request.Flag, ex, typeof(List<string>), request.DataMsg);
		}
	}

	private CvnetMsg HandleQueryOne(CvnetFlag flag, QueryOneParam queryOne) {
		_logger.LogInformation("パラメータ QueryOneParam.ItemType={ItemType} 内容={Payload}", queryOne.ItemType, Common.SerializeObject(queryOne));

		var sql = queryOne.AddWhere();
		try {
			var data = _db.Fetch(queryOne.ItemType, sql, queryOne.Parameters).FirstOrDefault();
			return data == null
				? CreateNotFoundResponse(flag)
				: CreateSuccessResponse(flag, data.GetType(), Common.SerializeObject(data));
		}
		catch (Exception ex) {
			return CreateExceptionResponse(flag, ex, typeof(string), ex.Message);
		}
	}

	private CvnetMsg HandleQueryById(CvnetFlag flag, QuerybyIdParam queryById) {
		_logger.LogInformation("パラメータ QuerybyIdParam.ItemType={ItemType} 内容={Payload}", queryById.ItemType, Common.SerializeObject(queryById));

		try {
			var data = _db.Fetch(queryById.ItemType, "where Id = @0", queryById.Id).FirstOrDefault();
			return data == null
				? CreateNotFoundResponse(flag)
				: CreateSuccessResponse(flag, data.GetType(), Common.SerializeObject(data));
		}
		catch (Exception ex) {
			return CreateExceptionResponse(flag, ex, typeof(string), ex.Message);
		}
	}

	private CvnetMsg HandleQueryList(CvnetFlag flag, QueryListParam queryList) {
		var sql = BuildQueryListSql(queryList);
		var listType = typeof(List<>).MakeGenericType(queryList.ItemType);

		_logger.LogInformation("パラメータ QueryListParam.ItemType={ItemType} 内容={Payload} SQL={Sql}", queryList.ItemType, Common.SerializeObject(queryList), sql);

		try {
			var list = _db.Fetch(queryList.ItemType, sql, queryList.Parameters);
			return list == null || list.Count == 0
				? CreateNotFoundResponse(flag, listType, "[]")
				: CreateSuccessResponse(flag, listType, Common.SerializeObject(list));
		}
		catch (Exception ex) {
			return CreateExceptionResponse(flag, ex, typeof(string), ex.Message);
		}
	}

	private CvnetMsg HandleQueryListSql(CvnetFlag flag, QueryListSqlParam querySql) {
		var sql = querySql.Sql ?? string.Empty;
		var listType = typeof(List<>).MakeGenericType(querySql.ItemType);

		_logger.LogInformation("パラメータ QueryListSqlParam.ItemType={ItemType} 内容={Payload} SQL={Sql}", querySql.ItemType, Common.SerializeObject(querySql), sql);

		try {
			var list = _db.Fetch(querySql.ItemType, sql, querySql.Parameters);
			return list == null || list.Count == 0
				? CreateNotFoundResponse(flag, listType, "[]")
				: CreateSuccessResponse(flag, listType, Common.SerializeObject(list));
		}
		catch (Exception ex) {
			return CreateExceptionResponse(flag, ex, typeof(string), ex.Message);
		}
	}

	private CvnetMsg HandleInsert(CvnetFlag flag, InsertParam insert) {
		_logger.LogInformation("パラメータ InsertParam.ItemType={ItemType} 内容={Payload}", insert.ItemType, Common.SerializeObject(insert));

		var item = insert.GetItemObject();
		SetCreatedAuditValues(insert.ItemType, item);

		try {
			_ = _db.Insert(item);
			return CreateSuccessResponse(flag, item.GetType(), Common.SerializeObject(item));
		}
		catch (Exception ex) {
			return CreateExceptionResponse(flag, ex, item.GetType(), Common.SerializeObject(item));
		}
	}
	private CvnetMsg HandleBulkInsert(CvnetFlag flag, InsertBulkParam insertBulk) {
		_logger.LogInformation("パラメータ InsertBulkParam.ItemType={ItemType} 内容={Payload}", insertBulk.ItemType, Common.SerializeObject(insertBulk));

		// JSON配列 → List<ItemType> にデシリアライズ
		var listType = typeof(List<>).MakeGenericType(insertBulk.ItemType);
		var items = Common.DeserializeObject(insertBulk.Item, listType);
		if (items is not IList list || list.Count == 0) {
			return CreateNotFoundResponse(flag, listType, "[]");
		}
		try {
			_db.BeginTransaction();
			foreach (var item in list) {
				SetCreatedAuditValues(insertBulk.ItemType, item);
				_db.Insert(item);
			}
			_db.CompleteTransaction();
			return CreateSuccessResponse(flag, listType, Common.SerializeObject(list));
		}
		catch (Exception ex) {
			return CreateExceptionResponse(flag, ex, listType, Common.SerializeObject(list));
		}
	}

	private CvnetMsg HandleUpdate(CvnetFlag flag, UpdateParam update) {
		_logger.LogInformation("パラメータ UpdateParam.ItemType={ItemType} 内容={Payload}", update.ItemType, Common.SerializeObject(update));

		var item = update.GetItemObject();
		if (!typeof(BaseDbClass).IsAssignableFrom(update.ItemType) || item is not BaseDbClass db) {
			throw new NotImplementedException();
		}

		var vdate = Common.GetVdate();
		try {
			var orgItem = FetchExistingBaseDbItem(update.ItemType, db.Id);
			if (orgItem is not BaseDbClass org) {
				throw new NotImplementedException();
			}

			if (db.Vdu != org.Vdu) {
				return CreateErrorResponse(flag, ConcurrentUpdateCode, ConcurrentUpdateMessage, item.GetType(), Common.SerializeObject(item));
			}

			db.Vdu = vdate;
			_db.Update(item);
			return CreateSuccessResponse(flag, item.GetType(), Common.SerializeObject(item));
		}
		catch (Exception ex) when (ex is not NotImplementedException) {
			return CreateExceptionResponse(flag, ex, update.ItemType, Common.SerializeObject(item));
		}
	}

	private CvnetMsg HandleDelete(CvnetFlag flag, DeleteParam delete) {
		_logger.LogInformation("パラメータ DeleteParam.ItemType={ItemType} 内容={Payload}", delete.ItemType, Common.SerializeObject(delete));

		var item = delete.GetItemObject();
		if (!typeof(BaseDbClass).IsAssignableFrom(delete.ItemType) || item is not BaseDbClass db) {
			throw new NotImplementedException();
		}

		var orgItem = FetchExistingBaseDbItem(delete.ItemType, db.Id);
		if (orgItem is not BaseDbClass org) {
			throw new NotImplementedException();
		}

		if (db.Vdu != org.Vdu) {
			return CreateErrorResponse(flag, ConcurrentUpdateCode, ConcurrentUpdateMessage, item.GetType(), Common.SerializeObject(item));
		}

		_db.Delete(item);
		return CreateSuccessResponse(flag, delete.ItemType, Common.SerializeObject(item));
	}

	private CvnetMsg HandleDeleteById(CvnetFlag flag, DeleteByIdParam deleteById) {
		_logger.LogInformation("パラメータ DeleteByIdParam.ItemType={ItemType} Id={Id} 内容={Payload}", deleteById.ItemType, deleteById.Id, Common.SerializeObject(deleteById));

		var item = FetchExistingBaseDbItem(deleteById.ItemType, deleteById.Id);
		if (!typeof(BaseDbClass).IsAssignableFrom(deleteById.ItemType) || item is not BaseDbClass db) {
			throw new NotImplementedException();
		}

		var orgItem = FetchExistingBaseDbItem(deleteById.ItemType, db.Id);
		if (orgItem is not BaseDbClass org) {
			throw new NotImplementedException();
		}

		if (deleteById.OriginalVdu != org.Vdu) {
			return CreateErrorResponse(flag, ConcurrentUpdateCode, ConcurrentUpdateMessage, item.GetType(), Common.SerializeObject(item));
		}

		_db.Delete(item);
		return CreateSuccessResponse(flag, item.GetType(), Common.SerializeObject(item));
	}

	private static Dictionary<string, string> GetEnvironmentVariables() {
#pragma warning disable RS1035
		var envVars = Environment.GetEnvironmentVariables();
#pragma warning restore RS1035

		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (DictionaryEntry entry in envVars) {
			var key = entry.Key?.ToString() ?? string.Empty;
			var value = entry.Value?.ToString() ?? string.Empty;
			result[key] = value;
		}

		return result;
	}

	private string BuildQueryListSql(QueryListParam queryList) {
		if (queryList is QueryListSimpleParam) {
			return $"select Id,Vdc,Vdu,Code,Name,Ryaku,Kana From {_db.GetTableName(queryList.ItemType)} {queryList.AddWhereOrder()}";
		}

		return queryList.AddWhereOrder();
	}

	private void SetCreatedAuditValues(Type itemType, object item) {
		if (!typeof(BaseDbClass).IsAssignableFrom(itemType) || item is not BaseDbClass db) {
			return;
		}

		var vdate = Common.GetVdate();
		db.Vdc = vdate;
		db.Vdu = vdate;
	}

	private object FetchExistingBaseDbItem(Type itemType, object id) {
		return _db.Fetch(itemType, "where Id=@0", id)?.First() ?? new();
	}

	private static CvnetMsg CreateSuccessResponse(CvnetFlag flag, Type? dataType, string? dataMsg) {
		return new CvnetMsg {
			Flag = flag,
			Code = 0,
			DataType = dataType ?? typeof(string),
			DataMsg = dataMsg ?? string.Empty,
		};
	}

	private static CvnetMsg CreateNotFoundResponse(CvnetFlag flag, Type? dataType = null, string? dataMsg = null) {
		return new CvnetMsg {
			Flag = flag,
			Code = NotFoundCode,
			DataType = dataType ?? typeof(string),
			DataMsg = dataMsg ?? string.Empty,
		};
	}

	private static CvnetMsg CreateExceptionResponse(CvnetFlag flag, Exception ex, Type? dataType, string? dataMsg) {
		return CreateErrorResponse(flag, UnexpectedErrorCode, ex.Message, dataType, dataMsg);
	}

	private static CvnetMsg CreateErrorResponse(CvnetFlag flag, int code, string? option, Type? dataType, string? dataMsg) {
		return new CvnetMsg {
			Flag = flag,
			Code = code,
			Option = option ?? string.Empty,
			DataType = dataType ?? typeof(string),
			DataMsg = dataMsg ?? string.Empty,
		};
	}
}
