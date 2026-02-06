using CodeShare;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Base.Share;
using NPoco;
using ProtoBuf.Grpc;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Cvnet10Server.Services;


public partial class CvnetCoreService {


	/// <summary>
	/// Query系の処理
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	/// <exception cref="NotImplementedException"></exception>
	CvnetMsg subLogicMsg_Op_Query(CvnetMsg request, CallContext context = default) {
		var param = Common.DeserializeObject(request.DataMsg ?? "", request.DataType);
		// パラメータの型で処理を分岐
		var queryOne = param as QueryOneParam;
		var ret = new CvnetMsg() { Flag = request.Flag };
		var sql = "";

		if (queryOne != null) {
			// ViewSqlが定義されている場合はそちらを使用する
			var sqlmain = Resolver.GetViewSql(queryOne.ItemType);
			if (sqlmain != null)
				sql = $"{sqlmain} {queryOne.AddWhere()}";
			else
				sql = queryOne.AddWhere();
			var data = _db.Fetch(queryOne.ItemType, sql, queryOne.Parameters).FirstOrDefault();
			if(data == null) {
				ret.Code = -1; // 見つからなかった
				return ret;
			}
			ret.Code = 0;
			ret.DataType = data.GetType();
			ret.DataMsg = Common.SerializeObject(data);
			return ret;
		}
		var querybyId = param as QuerybyIdParam;
		if (querybyId != null) {
			// ViewSqlが定義されている場合はそちらを使用する
			var sqlmain = Resolver.GetViewSql(querybyId.ItemType);
			if (sqlmain != null)
				sql = $"{sqlmain} where Id = @0";
			else
				sql = "where Id = @0";
			var data = _db.Fetch(querybyId.ItemType, sql, querybyId.Id).FirstOrDefault();
			if (data == null) {
				ret.Code = -1; // 見つからなかった
				return ret;
			}
			ret.Code = 0;
			ret.DataType = data.GetType();
			ret.DataMsg = Common.SerializeObject(data);
			return ret;
		}
		var queryList = param as QueryListParam;
		if (queryList != null) {
			// ViewSqlが定義されている場合はそちらを使用する
			var sqlmain = Resolver.GetViewSql(queryList.ItemType);
			if (sqlmain != null)
				sql = $"{MasterMeisho.ViewSql} {queryList.AddWhereOrder()}";
			else
				sql = queryList.AddWhereOrder();
			var list = _db.Fetch(queryList.ItemType, sql, queryList.Parameters);
			if (list == null || list.Count==0) {
				ret.Code = -1; // 見つからなかった
				ret.DataType = typeof(List<>).MakeGenericType(queryList.ItemType);
				ret.DataMsg = "[]";
				return ret;
			}
			if (queryList.ItemType.Name == "Test202601Master") { // 特定のテーブル型かどうかで判定
				var list0 = list.Cast<Test202601Master>();
				list0.LoadAllJcolsizMeishoNames(_db, true); // 名称をセット
				list0.LoadAllGeneralMeishoNames(_db, true);
			}
			ret.Code = 0;
			// ret.DataType = list.GetType(); // これはList<object>になるので正しくない
			ret.DataType = typeof(List<>).MakeGenericType(queryList.ItemType);
			ret.DataMsg = Common.SerializeObject(list);
			return ret;
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
	CvnetMsg subLogicMsg_Op_Execute(CvnetMsg request, CallContext context = default) {
		var param = Common.DeserializeObject(request.DataMsg ?? "", request.DataType);
		// パラメータの型で処理を分岐
		var insert = param as InsertParam;
		if (insert != null) {
			var item = insert.GetItemObject();
			var id = _db.Insert(item);
			var ret = new CvnetMsg() { Flag = request.Flag };
			ret.Code = 0;
			ret.DataType = insert.ItemType;
			ret.DataMsg = Common.SerializeObject(item);
			return ret;
		}
		var update = param as UpdateParam;
		if (update != null) {
			var item = update.GetItemObject();
			_db.Update(item);
			var ret = new CvnetMsg() { Flag = request.Flag };
			ret.Code = 0;
			ret.DataType = update.ItemType;
			ret.DataMsg = Common.SerializeObject(item);
			return ret;
		}
		var delete = param as DeleteParam;
		if (delete != null) {
			var item = delete.GetItemObject();
			_db.Delete(item);
			var ret = new CvnetMsg() { Flag = request.Flag };
			ret.Code = 0;
			ret.DataType = delete.ItemType;
			ret.DataMsg = Common.SerializeObject(item);
			return ret;
		}
		var deleteById = param as DeleteByIdParam;
		if (deleteById != null) {
			var ret_del =  _db.Delete(deleteById.ItemType.Name, "Id", deleteById.Id);
			var ret = new CvnetMsg() { Flag = request.Flag };
			ret.Code = 0;
			ret.DataType = typeof(long);
			ret.DataMsg = Common.SerializeObject(deleteById.Id);
			return ret;
		}
		// 未実装
		throw new NotImplementedException();
	}
}
