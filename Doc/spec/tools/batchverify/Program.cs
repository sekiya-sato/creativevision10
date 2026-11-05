// 月次集計・原価・棚卸の3DB検証ツール（batchverify）
//
// 使い方:
//   batchverify <sqlite.db> <出力フォルダ> [--month yyyyMM] [--from yyyyMM] [--only Sqlite,MariaDb,Postgre] [--skip-stocktake]
//
// 各DBで次を行い、集計値（年月ごとの件数・数値列合計・文字列長合計）で照合する。
//   実行前の指紋 → 処理一式(1回目) → 指紋 → 処理一式(2回目) → 指紋
//   冪等性: 1回目と2回目の指紋が同じ / 方言変換: 1回目の指紋と各処理の結果が SQLite と同じ
// 処理一式: 在庫・売掛・買掛の再集計（--from〜--month）、請求残・支払残（--month×実在締日）、
//           原価の確認5種（DBを変えない）、消化仕入更新、棚卸の確認・開始・確定（--month。--skip-stocktake で省く）
// 棚卸は2回目が1回目の確定済み状態に対して動くため、2回目の差は仕様上の差のことがある（再確定状況で判断する）。
// 対象DBを更新する。SQLite は専用の複製、MariaDB / PostgreSQL は appsettings.Development.json の MariaDb / Postgres。
// MariaDB / PostgreSQL の接続先は検証専用DB（DB切替UATで作った cv00）にすること。共有開発DBを指定しない。
using CvAsset;
using CvBase;
using CvBase.Share;
using CvDomainLogic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

