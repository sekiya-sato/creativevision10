using UatVm;
using UatVm.Scenarios;

// VM駆動UATハーネスの入口。
// シナリオを増やす場合は _scenarios へ1行追加する（ハーネス本体は変更しない）。
//
//   dotnet run --project Doc\test\UatVm\UatVm.csproj -- billing --url http://127.0.0.1:5002
//
var scenarios = new Dictionary<string, Func<VmSession, Task>>(StringComparer.OrdinalIgnoreCase) {
	["billing"] = BillingCalculationScenario.RunAsync,
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
	Console.Error.WriteLine("  --hide-views       Viewを表示しない");
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

var options = new VmHost.Options {
	ScenarioName = name.ToLowerInvariant(),
	ServerUrl = Option("--url"),
	ShowViews = !Flag("--hide-views"),
	ManageServer = Flag("--manage-server"),
};

var failures = VmHost.Run(options, scenario);
Console.WriteLine(failures == 0 ? "VERDICT: PASS" : $"VERDICT: FAIL ({failures} 件)");
return failures == 0 ? 0 : 1;
