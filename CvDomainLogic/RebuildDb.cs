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
	public int RebuildMasterShohin2Meisho() {
		// ToDo: MasterShohinのJcolsiz、JSON_EXTRACTからMeishoを再構築する
		var coreSql = @"
SELECT
  M.Code,
  M.SizeKu as Kubun,
  (select Name from MasterMeisho where Kubun='IDX' and Code = M.SizeKu) as KubunName,
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
";
		// まずはカラーIdを持たないものを抽出して、MasterMeishoに登録する
		var meishoSql = @$"
select distinct 'COL' as Kubun,'ｶﾗｰ' as KubunName,Code_Col as Code, coalesce(nullif(Mei_Col, ''),Name,'新色'||Code_Col) as Name from (
{coreSql} where json_extract(J.value, '$.Id_Col')=0)
";
		var meishoList = _db.Fetch<MasterMeisho>(meishoSql);
		_db.BeginTransaction();
		// 名称マスタ作成
		_db.InsertBulk<MasterMeisho>(meishoList);
		var updateSql = @"
UPDATE MasterShohin AS S
SET Jcolsiz = (
    SELECT json_group_array(json(X.value2))
    FROM (
        SELECT
            J.key,
            CASE
                WHEN json_extract(J.value, '$.Id_Col') = 0
                     AND M.Id IS NOT NULL
                THEN json_set(J.value, '$.Id_Col', M.Id)
                ELSE J.value
            END AS value2
        FROM json_each(S.Jcolsiz) AS J
        LEFT JOIN MasterMeisho AS M
          ON M.Kubun = 'COL'
         AND M.Code = json_extract(J.value, '$.Code_Col')
        ORDER BY CAST(J.key AS INTEGER)
    ) AS X
)
WHERE EXISTS (
    SELECT 1
    FROM json_each(S.Jcolsiz) AS J
    JOIN MasterMeisho AS M
      ON M.Kubun = 'COL'
     AND M.Code = json_extract(J.value, '$.Code_Col')
    WHERE json_extract(J.value, '$.Id_Col') = 0
);
";
		// MasterShohinのJcolsizを更新する(Id_Col)
		var retCnt = _db.RawExecCmd(updateSql);
		// 次にサイズIdを持たないものを抽出して、MasterMeishoに登録する
		meishoSql = @$"
select distinct Kubun,KubunName,Code_Siz as Code, coalesce(nullif(Mei_Siz, ''),'新サイズ'||Code_Siz) as Name from (
{coreSql} where json_extract(J.value, '$.Id_Siz')=0)
";
		meishoList = _db.Fetch<MasterMeisho>(meishoSql);
		// 名称マスタ作成
		_db.InsertBulk<MasterMeisho>(meishoList);
		updateSql = @"
UPDATE MasterShohin AS S
SET Jcolsiz = (
    SELECT json_group_array(json(X.value2))
    FROM (
        SELECT
            J.key,
            CASE
                WHEN json_extract(J.value, '$.Id_Siz') = 0
                     AND M.Id IS NOT NULL
                THEN json_set(J.value, '$.Id_Siz', M.Id)
                ELSE J.value
            END AS value2
        FROM json_each(S.Jcolsiz) AS J
        LEFT JOIN MasterMeisho AS M
          ON M.Kubun = S.SizeKu
         AND M.Code = json_extract(J.value, '$.Code_Siz')
        ORDER BY CAST(J.key AS INTEGER)
    ) AS X
)
WHERE EXISTS (
    SELECT 1
    FROM json_each(S.Jcolsiz) AS J
    JOIN MasterMeisho AS M
      ON M.Kubun = S.SizeKu
     AND M.Code = json_extract(J.value, '$.Code_Siz')
    WHERE json_extract(J.value, '$.Id_Siz') = 0
);";
		// MasterShohinのJcolsizを更新する(Id_Siz)
		retCnt = _db.RawExecCmd(updateSql);
		_db.CompleteTransaction();
		return 0;
	}
}