if (args.Length < 2) {
	Console.Error.WriteLine("使い方: batchverify <sqlite.db> <出力フォルダ> [--month yyyyMM] [--from yyyyMM] [--only Sqlite,MariaDb,Postgre]");
	return 2;
}
string? Option(string key) {
	var i = Array.FindIndex(args, x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
	return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
var sqlitePath = args[0];
var outDir = args[1];
var month = Option("--month") ?? "202203";
var from = Option("--from") ?? month;
var skipStocktake = args.Contains("--skip-stocktake", StringComparer.OrdinalIgnoreCase);
var only = (Option("--only") ?? "Sqlite,MariaDb,Postgre").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
Directory.CreateDirectory(outDir);
CvBase.Sql.SqlDialectOptions.Mode = CvBase.Sql.SqlDialectMode.Strict;

var repoRoot = FindRepoRoot();
var settings = File.ReadAllText(Path.Combine(repoRoot, "CvServer", "appsettings.Development.json"));
string Conn(string key) => Regex.Match(settings, $"\"{key}\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;

// 比較する表（GroupKey=指紋を分ける式。null は表全体）
var tables = new (string Name, string? GroupKey, string[] Numbers, string[] Texts)[] {
	("SummaryStock", "SumMonth", ["Su", "ReserveQty", "CumulativeSu", "InQty", "OutQty", "TransitQty", "AdjustQty", "BookQty", "ActualQty", "Id_Shohin", "Id_Soko"], ["StocktakeDdate"]),
	("SummaryRealStock", null, ["Su", "ReserveQty", "Id_Shohin", "Id_Soko"], []),
	("SummaryUriKake", "DenMonth", ["Id_Tokui", .. Kake("TotalIn", "TotalSales", "Uriage")], []),
	("SummaryKaiKake", "DenMonth", ["Id_Shiire", .. Kake("TotalOut", "TotalShiire", "Shiire")], []),
	("SummaryUriSei", "substr(DenDay, 1, 6)", ["Id_Tokui", "Renban", .. Kake("TotalIn", "TotalSales", "Uriage")], ["DenDay", "DayFrom", "DayTo", "SeikyuNo", "NyukinYoteiDay"]),
	("SummaryKaiShi", "substr(DenDay, 1, 6)", ["Id_Shiire", .. Kake("TotalOut", "TotalShiire", "Shiire")], ["DenDay", "DayFrom", "DayTo", "ShiharaiYoteiDay"]),
	("Tran03Shiire", "substr(DenDay, 1, 6)", ["SuTotal", "KingakuTotal", "Id_Shiire", "IsStock"], ["Memo"]),
	("Tran61Chosei", "substr(DenDay, 1, 6)", ["SuTotal", "KingakuTotal", "Id_Soko"], []),
	("Tran60TanaDate", null, ["Id_Shop"], ["TanaDay", "FixDay"]),
	("TranGenka", null, ["Id_Shohin"], ["SumMonth"]),
};
static string[] Kake(string t1, string t2, string main) => [
	"Balance", t1, t2, main, "Henpin", "Nebiki", "Sonota", "Tax1", "Tax2", "Tax3",
	"TaxableAmount1", "TaxableAmount2", "TaxableAmount3", "Cash", "Fee", "Densai", "Offset", "Other"];

var dbs = new List<(string Name, Func<ExDatabase> Open)> {
	("Sqlite", () => CvBaseSqlite.ExDatabaseSqlite.GetDbConn(sqlitePath)),
	("MariaDb", () => CvBaseMariadb.ExDatabaseMaria.GetDbConn(Conn("MariaDb"))),
	("Postgre", () => CvBasePostgre.ExDatabasePostgre.GetDbConn(Conn("Postgres"))),
}.Where(d => only.Contains(d.Name, StringComparer.OrdinalIgnoreCase)).ToList();

var all = new Dictionary<string, DbRun>();
foreach (var (name, open) in dbs) {
	using var db = open();
	db.ChangeTimeout(3600);
	var run = new DbRun();
	// 前回の中断で残った排他行を消す（試験用のDBだけを対象にするツールのため）
	var cleared = db.ExecuteDialect($"DELETE FROM {nameof(SysSequence)} WHERE SysSeqType = @0", (int)EmSysSeqType.ManualLock);
	Log($"== {name} 残っていた排他行を削除: {cleared}件");
	Log($"== {name} 実行前の指紋");
	run.Before = Fingerprint(db, tables);
	for (var pass = 1; pass <= 2; pass++) {
		Log($"== {name} {pass}回目");
		var steps = await RunAllAsync(db, from, month, skipStocktake);
		var fp = Fingerprint(db, tables);
		if (pass == 1) { run.Steps1 = steps; run.After1 = fp; } else { run.Steps2 = steps; run.After2 = fp; }
	}
	all[name] = run;
	File.WriteAllText(Path.Combine(outDir, $"{name}.json"), Common.SerializeObject(run));
}

// レポート
var md = new StringBuilder();
md.AppendLine($"# 月次集計・原価・棚卸 3DB検証（{DateTime.Now:yyyy-MM-dd HH:mm}）");
md.AppendLine();
md.AppendLine($"- 期間: 再集計 {from}〜{month}、請求・支払・原価・棚卸 {month}");
md.AppendLine($"- 基準: SQLite {sqlitePath}");
md.AppendLine();
md.AppendLine("## 冪等性（1回目と2回目の結果）");
md.AppendLine();
md.AppendLine("| DB | 処理の結果 | 集計値 |");
md.AppendLine("|---|---|---|");
foreach (var (name, run) in all) {
	var stepDiff = DiffSteps(run.Steps1, run.Steps2);
	var fpDiff = DiffFp(run.After1, run.After2);
	md.AppendLine($"| {name} | {(stepDiff.Count == 0 ? "一致" : $"不一致 {stepDiff.Count}")} | {(fpDiff.Count == 0 ? "一致" : $"不一致 {fpDiff.Count}")} |");
}
if (all.TryGetValue("Sqlite", out var baseRun)) {
	md.AppendLine();
	md.AppendLine("## 方言変換（SQLite との比較、1回目）");
	md.AppendLine();
	md.AppendLine("| DB | 実行前の集計値 | 処理の結果 | 集計値（1回目後） |");
	md.AppendLine("|---|---|---|---|");
	foreach (var (name, run) in all.Where(x => x.Key != "Sqlite")) {
		var beforeDiff = DiffFp(baseRun.Before, run.Before);
		var stepDiff = DiffSteps(baseRun.Steps1, run.Steps1);
		var fpDiff = DiffFp(baseRun.After1, run.After1);
		md.AppendLine($"| {name} | {(beforeDiff.Count == 0 ? "一致" : $"不一致 {beforeDiff.Count}")} | {(stepDiff.Count == 0 ? "一致" : $"不一致 {stepDiff.Count}")} | {(fpDiff.Count == 0 ? "一致" : $"不一致 {fpDiff.Count}")} |");
	}
}
md.AppendLine();
md.AppendLine("## 詳細");
foreach (var (name, run) in all) {
	md.AppendLine();
	md.AppendLine($"### {name}");
	foreach (var (step, result) in run.Steps1)
		md.AppendLine($"- {step}: {Head(result.Result, 300)}（{result.ElapsedMs:N0}ms / 2回目 {run.Steps2.GetValueOrDefault(step)?.ElapsedMs:N0}ms）");
	foreach (var d in DiffSteps(run.Steps1, run.Steps2)) md.AppendLine($"  - 冪等性 不一致 処理: {d}");
	foreach (var d in DiffFp(run.After1, run.After2)) md.AppendLine($"  - 冪等性 不一致 集計値: {d}");
	foreach (var d in DiffFp(run.Before, run.After1).Take(20)) md.AppendLine($"  - 実行前→1回目で変化: {d}");
	if (name != "Sqlite" && baseRun != null) {
		foreach (var d in DiffSteps(baseRun.Steps1, run.Steps1)) md.AppendLine($"  - SQLiteと不一致 処理: {d}");
		foreach (var d in DiffFp(baseRun.After1, run.After1)) md.AppendLine($"  - SQLiteと不一致 集計値: {d}");
	}
}
File.WriteAllText(Path.Combine(outDir, "report.md"), md.ToString());
Log($"レポート: {Path.Combine(outDir, "report.md")}");
return 0;

// ---------------------------------------------------------------

static async Task<Dictionary<string, StepResult>> RunAllAsync(ExDatabase db, string from, string month, bool skipStocktake) {
	var r = new Dictionary<string, StepResult>();
	async Task Step(string name, Func<Task<string>> action) {
		var sw = System.Diagnostics.Stopwatch.StartNew();
		string result;
		try { result = await action(); }
		catch (Exception ex) { result = $"例外 {ex.GetType().Name}: {ex.Message}"; }
		r[name] = new StepResult(result, sw.ElapsedMilliseconds);
		Log($"  {name}: {Head(result, 160)} ({sw.ElapsedMilliseconds:N0}ms)");
	}
	var summary = new SummaryDb(db);
	await Step("在庫再集計", () => Consume(summary.SummaryAllAsyncStream(new CalcDateTermParameter(from, month))));
	await Step("売掛再集計", () => Consume(summary.SummaryUriKakeAsyncStream(new CalcDateTermParameter(from, month))));
	await Step("買掛再集計", () => Consume(summary.SummaryKaiKakeAsyncStream(new CalcDateTermParameter(from, month))));
	foreach (var shime in new[] { 5, 9, 10, 15, 20, 99 })
		await Step($"請求残 締{shime}", () => Consume(summary.SummaryUriSeiAsyncStream(new BillingParameter(month, shime, string.Empty, string.Empty, false))));
	foreach (var shime in new[] { 20, 99 })
		await Step($"支払残 締{shime}", () => Consume(summary.SummaryKaiShiAsyncStream(new BillingParameter(month, shime, string.Empty, string.Empty, false))));

	var cost = new CostUpdateDb(db);
	await Step("原価 消化仕入確認", () => Task.FromResult(Json(cost.PreviewConsumptionPurchases(new CostUpdateParameter { TargetMonth = month, ProcessKind = EnumCostProcessKind.ConsumptionPurchase }))));
	await Step("原価 諸掛確認", () => Task.FromResult(Json(cost.PreviewSundryCharges(new CostUpdateParameter { TargetMonth = month }))));
	await Step("原価 最終仕入原価確認", () => Task.FromResult(Json(cost.PreviewLastPurchaseCost(new CostUpdateParameter { TargetMonth = month, ProcessKind = EnumCostProcessKind.CostUpdate, CostMethod = EnumCostMethod.LastPurchase }))));
	await Step("原価 総平均原価確認", () => Task.FromResult(Json(cost.PreviewTotalAverageCost(new CostUpdateParameter { TargetMonth = month, ProcessKind = EnumCostProcessKind.CostUpdate, CostMethod = EnumCostMethod.TotalAverage }))));
	await Step("原価 評価替え確認", () => Task.FromResult(Json(cost.PreviewRevaluation(new CostRevaluationParameter(
		month, default, new CostRevaluationCondition(), default, default, 90, 0, 1, default, 0, "BV")))));
	// 消化仕入更新は対象月が支払計算済み範囲だと業務チェックで中断する（排他行は解放される）。中断も結果として照合する
	await Step("原価 消化仕入更新", () => Task.FromResult(Json(cost.ApplyConsumptionPurchases(new CostUpdateParameter { TargetMonth = month, ProcessKind = EnumCostProcessKind.ConsumptionPurchase, BatchId = "BV" }))));
	await Step("原価 月次状態", () => Task.FromResult(Json(Enum.GetValues<EnumCostProcessKind>().Select(k => cost.FetchCostMonthStatus(month, k)))));

	if (skipStocktake)
		return r;
	var stocktake = new StocktakeDb(db);
	IReadOnlyList<StocktakeDay> days = [];
	await Step("棚卸 対象日", () => { days = stocktake.ResolveDays(month); return Task.FromResult(Json(days)); });
	if (days.Count > 0) {
		await Step("棚卸 帳簿数", () => Task.FromResult(Json(stocktake.FetchBookQtyAsOf(days[0]).OrderBy(x => Json(x)).Take(2000))));
		await Step("棚卸 日付違い", () => Task.FromResult(Json(stocktake.FetchMisdatedTana(days[0]))));
		await Step("棚卸 開始", () => Task.FromResult($"件数={stocktake.StartStocktake(days)}"));
		await Step("棚卸 確定", () => Task.FromResult(Json(stocktake.FixStocktake(days, 1, alignMisdated: true))));
		await Step("棚卸 再確定状況", () => Task.FromResult(Json(stocktake.FetchRefixStatus(days))));
	}
	return r;
}

static async Task<string> Consume(IAsyncEnumerable<StreamStepProgress> stream) {
	var steps = new List<string>();
	await foreach (var p in stream) {
		if (p.IsError) steps.Add($"エラー {p.StepName}: {p.ErrorMessage}");
		else if (p.Phase == StreamStepProgressPhase.Finished) steps.Add($"{p.StepName}={p.Count}");
	}
	return string.Join(", ", steps);
}

/// <summary>結果を比較用のJSONにする。実行のたびに変わる値（時刻・Vdu・確認時点の最大Vdu・Id・BatchId）は外す</summary>
static string Json(object? value) {
	var json = Common.SerializeObject(value);
	return Regex.Replace(json, @"""(StartedAt|FinishedAt|Vdc|Vdu|\w*MaxVdu|Id|BatchId|ElapsedSeconds|Elapsed\w*)"":(-?\d+(\.\d+)?|""[^""]*""),?", "");
}

static Dictionary<string, string> Fingerprint(ExDatabase db, (string Name, string? GroupKey, string[] Numbers, string[] Texts)[] tables) {
	var result = new Dictionary<string, string>(StringComparer.Ordinal);
	foreach (var (name, group, numbers, texts) in tables) {
		if (!db.IsExistTable(name)) continue;
		var cols = new List<string> { "count(*) AS c0" };
		cols.AddRange(numbers.Select((c, i) => $"ifnull(sum({c}), 0) AS n{i}"));
		cols.AddRange(texts.Select((c, i) => $"ifnull(sum(length({c})), 0) AS t{i}"));
		var sql = group == null
			? $"SELECT 'all' AS g, {string.Join(", ", cols)} FROM {name}"
			: $"SELECT {group} AS g, {string.Join(", ", cols)} FROM {name} GROUP BY {group}";
		try {
			foreach (var row in db.FetchDialect<Dictionary<string, object>>(sql)) {
				var values = row.Where(kv => kv.Key.ToLowerInvariant() != "g")
					.Select(kv => kv.Value is null or DBNull ? "0" : Convert.ToDecimal(kv.Value, CultureInfo.InvariantCulture).ToString("0.######", CultureInfo.InvariantCulture));
				var key = row.First(kv => kv.Key.Equals("g", StringComparison.OrdinalIgnoreCase)).Value?.ToString() ?? "";
				result[$"{name}|{key}"] = string.Join("|", values);
			}
		}
		catch (Exception ex) {
			result[$"{name}|ERROR"] = ex.Message;
		}
	}
	return result;
}

static List<string> DiffFp(Dictionary<string, string> a, Dictionary<string, string> b) =>
	[.. a.Keys.Union(b.Keys).OrderBy(k => k, StringComparer.Ordinal)
		.Where(k => !a.TryGetValue(k, out var x) || !b.TryGetValue(k, out var y) || x != y)
		.Select(k => $"{k}: {a.GetValueOrDefault(k) ?? "(なし)"} → {b.GetValueOrDefault(k) ?? "(なし)"}")];

static List<string> DiffSteps(Dictionary<string, StepResult> a, Dictionary<string, StepResult> b) =>
	[.. a.Keys.Union(b.Keys)
		.Where(k => !a.TryGetValue(k, out var x) || !b.TryGetValue(k, out var y) || x.Result != y.Result)
		.Select(k => $"{k}: {Head(a.GetValueOrDefault(k)?.Result ?? "(なし)", 200)} → {Head(b.GetValueOrDefault(k)?.Result ?? "(なし)", 200)}")];

static string Head(string s, int n) {
	var t = Regex.Replace(s, @"\s+", " ").Trim();
	return t.Length <= n ? t : t[..n] + "…";
}

static void Log(string message) => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {message}");

static string FindRepoRoot() {
	var dir = new DirectoryInfo(AppContext.BaseDirectory);
	while (dir != null && !File.Exists(Path.Combine(dir.FullName, "creativevision10.slnx"))) dir = dir.Parent;
	return dir?.FullName ?? throw new InvalidOperationException("creativevision10.slnx が見つかりません");
}

sealed record StepResult(string Result, long ElapsedMs);

sealed class DbRun {
	public Dictionary<string, string> Before { get; set; } = [];
	public Dictionary<string, StepResult> Steps1 { get; set; } = [];
	public Dictionary<string, string> After1 { get; set; } = [];
	public Dictionary<string, StepResult> Steps2 { get; set; } = [];
	public Dictionary<string, string> After2 { get; set; } = [];
}
