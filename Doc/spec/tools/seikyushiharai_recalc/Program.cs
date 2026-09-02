using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;

// Doc/spec/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md 9章「適用手順」のうち、
// 請求計算(CalcSummaryUriSei)・支払計算(CalcSummaryKaiShi)を「全締日・全請求/支払月」について
// 自動で回すバッチツール。
//
// 売掛残(CalcSummaryUriKake)・買掛残(CalcSummaryKaiKake)は日付範囲を1回の呼び出しで指定できるため
// 締日×月のループを持たず、kake-plan / kake-run モードで「全期間」を1回ずつ実行する。
//
// 対象の締日・月は、既存の SummaryUriSei / SummaryKaiShi に既に存在する期間から動的に求める
// （得意先/仕入先マスタの締日ごとに、その締日を持つ得意先/仕入先の既存行の DayTo から
//  請求/支払月を逆算する。GetPeriod の実装上、DayTo は常に対象月と同じ年月になるため
//  YYYYMM(DayTo) がそのまま対象月になる）。ハードコードした締日・月レンジは持たない。
//
// 使い方: seikyushiharai_recalc [plan|run|kake-plan|kake-run] [dbPath]
//   plan       請求計算・支払計算の実行対象（締日×月の一覧と件数）を表示するだけ。DBは更新しない（既定）
//   run        CalcSummaryUriSei / CalcSummaryKaiShi を全対象に対して実行する
//   kake-plan  売掛残・買掛残の対象期間（年月レンジ）を表示するだけ。DBは更新しない
//   kake-run   CalcSummaryUriKake / CalcSummaryKaiKake を全期間に対して実行する
//   dbPath     省略時 C:\gitroot\new2022\cv10\CvServer\server-user163.db
//
// 前提: 実行前に対象DBのバックアップを取ること。このツール自体はバックアップを取らない。

var mode = args.Length > 0 ? args[0] : "plan";
var dbPath = args.Length > 1 ? args[1] : @"C:\gitroot\new2022\cv10\CvServer\server-user163.db";

if (mode is not ("plan" or "run" or "kake-plan" or "kake-run")) {
    Console.WriteLine($"unknown mode: {mode} (plan|run|kake-plan|kake-run を指定してください)");
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

// ---- 売掛残・買掛残（kake-plan / kake-run） -------------------------------------------
// CalcSummaryUriKake / CalcSummaryKaiKake は (fromYYYYMM, toYYYYMM) の範囲を1回で受け取り、
// 内部で期首年月(MasterSysman.FiscalStartDate)より前を切り捨てる。したがって締日×月のループは不要で、
// 「全期間」を1回ずつ呼べばよい。
//
// from は期首年月をそのまま渡す（呼び出し先が同じ値でクランプするため、これが全期間の下限になる）。
// to は伝票側の KakeDay と既存 Summary 行から動的に求め、締日が月末でないときの翌月繰り上がりを
// 取りこぼさないよう1ヶ月足す。ハードコードした月レンジは持たない。
if (mode is "kake-plan" or "kake-run") {
    var fiscalStart = db.FirstOrDefault<string>("SELECT FiscalStartDate FROM MasterSysman ORDER BY Id LIMIT 1") ?? "";
    if (fiscalStart.Length < 6) {
        Console.WriteLine("MasterSysman.FiscalStartDate が取得できません。");
        return 1;
    }
    var fromYm = fiscalStart[..6];

    // 集計元テーブルの KakeDay（売掛: 売上・入金 / 買掛: 仕入・生地付属・支払）と既存 Summary 行の最大月。
    string? MaxMonthOf(string sql) => db.FirstOrDefault<string?>(sql);
    var candidates = new[] {
        MaxMonthOf("SELECT MAX(substr(KakeDay, 1, 6)) FROM Tran00Uriage WHERE KakeDay <> ''"),
        MaxMonthOf("SELECT MAX(substr(KakeDay, 1, 6)) FROM Tran06Nyukin WHERE KakeDay <> ''"),
        MaxMonthOf("SELECT MAX(substr(KakeDay, 1, 6)) FROM Tran03Shiire WHERE KakeDay <> ''"),
        MaxMonthOf("SELECT MAX(substr(KakeDay, 1, 6)) FROM Tran02Material WHERE KakeDay <> ''"),
        MaxMonthOf("SELECT MAX(substr(KakeDay, 1, 6)) FROM Tran07Shiharai WHERE KakeDay <> ''"),
        MaxMonthOf("SELECT MAX(DenMonth) FROM SummaryUriKake"),
        MaxMonthOf("SELECT MAX(DenMonth) FROM SummaryKaiKake"),
    }.Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).ToList();
    if (candidates.Count == 0) {
        Console.WriteLine("集計対象の伝票・既存Summary行が1件も無いため、実行対象がありません。");
        return 0;
    }
    // 締日が月末でない得意先では KakeDay の月が翌月へ繰り上がるため、上限に1ヶ月足す。
    var maxYm = candidates.OrderByDescending(x => x, StringComparer.Ordinal).First();
    var toYm = DateTime.ParseExact(maxYm, "yyyyMM", null).AddMonths(1).ToString("yyyyMM");

    Console.WriteLine($"\n売掛残(CalcSummaryUriKake) / 買掛残(CalcSummaryKaiKake) 対象期間: {fromYm}〜{toYm}");
    Console.WriteLine($"  （期首年月={fromYm} / 伝票・既存Summaryの最大月={maxYm} +1ヶ月）");

    if (mode == "kake-plan") {
        Console.WriteLine("\n(kake-plan モードのため実行はしていません。kake-run で実行してください)");
        return 0;
    }

    var kakeOk = 0;
    var kakeNg = 0;
    var kakeRows = 0L;
    foreach (var (label, calc) in new (string, Func<int>)[] {
        ("売掛残(CalcSummaryUriKake)", () => summaryDb.CalcSummaryUriKake(fromYm, toYm)),
        ("買掛残(CalcSummaryKaiKake)", () => summaryDb.CalcSummaryKaiKake(fromYm, toYm)),
    }) {
        try {
            var n = calc();
            kakeRows += n;
            kakeOk++;
            Console.WriteLine($"  OK  {label}  {fromYm}〜{toYm}  {n,8} 行");
        }
        catch (Exception ex) {
            kakeNg++;
            Console.WriteLine($"  NG  {label}  {fromYm}〜{toYm}  {ex.GetType().Name}: {ex.Message}");
        }
    }

    Console.WriteLine($"\n===== 結果 =====");
    Console.WriteLine($"成功: {kakeOk} 件  失敗: {kakeNg} 件  合計挿入行数: {kakeRows}");
    db.Close();
    Console.WriteLine("(done)");
    return kakeNg == 0 ? 0 : 1;
}

