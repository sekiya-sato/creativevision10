using System.IO;
using UatVm;
using UatVm.Scenarios;

// VM駆動UATハーネスの入口。
// シナリオを増やす場合は _scenarios へ1行追加する（ハーネス本体は変更しない）。
//
//   dotnet run --project Doc\test\UatVm\UatVm.csproj -- billing --url http://127.0.0.1:5002
//
var scenarios = new Dictionary<string, Func<VmSession, Task>>(StringComparer.OrdinalIgnoreCase) {
	["billing"] = BillingCalculationScenario.RunAsync,
	["shime20"] = ShimeBoundaryScenario.RunAsync,
	["numbering"] = BillingNumberingScenario.RunAsync,
	["e7"] = PaysakiWarningScenario.RunAsync,
	["closingblock"] = ClosingChangeBlockScenario.RunAsync,
	["taxmix"] = TaxMixScenario.RunAsync,
	["material"] = MaterialPurchaseScenario.RunAsync,
	["shopdailysales"] = ShopDailySalesQueryScenario.RunAsync,
	["jodaibulkextract"] = JodaiBulkExtractScenario.RunAsync,
	["cancel"] = CancelDuringRebuildScenario.RunAsync,
	["invoicepreflight"] = InvoicePreflightScenario.RunAsync,
	["manuallock"] = ManualLockScenario.RunAsync,
	["manuallock2"] = ManualLockCoverageScenario.RunAsync,
	["manuallockrace"] = ManualLockRaceScenario.RunAsync,
	["manuallockrestart1"] = ManualLockRestartScenario.RunAsync,
	["manuallockrestart2"] = ManualLockRestartScenario.RunAsync2,
};

// シナリオが網羅データを必要とする場合の投入処理。CvServer起動前に呼ばれる。
var seeders = new Dictionary<string, Action<string>>(StringComparer.OrdinalIgnoreCase) {
	["shime20"] = ShimeBoundaryScenario.Seeder,
	["numbering"] = BillingNumberingScenario.Seeder,
	["e7"] = PaysakiWarningScenario.Seeder,
	["closingblock"] = ClosingChangeBlockScenario.Seeder,
	["taxmix"] = TaxMixScenario.Seeder,
	["material"] = MaterialPurchaseScenario.Seeder,
};

var name = args.FirstOrDefault(x => !x.StartsWith('-'));
if (string.IsNullOrEmpty(name) || !scenarios.TryGetValue(name, out var scenario)) {
	Console.Error.WriteLine($"使い方: UatVm <scenario> [options]");
	Console.Error.WriteLine($"  scenario : {string.Join(" | ", scenarios.Keys)}");
	Console.Error.WriteLine("  --url <url>        接続先CvServer（既定: appsettings.jsonの値）");
	Console.Error.WriteLine("  --manage-server    CvServerの起動と終了(Ctrl+C相当)をハーネスが行う");
	Console.Error.WriteLine("  --month <yyyy/MM>  請求月（billing）");
	Console.Error.WriteLine("  --code <code>      対象取引先コード（billing）");
	Console.Error.WriteLine("  --no-execute       更新を伴う実行を省き、入力検証だけ行う");
	Console.Error.WriteLine("  --no-seed          網羅データの投入を省く（前回投入済みを再利用）");
	Console.Error.WriteLine("  --hide-views       Viewを表示しない");
	Console.Error.WriteLine("  --fire-at <HH:mm:ss> 同日の壁時計時刻まで待ってから実行する（manuallockrace）");
	Console.Error.WriteLine("  --race-label <名前>  証跡・記録上の自分の名前、例 A/B（manuallockrace、既定は請求計算役、Bのみ支払計算役）");
	return 2;
}

string? Option(string key) {
	var i = Array.FindIndex(args, x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
	return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
bool Flag(string key) => args.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));

if (Option("--month") is { } month) BillingCalculationScenario.BillingMonth = month;
if (Option("--code") is { } code) BillingCalculationScenario.TokuiCode = code;
if (Flag("--no-execute")) BillingCalculationScenario.Execute = false;

// manuallockrace: 2プロセスを同一の壁時計時刻で発火させるための同期パラメータ（Run-ManualLockRace.ps1が渡す）。
if (Option("--race-label") is { } raceLabel) ManualLockRaceScenario.RaceLabel = raceLabel;
if (Option("--fire-at") is { } fireAtRaw) {
	if (!TimeSpan.TryParse(fireAtRaw, out var timeOfDay)) {
		Console.Error.WriteLine($"--fire-at の形式が不正です（HH:mm:ss を指定してください）: {fireAtRaw}");
		return 2;
	}
	var fireAt = DateTime.Today + timeOfDay;
	// 深夜跨ぎの保険: 23:59:40 に起動して +40秒を渡すと当日の 00:00:20 は過去になり、
	// 両プロセスが待たずに即撃って同期が崩れる。大きく過去なら翌日として解釈する。
	if (fireAt < DateTime.Now.AddHours(-12)) {
		fireAt = fireAt.AddDays(1);
	}
	ManualLockRaceScenario.FireAt = fireAt;
}

var options = new VmHost.Options {
	ScenarioName = name.ToLowerInvariant(),
	ServerUrl = Option("--url"),
	ShowViews = !Flag("--hide-views"),
	ManageServer = Flag("--manage-server"),
	Seed = Flag("--no-seed") ? null : seeders.GetValueOrDefault(name),
};

// manuallockrace は同一秒に2プロセスが起動しうるため、既定の証跡ファイル名(<scenario>-<yyyyMMdd-HHmmss>.jsonl)
// では衝突しかねない。race-labelとミリ秒・PIDを含めた専用の名前にして衝突を避ける
// （ハーネス本体(VmHost)は変更せず、公開済みのOptions.EvidencePathへ設定するだけで足りる）。
if (string.Equals(options.ScenarioName, "manuallockrace", StringComparison.OrdinalIgnoreCase)) {
	var label = string.IsNullOrWhiteSpace(ManualLockRaceScenario.RaceLabel) ? "unlabeled" : ManualLockRaceScenario.RaceLabel;
	var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
	var fileName = $"{options.ScenarioName}-{label}-{stamp}-pid{Environment.ProcessId}.jsonl";
	// baseDir(CvWpfclientフォルダ)がカレントディレクトリになった後に解決されるため、そこからの相対パスで指定する。
	options.EvidencePath = Path.Combine("..", "Doc", "test", "UatVm", "out", fileName);
}

var failures = VmHost.Run(options, scenario);
Console.WriteLine(failures == 0 ? "VERDICT: PASS" : $"VERDICT: FAIL ({failures} 件)");
return failures == 0 ? 0 : 1;
