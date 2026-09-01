using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;

// Doc/spec/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md 9章「適用手順」のうち、
// 請求計算(CalcSummaryUriSei)・支払計算(CalcSummaryKaiShi)を「全締日・全請求/支払月」について
// 自動で回すバッチツール。売掛残(CalcSummaryUriKake)・買掛残(CalcSummaryKaiKake)は対象外
// （日付範囲を1回で指定できるため、別途手動/画面で再計算済みの前提）。
//
// 対象の締日・月は、既存の SummaryUriSei / SummaryKaiShi に既に存在する期間から動的に求める
// （得意先/仕入先マスタの締日ごとに、その締日を持つ得意先/仕入先の既存行の DayTo から
//  請求/支払月を逆算する。GetPeriod の実装上、DayTo は常に対象月と同じ年月になるため
//  YYYYMM(DayTo) がそのまま対象月になる）。ハードコードした締日・月レンジは持たない。
//
// 使い方: seikyushiharai_recalc [plan|run] [dbPath]
//   plan    実行対象（締日×月の一覧と件数）を表示するだけで、DBは一切更新しない（既定）
//   run     実際に CalcSummaryUriSei / CalcSummaryKaiShi を全対象に対して実行する
//   dbPath  省略時 C:\gitroot\new2022\cv10\CvServer\server-user163.db
//
// 前提: 実行前に対象DBのバックアップを取ること。このツール自体はバックアップを取らない。

var mode = args.Length > 0 ? args[0] : "plan";
var dbPath = args.Length > 1 ? args[1] : @"C:\gitroot\new2022\cv10\CvServer\server-user163.db";

if (mode is not ("plan" or "run")) {
    Console.WriteLine($"unknown mode: {mode} (plan|run を指定してください)");
    return 1;
}

Console.WriteLine($"db={dbPath}  mode={mode}");

var cs = new SqliteConnectionStringBuilder {
    DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
}.ToString();
using var conn = new SqliteConnection(cs);
conn.Open();
var db = new ExDatabaseSqlite(conn) { KeepConnectionAlive = true };
await UpdateDb.WriteVersionInfoAsync(db); // 対象DBのスキーマをUpdateDb.versionsの最新まで追随させる
var summaryDb = new SummaryDb(db);

// 締日ごとに、その締日を持つ得意先/仕入先の既存 SummaryUriSei/SummaryKaiShi 行から
// 対象の請求月/支払月(YYYYMM)を列挙する。
List<(int Shime, string Yyyymm)> PlanUriSei() {
    var shimes = db.Fetch<int>(
        "SELECT DISTINCT Shime1 FROM MasterTokui WHERE Shime1 BETWEEN 1 AND 28 OR Shime1 = 99 ORDER BY Shime1");
    var plan = new List<(int, string)>();
    foreach (var shime in shimes) {
        var months = db.Fetch<string>(@"
SELECT DISTINCT substr(s.DayTo, 1, 6) AS Ym
FROM SummaryUriSei s
JOIN MasterTokui t ON t.Id = s.Id_Tokui
WHERE t.Shime1 = @0
ORDER BY Ym", shime);
        plan.AddRange(months.Select(m => (shime, m)));
    }
    return plan;
}

List<(int Shime, string Yyyymm)> PlanKaiShi() {
    var shimes = db.Fetch<int>(
        "SELECT DISTINCT Shime1 FROM MasterShiire WHERE Shime1 BETWEEN 1 AND 28 OR Shime1 = 99 ORDER BY Shime1");
    var plan = new List<(int, string)>();
    foreach (var shime in shimes) {
        var months = db.Fetch<string>(@"
SELECT DISTINCT substr(s.DayTo, 1, 6) AS Ym
FROM SummaryKaiShi s
JOIN MasterShiire t ON t.Id = s.Id_Shiire
WHERE t.Shime1 = @0
ORDER BY Ym", shime);
        plan.AddRange(months.Select(m => (shime, m)));
    }
    return plan;
}

var uriSeiPlan = PlanUriSei();
var kaiShiPlan = PlanKaiShi();

Console.WriteLine($"\n請求計算(CalcSummaryUriSei) 対象: {uriSeiPlan.Count} 件");
foreach (var g in uriSeiPlan.GroupBy(x => x.Shime))
    Console.WriteLine($"  締日={g.Key,3}  {g.Min(x => x.Yyyymm)}〜{g.Max(x => x.Yyyymm)} ({g.Count()}ヶ月)");

Console.WriteLine($"\n支払計算(CalcSummaryKaiShi) 対象: {kaiShiPlan.Count} 件");
foreach (var g in kaiShiPlan.GroupBy(x => x.Shime))
    Console.WriteLine($"  締日={g.Key,3}  {g.Min(x => x.Yyyymm)}〜{g.Max(x => x.Yyyymm)} ({g.Count()}ヶ月)");

if (mode == "plan") {
    Console.WriteLine("\n(plan モードのため実行はしていません。run で実行してください)");
    return 0;
}

var okCount = 0;
var ngCount = 0;
var totalRows = 0L;
var errors = new List<string>();

Console.WriteLine("\n===== 請求計算(CalcSummaryUriSei) 実行 =====");
foreach (var (shime, ym) in uriSeiPlan) {
    try {
        var n = summaryDb.CalcSummaryUriSei(ym, shime);
        totalRows += n;
        okCount++;
        Console.WriteLine($"  OK  締日={shime,3} {ym}  {n,6} 行");
    }
    catch (Exception ex) {
        ngCount++;
        var msg = $"  NG  締日={shime,3} {ym}  {ex.GetType().Name}: {ex.Message}";
        Console.WriteLine(msg);
        errors.Add(msg);
    }
}

Console.WriteLine("\n===== 支払計算(CalcSummaryKaiShi) 実行 =====");
foreach (var (shime, ym) in kaiShiPlan) {
    try {
        var n = summaryDb.CalcSummaryKaiShi(ym, shime);
        totalRows += n;
        okCount++;
        Console.WriteLine($"  OK  締日={shime,3} {ym}  {n,6} 行");
    }
    catch (Exception ex) {
        ngCount++;
        var msg = $"  NG  締日={shime,3} {ym}  {ex.GetType().Name}: {ex.Message}";
        Console.WriteLine(msg);
        errors.Add(msg);
    }
}

Console.WriteLine($"\n===== 結果 =====");
Console.WriteLine($"成功: {okCount} 件  失敗: {ngCount} 件  合計挿入行数: {totalRows}");
if (errors.Count > 0) {
    Console.WriteLine("\n失敗一覧:");
    foreach (var e in errors) Console.WriteLine(e);
}

db.Close();
Console.WriteLine("(done)");
return ngCount == 0 ? 0 : 1;
