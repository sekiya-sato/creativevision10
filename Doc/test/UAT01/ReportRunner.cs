using CodeShare;
using CvAsset;
using CvBase;
using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;

const string baseUrl = "http://localhost:5002";

static QueryListSqlParam Sql(string text) => new(typeof(object), text, []);

static string KaikakeSql() => @"
WITH cur AS (
  SELECT Id_Shiire, Balance, TotalOut, TotalShiire, Shiire, Henpin, Nebiki, Tax1+Tax2+Tax3 AS Tax, Cash, Fee
  FROM SummaryKaiKake WHERE DenMonth = '202609'
), prev AS (
  SELECT Id_Shiire, Balance FROM SummaryKaiKake WHERE DenMonth = '202608'
)
SELECT s.Code, s.Name,
       ifnull(p.Balance,0), ifnull(c.TotalShiire,0), ifnull(c.Tax,0),
       ifnull(c.Henpin,0), ifnull(c.Nebiki,0), ifnull(c.TotalOut,0),
       ifnull(c.Cash,0), ifnull(c.Fee,0), ifnull(c.Balance,0)
FROM MasterShiire s
LEFT JOIN cur c ON c.Id_Shiire=s.Id
LEFT JOIN prev p ON p.Id_Shiire=s.Id
WHERE s.Code='UAT01-SI'
  AND (ifnull(p.Balance,0)<>0 OR ifnull(c.TotalShiire,0)<>0 OR ifnull(c.TotalOut,0)<>0 OR ifnull(c.Balance,0)<>0)
ORDER BY s.Code";

static string ShiharaiLedgerSql() => @"
SELECT substr(k.DenDay,1,4)||'/'||substr(k.DenDay,5,2)||'/'||substr(k.DenDay,7,2),
       s.Code, s.Name,
       substr(k.DayFrom,1,4)||'/'||substr(k.DayFrom,5,2)||'/'||substr(k.DayFrom,7,2)||'～'||
       substr(k.DayTo,1,4)||'/'||substr(k.DayTo,5,2)||'/'||substr(k.DayTo,7,2),
       k.TotalShiire, k.Tax1+k.Tax2+k.Tax3, k.TotalOut, k.Balance,
       substr(k.ShiharaiYoteiDay,1,4)||'/'||substr(k.ShiharaiYoteiDay,5,2)||'/'||substr(k.ShiharaiYoteiDay,7,2)
FROM SummaryKaiShi k JOIN MasterShiire s ON s.Id=k.Id_Shiire
WHERE k.DenDay>='20260901' AND k.DenDay<='20260930'
  AND (k.TotalOut<>0 OR k.Balance<>0) AND s.Code='UAT01-SI'
ORDER BY k.DenDay,s.Code";

static string MonthlyPaymentSql() => @"
SELECT '2026/09' AS yoteiYmLabel, '2026/09/30' AS yoteiDayLabel,
       s.Code AS shiireCode, s.Name AS shiireName,
       SUM(k.TotalShiire) AS totalShiire, SUM(k.TotalOut) AS totalOut,
       SUM(k.Balance) AS yoteiKingaku, COUNT(*) AS shimeCount,
       '2026/09/30' AS lastShimeDay
FROM SummaryKaiShi k JOIN MasterShiire s ON s.Id=k.Id_Shiire
WHERE k.DenDay='20260930' AND s.Code='UAT01-SI'
GROUP BY s.Code,s.Name";

var reports = new (string Form, QueryListSqlParam Param)[] {
    ("KaikakeBalanceReport.qfm", Sql(KaikakeSql())),
    ("ShiharaiLedgerReport.qfm", Sql(ShiharaiLedgerSql())),
    ("MonthlyShiharaiYoteiTable.qfm", Sql(MonthlyPaymentSql())),
};

using var channel = GrpcChannel.ForAddress(baseUrl);
var service = channel.CreateGrpcService<ICoreService>();
foreach (var report in reports) {
    var request = new PrintOperation {
        DataType = typeof(QueryListSqlParam),
        DataMsg = Common.SerializeObject(report.Param),
        FormFile = report.Form,
    };
    string? result = null;
    await foreach (var message in service.PrintPdfAsync(request)) {
        Console.WriteLine($"{report.Form} status={message.Status} progress={message.Progress} completed={message.IsCompleted} msg={message.DataMsg}");
        if (message.Status < 0) throw new InvalidOperationException($"{report.Form}: {message.DataMsg}");
        if (message.IsCompleted) result = message.DataMsg;
    }
    if (string.IsNullOrWhiteSpace(result)) throw new InvalidOperationException($"{report.Form}: no result");
    Console.WriteLine($"PDF {report.Form} {result}");
}