// 締日ごとに、その締日を持つ得意先/仕入先の既存 SummaryUriSei/SummaryKaiShi 行から
// 対象の請求月/支払月(YYYYMM)を列挙する。
// 締日の列挙は Shime1/2/3 の和集合＋Shime1=0 の自社締日フォールバック(ClosingDaySet.ResolveDistinctDays、4.3)。
// 対象月の逆算ロジック(DayTo の年月をそのまま対象月とみなす)自体は変更しない。
var ownShime = db.FirstOrDefault<int>("SELECT ShimeBi FROM MasterSysman ORDER BY Id LIMIT 1");

List<(int Shime, string Yyyymm)> PlanUriSei() {
    var patterns = db.Fetch<ShimePatternRow>("SELECT DISTINCT Shime1, Shime2, Shime3 FROM MasterTokui");
    var shimes = ClosingDaySet.ResolveDistinctDays(patterns.Select(p => (p.Shime1, p.Shime2, p.Shime3)), ownShime);
    var plan = new List<(int, string)>();
    foreach (var shime in shimes) {
        var months = db.Fetch<string>($@"
SELECT DISTINCT substr(s.DayTo, 1, 6) AS Ym
FROM SummaryUriSei s
JOIN MasterTokui t ON t.Id = s.Id_Tokui
WHERE {ClosingDaySet.ContainsShimeSql("t", "@0", "@1")}
ORDER BY Ym", shime, ownShime);
        plan.AddRange(months.Select(m => (shime, m)));
    }
    return plan;
}

List<(int Shime, string Yyyymm)> PlanKaiShi() {
    var patterns = db.Fetch<ShimePatternRow>("SELECT DISTINCT Shime1, Shime2, Shime3 FROM MasterShiire");
    var shimes = ClosingDaySet.ResolveDistinctDays(patterns.Select(p => (p.Shime1, p.Shime2, p.Shime3)), ownShime);
    var plan = new List<(int, string)>();
    foreach (var shime in shimes) {
        var months = db.Fetch<string>($@"
SELECT DISTINCT substr(s.DayTo, 1, 6) AS Ym
FROM SummaryKaiShi s
JOIN MasterShiire t ON t.Id = s.Id_Shiire
WHERE {ClosingDaySet.ContainsShimeSql("t", "@0", "@1")}
ORDER BY Ym", shime, ownShime);
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
