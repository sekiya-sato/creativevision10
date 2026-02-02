using CodeShare;
using Cvnet10AppShared;
using Cvnet10Base;
using ProtoBuf.Grpc;
using System.Collections.Generic;
using System.ComponentModel;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Cvnet10Server.Services;


public partial class CvnetCoreService {

	/// <summary>
	/// 単レコードの取得
	/// </summary>
	/// <param name="request">DataType: Table, Code: 取得対象のId</param>
	/// <param name="context"></param>
	/// <returns>DataType: Table, DataMsg: 対象レコードのJSON</returns>
	CvnetMsg subLogicMsg101(CvnetMsg request, CallContext context = default) {
		var data = _db.Fetch(request.DataType, "where Id = @0", request.Code);
		var ret = new CvnetMsg() { Flag = request.Flag };
		ret.Code = 0;
		ret.DataType = request.DataType;
		var firstItem = data.FirstOrDefault();
		// 特定のテーブル型かどうかで判定
		if (firstItem?.GetType().Name == "Test202601Master") {
			var item = (Test202601Master)firstItem;
			item.LoadJcolsizMeishoNames(_db, true);
			item.LoadGeneralMeishoNames(_db, true);
		}
		ret.DataMsg = Common.SerializeObject(data.FirstOrDefault() ?? "");
		return ret;
	}
	/// <summary>
	/// レコードの更新
	/// </summary>
	/// <param name="request">DataMsg: ParamExecuteのJSONデータ(TableType: Table, JsonData: 対象レコードのJSON, ExecuteType: 処理タイプ)</param>
	/// <param name="context"></param>
	/// <returns>DataType: Table, DataMsg: 対象レコードのJSON</returns>
	CvnetMsg subLogicMsg102(CvnetMsg request, CallContext context = default) {
		var exec = Common.DeserializeObject< ParamExecute >(request.DataMsg ?? "") ?? new ParamExecute();
		var item = Common.DeserializeObject(exec.JsonData ?? "", exec.TableType);
		if (exec.ExecuteType == ParamExecuteType.Update) {
			if(item !=null)
				_db.Update(item);
		}
		else if (exec.ExecuteType == ParamExecuteType.Insert) {
			if(item !=null)
				_db.Insert(item);
		}
		else if (exec.ExecuteType == ParamExecuteType.Delete) {
			if(item !=null)
				_db.Delete(item);
		}
		var ret = new CvnetMsg() { Flag = request.Flag };
		ret.Code = 0;
		ret.DataType = request.DataType;
		ret.DataMsg = Common.SerializeObject(item ?? "");
		return ret;
	}
	/// <summary>
	/// 複数レコードの取得
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	CvnetMsg subLogicMsg103(CvnetMsg request, CallContext context = default) {
		var param = Common.DeserializeObject< ParamQuery >(request.DataMsg ?? "") ?? new ParamQuery();
		var list = _db.Fetch(param.TableType, 
			!string.IsNullOrWhiteSpace(param.Where)? $" where {param.Where}" :""
			+ (!string.IsNullOrWhiteSpace(param.Order)? $" order by {param.Order}" : ""),
			param.Parameters);
		var ret = new CvnetMsg() { Flag = request.Flag };
		ret.Code = 0;
		ret.DataType = param.ResultType;
		// 特定のテーブル型かどうかで判定
		var firstItem = list.FirstOrDefault();
		if (firstItem?.GetType().Name == "Test202601Master") {
			var masterList = list.Cast<Test202601Master>();
			masterList.LoadAllJcolsizMeishoNames(_db, true);
			masterList.LoadAllGeneralMeishoNames(_db, true);
			ret.DataMsg = Common.SerializeObject(masterList);
		}
		else {
			ret.DataMsg = Common.SerializeObject(list);
		}
		return ret;
	}

}
