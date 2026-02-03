using CodeShare;
using Cvnet10AppShared;
using Cvnet10Base;
using ProtoBuf.Grpc;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Cvnet10Server.Services;


public partial class CvnetCoreService {

	/// <summary>
	/// ロジック検証用の一時的なテスト処理
	/// Todo : 実際の業務ロジックに置き換えることあるいは削除すること
	/// Note : 特定のテーブル型に依存した名称セット処理を行っている
	/// </summary>
	/// <param name="item"></param>
	/// <param name="itemType"></param>
	void setName(object? item, Type itemType) {
		if (item == null)
			return;
		if (itemType.Name == "Test202601Master") { // 特定のテーブル型かどうかで判定
			var data0 = (Test202601Master?)item;
			if (data0 != null) {
				data0.LoadJcolsizMeishoNames(_db, true); // 名称をセット
				data0.LoadGeneralMeishoNames(_db, true);
				return;
			}
		}
		// throw new InvalidOperationException("Invalid item type for setName");
	}

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
		if (queryOne != null) {
			var data = _db.Fetch(queryOne.ItemType,  queryOne.AddWhere(), queryOne.Parameters).FirstOrDefault();
			if(data == null) {
				ret.Code = -1; // 見つからなかった
				return ret;
			}
			setName(data, queryOne.ItemType);
			ret.Code = 0;
			ret.DataType = data.GetType();
			ret.DataMsg = Common.SerializeObject(data);
			return ret;
		}
		var querybyId = param as QuerybyIdParam;
		if (querybyId != null) {
			var data = _db.Fetch(querybyId.ItemType, "where Id = @0", querybyId.Id).FirstOrDefault();
			if (data == null) {
				ret.Code = -1; // 見つからなかった
				return ret;
			}
			setName(data, querybyId.ItemType);
			ret.Code = 0;
			ret.DataType = data.GetType();
			ret.DataMsg = Common.SerializeObject(data);
			return ret;
		}
		var queryList = param as QueryListParam;
		if (queryList != null) {
			var list = _db.Fetch(queryList.ItemType, queryList.AddWhereOrder(), queryList.Parameters);
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
