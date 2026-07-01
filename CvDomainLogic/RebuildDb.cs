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
		// MasterShohinのJcolsiz、JSON_EXTRACTからMeishoを再構築する
		int cnt = 0; // MasterMeishoへの登録件数
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
		var updateSql = "";
		// まずはカラーIdを持たないものを抽出して、MasterMeishoに登録する
		var meishoSql = @$"
select distinct 'COL' as Kubun,'ｶﾗｰ' as KubunName,Code_Col as Code, coalesce(nullif(Mei_Col, ''),Name,'新色'||Code_Col) as Name from (
{coreSql} where json_extract(J.value, '$.Id_Col')=0)
";
		var meishoList = _db.Fetch<MasterMeisho>(meishoSql);
		_db.BeginTransaction();
		// カラーId=0のものがあれば、MasterMeishoに登録する
		if (meishoList.Count > 0) {
			cnt += meishoList.Count;
			// 名称マスタ作成
			_db.InsertBulk<MasterMeisho>(meishoList);
			// MasterShohinのJcolsizを更新する
			updateSql = @"
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
			var retData = _db.RawExecCmd(updateSql);
		}

		// 次にサイズIdを持たないものを抽出して、MasterMeishoに登録する
		meishoSql = @$"
select distinct Kubun,KubunName,Code_Siz as Code, coalesce(nullif(Mei_Siz, ''),'新サイズ'||Code_Siz) as Name from (
{coreSql} where json_extract(J.value, '$.Id_Siz')=0)
";
		meishoList = _db.Fetch<MasterMeisho>(meishoSql);
		// サイズId=0のものがあれば、MasterMeishoに登録する
		if (meishoList.Count > 0) {
			cnt += meishoList.Count;
			// 名称マスタ作成
			_db.InsertBulk<MasterMeisho>(meishoList);
			// MasterShohinのJcolsizを更新する
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
WHERE EXISTS (-
    SELECT 1
    FROM json_each(S.Jcolsiz) AS J
    JOIN MasterMeisho AS M
      ON M.Kubun = S.SizeKu
     AND M.Code = json_extract(J.value, '$.Code_Siz')
    WHERE json_extract(J.value, '$.Id_Siz') = 0
);";
			// MasterShohinのJcolsizを更新する(Id_Siz)
			var retData = _db.RawExecCmd(updateSql);
		}
		_db.CompleteTransaction();
		// Derived を更新する
		return cnt;
	}
	/// <summary>
	/// Tran系のテーブルでId_ColとId_Sizを再構築する
	/// </summary>
	/// <returns></returns>
	public int RebuildTranAll() {
		//RebuildMasterShohin2Meisho()にて登録されたMasterMeishoを使い、Tran系のテーブルでId_ColとId_Sizを再構築する
		int cnt = 0;

		_db.BeginTransaction();
		try {
			// まずは Tran00Uriage のJmeisaiを更新する(Id_Col)
			cnt += ExecuteUpdateAndGetChanges(@"
UPDATE Tran00Uriage AS T
SET Jmeisai = (
    SELECT json_group_array(json(X.value2))
    FROM (
        SELECT
            J.key,
            CASE
                WHEN CAST(ifnull(json_extract(J.value, '$.Id_Col'), 0) AS INTEGER) = 0
                     AND M.Id IS NOT NULL
                THEN json_set(J.value, '$.Id_Col', M.Id)
                ELSE J.value
            END AS value2
        FROM json_each(T.Jmeisai) AS J
        LEFT JOIN MasterMeisho AS M
          ON M.Kubun = 'COL'
         AND M.Code = COALESCE(
             NULLIF(json_extract(J.value, '$.Code_Col'), ''),
             NULLIF(json_extract(J.value, '$.Cd_Col'), '')
         )
        ORDER BY CAST(J.key AS INTEGER)
    ) AS X
)
WHERE EXISTS (
    SELECT 1
    FROM json_each(T.Jmeisai) AS J
    JOIN MasterMeisho AS M
      ON M.Kubun = 'COL'
     AND M.Code = COALESCE(
         NULLIF(json_extract(J.value, '$.Code_Col'), ''),
         NULLIF(json_extract(J.value, '$.Cd_Col'), '')
     )
    WHERE CAST(ifnull(json_extract(J.value, '$.Id_Col'), 0) AS INTEGER) = 0
);
");

			// Tran00Uriage のJmeisaiを更新する(Id_Siz)
			cnt += ExecuteUpdateAndGetChanges(@"
UPDATE Tran00Uriage AS T
SET Jmeisai = (
    SELECT json_group_array(json(X.value2))
    FROM (
        SELECT
            J.key,
            CASE
                WHEN CAST(ifnull(json_extract(J.value, '$.Id_Siz'), 0) AS INTEGER) = 0
                     AND M.Id IS NOT NULL
                THEN json_set(J.value, '$.Id_Siz', M.Id)
                ELSE J.value
            END AS value2
        FROM json_each(T.Jmeisai) AS J
        LEFT JOIN MasterShohin AS S
          ON S.Id = CAST(ifnull(json_extract(J.value, '$.Id_Shohin'), 0) AS INTEGER)
          OR (
              CAST(ifnull(json_extract(J.value, '$.Id_Shohin'), 0) AS INTEGER) = 0
              AND S.Code = COALESCE(
                  NULLIF(json_extract(J.value, '$.Code_Shohin'), ''),
                  NULLIF(json_extract(J.value, '$.Cd_Shohin'), '')
              )
          )
        LEFT JOIN MasterMeisho AS M
          ON M.Kubun = S.SizeKu
         AND M.Code = COALESCE(
             NULLIF(json_extract(J.value, '$.Code_Siz'), ''),
             NULLIF(json_extract(J.value, '$.Cd_Siz'), '')
         )
        ORDER BY CAST(J.key AS INTEGER)
    ) AS X
)
WHERE EXISTS (
    SELECT 1
    FROM json_each(T.Jmeisai) AS J
    JOIN MasterShohin AS S
      ON S.Id = CAST(ifnull(json_extract(J.value, '$.Id_Shohin'), 0) AS INTEGER)
      OR (
          CAST(ifnull(json_extract(J.value, '$.Id_Shohin'), 0) AS INTEGER) = 0
          AND S.Code = COALESCE(
              NULLIF(json_extract(J.value, '$.Code_Shohin'), ''),
              NULLIF(json_extract(J.value, '$.Cd_Shohin'), '')
          )
      )
    JOIN MasterMeisho AS M
      ON M.Kubun = S.SizeKu
     AND M.Code = COALESCE(
         NULLIF(json_extract(J.value, '$.Code_Siz'), ''),
         NULLIF(json_extract(J.value, '$.Cd_Siz'), '')
     )
    WHERE CAST(ifnull(json_extract(J.value, '$.Id_Siz'), 0) AS INTEGER) = 0
);
");
			_db.CompleteTransaction();
		}
		catch {
			_db.AbortTransaction();
			throw;
		}

		return cnt;
	}

	int ExecuteUpdateAndGetChanges(string sql) {
		_db.RawExecCmd(sql);
		if (!string.IsNullOrEmpty(_db.RawLastError)) {
			throw new InvalidOperationException(_db.RawLastError);
		}

		return _db.FirstOrDefault<int>("SELECT changes() AS updated_count");
	}
}
