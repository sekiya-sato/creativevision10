using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBaseOracle;
using CvDomainLogic;
using ProtoBuf.Grpc;
using System.Collections;

namespace CvServer.Services;

public partial class CoreService {
	private const int NotFoundCode = -1;
	private const int ConcurrentUpdateCode = -9901;
	private const int UnexpectedErrorCode = -9902;
	private const string ConcurrentUpdateMessage = "他で更新されています";

	private CvMsg HandleCopyReply(CvMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogDebug("HandleCopyReply invoked Flag:{Flag}", request.Flag);

		return CreateSuccessResponse(request.Flag, request.DataType, request.DataMsg);
	}

	private CvMsg HandleGetVersion(CvMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogDebug("HandleGetVersion invoked Flag:{Flag}", request.Flag);

		return CreateSuccessResponse(request.Flag, typeof(InfoServer), Common.SerializeObject(new AppGlobal().VerInfo));
	}

	private CvMsg HandleGetEnv(CvMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogDebug("HandleGetEnv invoked Flag:{Flag}", request.Flag);

		return CreateSuccessResponse(request.Flag, typeof(Dictionary<string, string>), Common.SerializeObject(GetEnvironmentVariables()));
	}

	private CvMsg HandlerGetTableList(CvMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogInformation("HandleGetTableList invoked Flag:{Flag}", request.Flag);
		var resultData = new List<Tuple<string, string, long>>();
		try {
			resultData = _db.GetTableCounts();
		}
		catch (Exception ex) {
			_logger.LogError(ex, "HandleGetTableList error");
			return CreateExceptionResponse(request.Flag, ex, typeof(string), ex.Message);
		}
		return CreateSuccessResponse(request.Flag, typeof(List<Tuple<string, string, long>>), Common.SerializeObject(resultData));
	}
	private CvMsg HandlerGetConvertTaskList(CvMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogInformation("HandleGetConvertTaskList invoked Flag:{Flag}", request.Flag);
		var resultData = new List<string>();
		try {
			var oracleConnectionString = _configuration.GetConnectionString("oracle") ?? string.Empty;
			var fromDb = ExDatabaseOracle.GetDbConn(oracleConnectionString);

			var convertDb = new ConvertDb(fromDb, _db);

			resultData = convertDb.GetAllTaskNames();
		}
		catch (Exception ex) {
			_logger.LogError(ex, "HandleGetConvertTaskList error");
			return CreateExceptionResponse(request.Flag, ex, typeof(string), ex.Message);
		}
		return CreateSuccessResponse(request.Flag, typeof(List<string>), Common.SerializeObject(resultData));
	}
	private CvMsg HandleConvertMasterShohin(CvMsg request, CallContext context) {
		var rebuild = new RebuildDb(_db);
		var ret = rebuild.RebuildMasterShohin2Meisho();
		return CreateSuccessResponse(request.Flag, typeof(InfoServer), Common.SerializeObject(new AppGlobal().VerInfo));
	}
	/// <summary>
	/// Query系の処理
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	/// <exception cref="NotImplementedException"></exception>
	CvMsg HandleOpQuery(CvMsg request, CallContext context = default) {
		ArgumentNullException.ThrowIfNull(request);

		var param = Common.DeserializeObject(request.DataMsg ?? string.Empty, request.DataType);
		return param switch {
			QueryOneParam queryOne => HandleQueryOne(request.Flag, queryOne),
			QueryByIdParam queryById => HandleQueryById(request.Flag, queryById),
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
	CvMsg HandleOpExecute(CvMsg request, CallContext context = default) {
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

	private CvMsg HandleQueryOne(CvFlag flag, QueryOneParam queryOne) {
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

	private CvMsg HandleQueryById(CvFlag flag, QueryByIdParam queryById) {
		_logger.LogInformation("パラメータ QueryByIdParam.ItemType={ItemType} 内容={Payload}", queryById.ItemType, Common.SerializeObject(queryById));

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

	private CvMsg HandleQueryList(CvFlag flag, QueryListParam queryList) {
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

	private CvMsg HandleQueryListSql(CvFlag flag, QueryListSqlParam querySql) {
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

	private CvMsg HandleInsert(CvFlag flag, InsertParam insert) {
		_logger.LogInformation("パラメータ InsertParam.ItemType={ItemType} 内容={Payload}", insert.ItemType, Common.SerializeObject(insert));

		var item = insert.GetItemObject();
		SetCreatedAuditValues(insert.ItemType, item);

		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			var newItem = _db.Insert(item);
			if (typeof(IDerivedOrigin).IsAssignableFrom(insert.ItemType)) {
				new HandlerDerived(_db).Insert(insert.ItemType, item, ((BaseDbClass)item).Id);
			}
			else if (typeof(ITranSoko).IsAssignableFrom(insert.ItemType)) {
				var summaryDb = new SummaryDb(_db);
				var id = ((BaseDbClass)item).Id;
				summaryDb.CalcTran2SummaryStock(insert.ItemType.Name, "Id_Soko", id, false);
				if (typeof(ITranIdo).IsAssignableFrom(insert.ItemType)) {
					summaryDb.CalcTran2SummaryStock(insert.ItemType.Name, "Id_Ido", id, false);
				}
			}
			_db.CompleteTransaction();
			return CreateSuccessResponse(flag, item.GetType(), Common.SerializeObject(item));
		}
		catch (Exception ex) {
			_db.AbortTransaction();
			return CreateExceptionResponse(flag, ex, item.GetType(), Common.SerializeObject(item));
		}
	}
	/// <summary>
	/// 追加処理 ( ToDo: 付随する処理も同時に実行する)
	/// </summary>
	/// <param name="flag"></param>
	/// <param name="insertBulk"></param>
	/// <returns></returns>
	private CvMsg HandleBulkInsert(CvFlag flag, InsertBulkParam insertBulk) {
		_logger.LogInformation("パラメータ InsertBulkParam.ItemType={ItemType} 内容={Payload}", insertBulk.ItemType, Common.SerializeObject(insertBulk));

		// JSON配列 → List<ItemType> にデシリアライズ
		var listType = typeof(List<>).MakeGenericType(insertBulk.ItemType);
		var items = Common.DeserializeObject(insertBulk.Item, listType);
		if (items is not IList list || list.Count == 0) {
			return CreateNotFoundResponse(flag, listType, "[]");
		}
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			foreach (var item in list) {
				SetCreatedAuditValues(insertBulk.ItemType, item);
				_db.Insert(item);
				if (typeof(IDerivedOrigin).IsAssignableFrom(insertBulk.ItemType)) {
					new HandlerDerived(_db).Insert(insertBulk.ItemType, item, ((BaseDbClass)item).Id);
				}
				else if (typeof(ITranSoko).IsAssignableFrom(insertBulk.ItemType)) {
					var summaryDb = new SummaryDb(_db);
					var id = ((BaseDbClass)item).Id;
					summaryDb.CalcTran2SummaryStock(insertBulk.ItemType.Name, "Id_Soko", id, false);
					if (typeof(ITranIdo).IsAssignableFrom(insertBulk.ItemType)) {
						summaryDb.CalcTran2SummaryStock(insertBulk.ItemType.Name, "Id_Ido", id, false);
					}
				}
			}
			_db.CompleteTransaction();
			return CreateSuccessResponse(flag, listType, Common.SerializeObject(list));
		}
		catch (Exception ex) {
			_db.AbortTransaction();
			return CreateExceptionResponse(flag, ex, listType, Common.SerializeObject(list));
		}
	}
	/// <summary>
	/// 更新処理 ( ToDo: 付随する処理も同時に実行する)
	/// </summary>
	/// <param name="flag"></param>
	/// <param name="update"></param>
	/// <returns></returns>
	/// <exception cref="NotImplementedException"></exception>
	private CvMsg HandleUpdate(CvFlag flag, UpdateParam update) {
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
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			if (typeof(ITranSoko).IsAssignableFrom(update.ItemType)) {
				var summaryDb = new SummaryDb(_db);
				var id = ((BaseDbClass)item).Id;
				summaryDb.CalcTran2SummaryStock(update.ItemType.Name, "Id_Soko", id, true);
				if (typeof(ITranIdo).IsAssignableFrom(update.ItemType)) {
					summaryDb.CalcTran2SummaryStock(update.ItemType.Name, "Id_Ido", id, true);
				}
			}
			db.Vdu = vdate;
			_db.Update(item);
			// マスタのCode/Name変更を参照側のV*列へ伝播する(Master系のみ。Tran系のV*列は伝票の時点名称なので対象外)
			// vdate は必ず渡す: 自己参照(MasterTokui.Id_Paysakiが自分自身など)で更新元の行自身が伝播対象になり、
			// 別採番するとクライアントへ返す Vdu とDB上の Vdu がずれて次回保存が楽観排他で弾かれる
			if (MasterCascadeDb.NeedsCascade(update.ItemType, item, org) && item is IBaseCodeName codeName) {
				// kubun/oldCode は MasterMeisho の区分定義行(Kubun='IDX')の改名を区分名へ伝播するために渡す
				var cascadeCount = new MasterCascadeDb(_db).CascadeFromMaster(update.ItemType, db.Id, codeName.Code, codeName.Name, vdate,
					(item as MasterMeisho)?.Kubun, (org as IBaseCodeName)?.Code);
				_logger.LogInformation("V*列伝播 {ItemType} Id={Id} 更新行数={Count}", update.ItemType.Name, db.Id, cascadeCount);
			}
			if (typeof(IDerivedOrigin).IsAssignableFrom(update.ItemType)) {
				new HandlerDerived(_db).Update(update.ItemType, item, ((BaseDbClass)item).Id);
			}
			else if (typeof(ITranSoko).IsAssignableFrom(update.ItemType)) {
				var summaryDb = new SummaryDb(_db);
				var id = ((BaseDbClass)item).Id;
				summaryDb.CalcTran2SummaryStock(update.ItemType.Name, "Id_Soko", id, false);
				if (typeof(ITranIdo).IsAssignableFrom(update.ItemType)) {
					summaryDb.CalcTran2SummaryStock(update.ItemType.Name, "Id_Ido", id, false);
				}
			}
			_db.CompleteTransaction();
			return CreateSuccessResponse(flag, item.GetType(), Common.SerializeObject(item));
		}
		catch (Exception ex) when (ex is not NotImplementedException) {
			_db.AbortTransaction();
			return CreateExceptionResponse(flag, ex, update.ItemType, Common.SerializeObject(item));
		}
	}
	/// <summary>
	/// 削除処理 ( ToDo: 付随する処理も同時に実行する)
	/// </summary>
	/// <param name="flag"></param>
	/// <param name="delete"></param>
	/// <returns></returns>
	/// <exception cref="NotImplementedException"></exception>
	private CvMsg HandleDelete(CvFlag flag, DeleteParam delete) {
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
		_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
		if (typeof(ITranSoko).IsAssignableFrom(delete.ItemType)) {
			var summaryDb = new SummaryDb(_db);
			var id = ((BaseDbClass)item).Id;
			summaryDb.CalcTran2SummaryStock(delete.ItemType.Name, "Id_Soko", id, true);
			if (typeof(ITranIdo).IsAssignableFrom(delete.ItemType)) {
				summaryDb.CalcTran2SummaryStock(delete.ItemType.Name, "Id_Ido", id, true);
			}
		}
		_db.Delete(item);
		if (typeof(IDerivedOrigin).IsAssignableFrom(delete.ItemType)) {
			new HandlerDerived(_db).Delete(delete.ItemType, item, ((BaseDbClass)item).Id);
		}
		_db.CompleteTransaction();
		return CreateSuccessResponse(flag, delete.ItemType, Common.SerializeObject(item));
	}

	private CvMsg HandleDeleteById(CvFlag flag, DeleteByIdParam deleteById) {
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
		_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
		if (typeof(ITranSoko).IsAssignableFrom(deleteById.ItemType)) {
			var summaryDb = new SummaryDb(_db);
			var id = ((BaseDbClass)item).Id;
			summaryDb.CalcTran2SummaryStock(deleteById.ItemType.Name, "Id_Soko", id, true);
			if (typeof(ITranIdo).IsAssignableFrom(deleteById.ItemType)) {
				summaryDb.CalcTran2SummaryStock(deleteById.ItemType.Name, "Id_Ido", id, true);
			}
		}

		_db.Delete(item);
		if (typeof(IDerivedOrigin).IsAssignableFrom(deleteById.ItemType)) {
			new HandlerDerived(_db).Delete(deleteById.ItemType, item, ((BaseDbClass)item).Id);
		}
		_db.CompleteTransaction();
		return CreateSuccessResponse(flag, item.GetType(), Common.SerializeObject(item));
	}
	/// <summary>
	/// ToDo: 出力系の処理を集約して、マスタ以外も対応できるようにする
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	/// <exception cref="NotImplementedException"></exception>
	CvMsg HandleOutData(CvMsg request, CallContext context = default) {
		ArgumentNullException.ThrowIfNull(request);

		var param = Common.DeserializeObject(request.DataMsg ?? string.Empty, request.DataType);
		try {
			if (param is OutDataHhtMasterParam outDataParam) {
				_logger.LogInformation("パラメータ HhtMaster isFix={IsFix} OutMasterMei={OutMasterMei} 内容={Payload}", outDataParam.IsFixedLengthFormat, outDataParam.ReservedInt, Common.SerializeObject(outDataParam));

				var list = new HhtProcess(_db).CreateMaster(outDataParam.IsFixedLengthFormat, outDataParam.ReservedInt);
				return CreateSuccessResponse(request.Flag, typeof(List<string>), Common.SerializeObject(list));
			}
			throw new NotImplementedException();
		}
		catch (Exception ex) {
			return CreateExceptionResponse(request.Flag, ex, typeof(List<string>), request.DataMsg);
		}
	}


	private static Dictionary<string, string> GetEnvironmentVariables() {
		var envVars = Environment.GetEnvironmentVariables();

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
		// SysLoginのLastDateは、ログイン時に更新する
		if (typeof(SysLogin).IsAssignableFrom(itemType)) {
			var login = (SysLogin)item;
			login.LastDate = "";
		}
		var vdate = Common.GetVdate();
		db.Vdc = vdate;
		db.Vdu = vdate;
	}

	private object FetchExistingBaseDbItem(Type itemType, object id) {
		return _db.Fetch(itemType, "where Id=@0", id)?.First() ?? new();
	}

	private static CvMsg CreateSuccessResponse(CvFlag flag, Type? dataType, string? dataMsg) {
		return new CvMsg {
			Flag = flag,
			Code = 0,
			DataType = dataType ?? typeof(string),
			DataMsg = dataMsg ?? string.Empty,
		};
	}

	private static CvMsg CreateNotFoundResponse(CvFlag flag, Type? dataType = null, string? dataMsg = null) {
		return new CvMsg {
			Flag = flag,
			Code = NotFoundCode,
			DataType = dataType ?? typeof(string),
			DataMsg = dataMsg ?? string.Empty,
		};
	}

	private static CvMsg CreateExceptionResponse(CvFlag flag, Exception ex, Type? dataType, string? dataMsg) {
		return CreateErrorResponse(flag, UnexpectedErrorCode, ex.Message, dataType, dataMsg);
	}

	private static CvMsg CreateErrorResponse(CvFlag flag, int code, string? option, Type? dataType, string? dataMsg) {
		return new CvMsg {
			Flag = flag,
			Code = code,
			Option = option ?? string.Empty,
			DataType = dataType ?? typeof(string),
			DataMsg = dataMsg ?? string.Empty,
		};
	}
}
