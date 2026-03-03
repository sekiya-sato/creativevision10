using CodeShare;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Base.Oracle;
using Cvnet10Base.Share;
using Cvnet10DomainLogic;
using ProtoBuf.Grpc;
using System.Collections;

namespace Cvnet10Server.Services;

public partial class CvnetCoreService {
	// CvnetCoreService のハンドラ実装（元の switch の各ケースを分離）

	private CvnetMsg HandleCopyReply(CvnetMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogDebug("HandleCopyReply invoked Flag:{Flag}", request.Flag);

		var result = new CvnetMsg { Flag = request.Flag };
		result.Code = 0;
		result.Flag = request.Flag;
		result.DataType = request.DataType;
		result.DataMsg = request.DataMsg;
		return result;
	}

	private CvnetMsg HandleGetVersion(CvnetMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogDebug("HandleGetVersion invoked Flag:{Flag}", request.Flag);

		var result = new CvnetMsg { Flag = request.Flag };
		result.Code = 0;
		result.Flag = request.Flag;
		result.DataType = typeof(VersionInfo);
		result.DataMsg = Common.SerializeObject(AppGlobal.Version);
		return result;
	}

	private CvnetMsg HandleGetEnv(CvnetMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogDebug("HandleGetEnv invoked Flag:{Flag}", request.Flag);

		var result = new CvnetMsg { Flag = request.Flag };
		result.Code = 0;
		result.Flag = request.Flag;

		// 環境変数を取得して Dictionary<string,string> に変換、JSON シリアライズして返す
		var envVars = Environment.GetEnvironmentVariables();
		var dict0 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (DictionaryEntry entry in envVars) {
			var key = entry.Key?.ToString() ?? string.Empty;
			var value = entry.Value?.ToString() ?? string.Empty;
			dict0[key] = value;
		}
		result.DataType = typeof(Dictionary<string, string>);
		result.DataMsg = Common.SerializeObject(dict0);
		return result;
	}

	private CvnetMsg HandleConvertDb(CvnetMsg request, CallContext context) {
		ArgumentNullException.ThrowIfNull(request);
		_logger.LogInformation("HandleConvertDb invoked Flag:{Flag}", request.Flag);

		var result = new CvnetMsg { Flag = request.Flag };
		result.Code = 0;
		var start = DateTime.Now;
		var dict0 = new Dictionary<string, string>();

		var oracleConnectionString = _configuration.GetConnectionString("oracle");
		if (string.IsNullOrWhiteSpace(oracleConnectionString))
			throw new InvalidOperationException("Connection string 'oracle' is missing. Configure it in appsettings.json under ConnectionStrings.");
		var fromDb = ExDatabaseOracle.GetDbConn(oracleConnectionString);
		var cnvDb = new ConvertDb(fromDb, _db);
		try {
			var initFlg = request.Flag == CvnetFlag.MSg041_ConvertDbInit;
			cnvDb.ConvertAll(initFlg);
			dict0["Status"] = "Success";
			var timespan = DateTime.Now - start;
			dict0["Timesec"] = timespan.TotalSeconds.ToString();
		}
		catch (Exception ex) {
			_logger.LogError(ex, "HandleConvertDb error");
			dict0["Status"] = "Error";
			dict0["Message"] = ex.Message;
		}
		result.DataType = typeof(Dictionary<string, string>);
		result.DataMsg = Common.SerializeObject(dict0);
		return result;
	}

