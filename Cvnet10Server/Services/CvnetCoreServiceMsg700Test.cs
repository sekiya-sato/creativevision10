using CodeShare;
using Cvnet10Asset;
using Cvnet10Base;
using ProtoBuf.Grpc;
using System.ComponentModel;

namespace Cvnet10Server.Services;


public partial class CvnetCoreService {

	CvnetMsg subLogicMsg700(CvnetMsg request, CallContext context = default) {
		// 未実装
		throw new NotImplementedException();
	}
	CvnetMsg subLogicMsg701(CvnetMsg request, CallContext context = default) {
		var sql = Test202601Master.ViewSql;
		var list = _db.Fetch<Test202601Master>(sql);
		/*
			const string selectSql = """
select T.*, m1.Name as Mei_Brand, m2.Name as  Mei_Item, m3.Name as  Mei_Tenji
from Test202601Master T
left join MasterMeisho m1 on T.Id_MeiBrand = m1.Id
left join MasterMeisho m2 on T.Id_MeiItem = m2.Id
left join MasterMeisho m3 on T.Id_MeiTenji = m3.Id
order by T.Id
""";
			var retdb = _db.Fetch<Test202601Master>(selectSql);
			foreach(var item in retdb) {
				if(item.Jcolsiz != null) {
					item.Jcolsiz?[0]?.Jan3 = $"{item.Id} : 作成 {DateTime.Now.ToLongTimeString()}";
					foreach (var sub in item.Jcolsiz!) {
						var meiColData = _db.Fetch<MasterMeisho>("where Id = @Id", new { Id = sub.Id_MeiCol }).FirstOrDefault();
						var meiSizData = _db.Fetch<MasterMeisho>("where Id = @Id", new { Id = sub.Id_MeiSiz }).FirstOrDefault();
						if(meiColData != null) {
							sub.Mei_Col = meiColData.Name;
						}
						if (meiSizData != null) {
							sub.Mei_Siz = meiSizData.Name;
						}
					}
					_db.Update(item);
				}
			}
			var result0 = _db.Fetch<Test202601MasterJan>("""
				SELECT 
				    m.*,
				    -- JSON内の各フィールドを展開
				    json_extract(json_each.value, '$.Id_MeiCol') AS Id_MeiCol,
				    json_extract(json_each.value, '$.Id_MeiSiz') AS Id_MeiSiz,
				    json_extract(json_each.value, '$.Mei_Col')   AS Mei_Col,
				    json_extract(json_each.value, '$.Mei_Siz')   AS Mei_Siz,
				    json_extract(json_each.value, '$.Jan1')      AS Jan1,
				    json_extract(json_each.value, '$.Jan2')      AS Jan2,
				    json_extract(json_each.value, '$.Jan3')      AS Jan3
				FROM 
				    Test202601Master m,
				    json_each(m.Jcolsiz);
				""");

			var result1 = _db.Fetch<Test202601Master>("""
				SELECT 
				    m.*,json_each.value AS Disp0
				FROM 
				    Test202601Master m,
				    json_each(m.Jcolsiz);
				""");


			var wrk = result0.Count();
		 */

		// ✅ jcolsiz 内の名称を自動的にセット
		list.LoadAllJcolsizMeishoNames(_db, true);
		list.LoadAllGeneralMeishoNames(_db, true);
		var ret = new CvnetMsg() { Flag = request.Flag};
		ret.Code = 0;
		ret.DataType = typeof(List<Test202601Master>);
		ret.DataMsg = Common.SerializeObject(list);
		return ret;
	}
	CvnetMsg subLogicMsg702(CvnetMsg request, CallContext context = default) {
		// 未実装
		throw new NotImplementedException();
	}

}
