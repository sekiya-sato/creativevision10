--  SummaryRealStock が SummaryStockの年月を集約した在庫数になっているか確認する
WITH
ss AS (
    SELECT
        Id_Soko,
        Id_Shohin,
        Id_Col,
        Id_Siz,
        SUM(COALESCE(Su, 0)) AS SummaryStockSu
    FROM SummaryStock
    GROUP BY
        Id_Soko,
        Id_Shohin,
        Id_Col,
        Id_Siz
),
sr AS (
    SELECT
        Id_Soko,
        Id_Shohin,
        Id_Col,
        Id_Siz,
        SUM(COALESCE(Su, 0)) AS SummaryRealStockSu
    FROM SummaryRealStock
    GROUP BY
        Id_Soko,
        Id_Shohin,
        Id_Col,
        Id_Siz
)
SELECT
    CASE
        WHEN ss.Id_Soko IS NULL THEN 'SummaryRealStockのみ'
        ELSE '数量不一致'
    END AS CheckResult,
    sr.Id_Soko,
    sr.Id_Shohin,
    sr.Id_Col,
    sr.Id_Siz,
    sr.SummaryRealStockSu,
    COALESCE(ss.SummaryStockSu, 0) AS SummaryStockAggSu,
    sr.SummaryRealStockSu - COALESCE(ss.SummaryStockSu, 0) AS DiffSu
FROM sr
LEFT JOIN ss
  ON ss.Id_Soko   = sr.Id_Soko
 AND ss.Id_Shohin = sr.Id_Shohin
 AND ss.Id_Col    = sr.Id_Col
 AND ss.Id_Siz    = sr.Id_Siz
WHERE ss.Id_Soko IS NULL
   OR sr.SummaryRealStockSu <> ss.SummaryStockSu

UNION ALL

SELECT
    'SummaryStockのみ' AS CheckResult,
    ss.Id_Soko,
    ss.Id_Shohin,
    ss.Id_Col,
    ss.Id_Siz,
    0 AS SummaryRealStockSu,
    ss.SummaryStockSu AS SummaryStockAggSu,
    0 - ss.SummaryStockSu AS DiffSu
FROM ss
LEFT JOIN sr
  ON sr.Id_Soko   = ss.Id_Soko
 AND sr.Id_Shohin = ss.Id_Shohin
 AND sr.Id_Col    = ss.Id_Col
 AND sr.Id_Siz    = ss.Id_Siz
WHERE sr.Id_Soko IS NULL

ORDER BY
    CheckResult,
    ss.Id_Soko,
    ss.Id_Shohin,
    ss.Id_Col,
    ss.Id_Siz;