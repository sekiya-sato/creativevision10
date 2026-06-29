using CvAsset;
using CvBase;
using Microsoft.Extensions.Logging;

namespace CvDomainLogic;

public class RebuildDb {
	ExDatabase _db;
	ILogger<RebuildDb> _logger;

	public RebuildDb(ExDatabase db) {
		_db = db;
		_logger = new NLogExtender<RebuildDb>();
	}
	/// <summary>
	/// MasterShohinのJcolsizからMeishoを再構築する	
	/// </summary>
	public void RebuildMasterShohin2Meisho() {
		// ToDo: MasterShohinのJcolsiz、JSON_EXTRACTからMeishoを再構築する
		var sql = @"
select distinct 'COL' as Kubun,'ｶﾗｰ' as KubunName,Code_Col, Mei_Col,Name from (
SELECT
  M.Code,
  mm.Kubun,
  mm.KubunName,
  mm.Name,
  json_extract(J.value, '$.Id_Col') AS Id_Col,
  json_extract(J.value, '$.Code_Col') AS Code_Col,
  json_extract(J.value, '$.Mei_Col') AS Mei_Col,
  json_extract(J.value, '$.Id_Siz') AS Id_Siz,
  json_extract(J.value, '$.Code_Siz') AS Code_Siz,
  json_extract(J.value, '$.Mei_Siz') AS Mei_Siz,
  json_extract(J.value, '$.Jan1') AS Jan1,
  json_extract(J.value, '$.Jan2') AS Jan2,
  json_extract(J.value, '$.Jan3') AS Jan3
FROM MasterShohin M, json_each(M.Jcolsiz) J
left outer join MasterMeisho mm on mm.Kubun='COL' and mm.Code=json_extract(J.value, '$.Code_Col')
where json_extract(J.value, '$.Id_Col')=0
)
";
		var meishoList = _db.Fetch<MasterMeisho>(sql);
		foreach (var meisho in meishoList) {
			meisho.Vdc = Common.GetVdate();
			meisho.Vdu = Common.GetVdate();
			_db.Insert(meisho);
		}



	}

}