	/// <summary>
	/// Query系の処理
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	/// <exception cref="NotImplementedException"></exception>
	CvnetMsg HandleOpQuery(CvnetMsg request, CallContext context = default) {
		var param = Common.DeserializeObject(request.DataMsg ?? "", request.DataType);


		// パラメータの型で処理を分岐
		var queryOne = param as QueryOneParam;
		var ret = new CvnetMsg() { Flag = request.Flag };
		var sql = "";
		if (queryOne != null) {
			_logger.LogInformation($"パラメータ QueryOneParam.ItemType={queryOne.ItemType}  内容{Common.SerializeObject(queryOne)}");
			sql = queryOne.AddWhere();
			try {
				var data = _db.Fetch(queryOne.ItemType, sql, queryOne.Parameters).FirstOrDefault();
				if (data == null) {
					ret.Code = -1; // 見つからなかった
					return ret;
				}
				ret.Code = 0;
				ret.DataType = data.GetType();
				ret.DataMsg = Common.SerializeObject(data);
				return ret;
			}
			catch (Exception ex) {
				var error = new CvnetMsg() { Flag = request.Flag };
				error.Code = -9902;
				error.Option = ex.Message;
				error.DataType = typeof(string);
				error.DataMsg = ex.Message;
				return error;
			}
		}
		var querybyId = param as QuerybyIdParam;
		if (querybyId != null) {
			_logger.LogInformation($"パラメータ QuerybyIdParam.ItemType={querybyId.ItemType} 内容{Common.SerializeObject(querybyId)}");
			try {
				var data = _db.Fetch(querybyId.ItemType, "where Id = @0", querybyId.Id).FirstOrDefault();
				if (data == null) {
					ret.Code = -1; // 見つからなかった
					return ret;
				}
				ret.Code = 0;
				ret.DataType = data.GetType();
				ret.DataMsg = Common.SerializeObject(data);
				return ret;
			}
			catch (Exception ex) {
				var error = new CvnetMsg() { Flag = request.Flag };
				error.Code = -9902;
				error.Option = ex.Message;
				error.DataType = typeof(string);
				error.DataMsg = ex.Message;
				return error;
			}
		}
		var queryList = param as QueryListParam;
		if (queryList != null) {
			_logger.LogInformation($"パラメータ QueryListParam.ItemType={queryList.ItemType} 内容{Common.SerializeObject(queryList)}");
			sql = queryList.AddWhereOrder();
			try {
				var list = _db.Fetch(queryList.ItemType, sql, queryList.Parameters);
				if (list == null || list.Count == 0) {
					ret.Code = -1; // 見つからなかった
					ret.DataType = typeof(List<>).MakeGenericType(queryList.ItemType);
					ret.DataMsg = "[]";
					return ret;
				}
				ret.Code = 0;
				// ret.DataType = list.GetType(); // これはList<object>になるので正しくない
				ret.DataType = typeof(List<>).MakeGenericType(queryList.ItemType);
				ret.DataMsg = Common.SerializeObject(list);
				return ret;
			}
			catch (Exception ex) {
				var error = new CvnetMsg() { Flag = request.Flag };
				error.Code = -9902;
				error.Option = ex.Message;
				error.DataType = typeof(string);
				error.DataMsg = ex.Message;
				return error;
			}
		}
		var querySql = param as QueryListSqlParam;
		if (querySql != null) {
			_logger.LogInformation($"パラメータ QueryListSqlParam.ItemType={querySql.ItemType} 内容{Common.SerializeObject(querySql)}");
			sql = querySql.Sql ?? "";
			try {
				var list = _db.Fetch(querySql.ItemType, sql, querySql.Parameters);
				if (list == null || list.Count == 0) {
					ret.Code = -1; // 見つからなかった
					ret.DataType = typeof(List<>).MakeGenericType(querySql.ItemType);
					ret.DataMsg = "[]";
					return ret;
				}
				ret.Code = 0;
				// ret.DataType = list.GetType(); // これはList<object>になるので正しくない
				ret.DataType = typeof(List<>).MakeGenericType(querySql.ItemType);
				ret.DataMsg = Common.SerializeObject(list);
				return ret;
			}
			catch (Exception ex) {
				var error = new CvnetMsg() { Flag = request.Flag };
				error.Code = -9902;
				error.Option = ex.Message;
				error.DataType = typeof(string);
				error.DataMsg = ex.Message;
				return error;
			}
		}

		// 未実装
		throw new NotImplementedException();
	}
	/// <summary>
	/// Execute系の処理
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	/// <exception cref="NotImplementedException"></exception>
	CvnetMsg HandleOpExecute(CvnetMsg request, CallContext context = default) {
		var param = Common.DeserializeObject(request.DataMsg ?? "", request.DataType);
		// パラメータの型で処理を分岐
		var insert = param as InsertParam;
		if (insert != null) {
			_logger.LogInformation($"パラメータ InsertParam.ItemType={insert.ItemType} 内容{Common.SerializeObject(insert)}");
			var item = insert.GetItemObject();
			if (typeof(BaseDbClass).IsAssignableFrom(insert.ItemType)) {
				var vdate = Common.GetVdate();
				if (item is BaseDbClass db) {
					db.Vdc = vdate;
					db.Vdu = vdate;
				}
			}
			try {
				var id = _db.Insert(item);
				var ret = new CvnetMsg() { Flag = request.Flag };
				ret.Code = 0;
				ret.DataType = item.GetType();
				ret.DataMsg = Common.SerializeObject(item);
				return ret;
			}
			catch (Exception ex) {
				var error = new CvnetMsg() { Flag = request.Flag };
				error.Code = -9902;
				error.Option = ex.Message;
				error.DataType = item.GetType();
				error.DataMsg = Common.SerializeObject(item);
				return error;
			}
		}
		var update = param as UpdateParam;
		if (update != null) {
			_logger.LogInformation($"パラメータ UpdateParam.ItemType={update.ItemType} 内容{Common.SerializeObject(update)}");
			var item = update.GetItemObject();
			if (typeof(BaseDbClass).IsAssignableFrom(update.ItemType)) {
				var vdate = Common.GetVdate();
				if (item is BaseDbClass db) {
					try {
						var orgItem = _db.Fetch(update.ItemType, "where Id=@0", db.Id)?.First() ?? new();
						if (orgItem is BaseDbClass org) {
							if (db.Vdu != org.Vdu) {
								var error = new CvnetMsg() { Flag = request.Flag };
								error.Code = -9901;
								error.Option = "他で更新されています";
								error.DataType = item.GetType();
								error.DataMsg = Common.SerializeObject(item);
								return error;
							}
							db.Vdu = vdate;
							_db.Update(item);
							var ret = new CvnetMsg() { Flag = request.Flag };
							ret.Code = 0;
							ret.DataType = item.GetType();
							ret.DataMsg = Common.SerializeObject(item);
							return ret;
						}
					}
					catch (Exception ex) {
						var error = new CvnetMsg() { Flag = request.Flag };
						error.Code = -9902;
						error.Option = ex.Message;
						error.DataType = update.ItemType;
						error.DataMsg = Common.SerializeObject(item);
						return error;
					}
				}
			}
		}
		var delete = param as DeleteParam;
		if (delete != null) {
			_logger.LogInformation($"パラメータ DeleteParam.ItemType={delete.ItemType} 内容{Common.SerializeObject(delete)}");
			var item = delete.GetItemObject();
			if (typeof(BaseDbClass).IsAssignableFrom(delete.ItemType)) {
				if (item is BaseDbClass db) {
					var orgItem = _db.Fetch(delete.ItemType, "where Id=@0", db.Id)?.First() ?? new();
					if (orgItem is BaseDbClass org) {
						if (db.Vdu != org.Vdu) {
							var error = new CvnetMsg() { Flag = request.Flag };
							error.Code = -9901;
							error.Option = "他で更新されています";
							error.DataType = item.GetType();
							error.DataMsg = Common.SerializeObject(item);
							return error;
						}
						_db.Delete(item);
						var ret = new CvnetMsg() { Flag = request.Flag };
						ret.Code = 0;
						ret.DataType = delete.ItemType;
						ret.DataMsg = Common.SerializeObject(item);
						return ret;
					}
				}
			}
		}
		var deleteById = param as DeleteByIdParam;
		if (deleteById != null) {
			_logger.LogInformation($"パラメータ DeleteByIdParam.ItemType={deleteById.ItemType} Id={deleteById.Id} 内容{Common.SerializeObject(deleteById)}");
			var item = _db.Fetch(deleteById.ItemType, "where Id=@0", deleteById.Id)?.First() ?? new();
			if (typeof(BaseDbClass).IsAssignableFrom(deleteById.ItemType)) {
				if (item is BaseDbClass db) {
					var orgItem = _db.Fetch(deleteById.ItemType, "where Id=@0", db.Id)?.First() ?? new();
					if (orgItem is BaseDbClass org) {
						if (deleteById.OriginalVdu != org.Vdu) {
							var error = new CvnetMsg() { Flag = request.Flag };
							error.Code = -9901;
							error.Option = "他で更新されています";
							error.DataType = item.GetType();
							error.DataMsg = Common.SerializeObject(item);
							return error;
						}
						_db.Delete(item);
						var ret = new CvnetMsg() { Flag = request.Flag };
						ret.Code = 0;
						ret.DataType = item.GetType();
						ret.DataMsg = Common.SerializeObject(item);
						return ret;
					}
				}
			}
		}
		// 未実装
		throw new NotImplementedException();
	}
}
