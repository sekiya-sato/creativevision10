using System.IO;
using System.Text.RegularExpressions;
using CvBase;
using CvWpfclient.ViewModels._31Monthly;
using CvWpfclient.Views._31Monthly;
using CvWpfclient.ViewModels._00System;
using CvWpfclient.Views._00System;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>実画面→既存一括登録I/F→隔離SQLiteで積送中クリアを検証する。</summary>
public static class InTransitClearScenario {
	private static InTransitClearSeeder.Result? _seeded;
	public static void Seeder(string dbPath) => _seeded = InTransitClearSeeder.Seed(dbPath, Console.WriteLine);

	public static async Task RunAsync(VmSession session) {
		var seed = _seeded ?? throw new InvalidOperationException("専用DBシードが必要です。");
		Directory.CreateDirectory(Path.Combine("..", ".tmp_ui_check", "intransit-screens"));
		session.SetDialogResponder(request => request.Button is System.Windows.MessageBoxButton.YesNo or System.Windows.MessageBoxButton.YesNoCancel
			? System.Windows.MessageBoxResult.Yes : System.Windows.MessageBoxResult.OK);
		var d = session.OpenView<InTransitClearView, InTransitClearViewModel>();
		await d.RunAsync("初期一覧", vm => vm.InitCommand);
		session.Check("初期は全未選択", d.Vm.Rows.Count == 3 && d.Vm.Rows.All(row => !row.IsTarget), new { count = d.Vm.Rows.Count });
		var a = d.Vm.Rows.Single(row => row.WarehouseCode == "UATVM-TC-A");
		session.Check("合計0でも正負SKUがあれば一覧表示", a.TransitQty == 0 && a.PositiveQty == 10 && a.NegativeQty == -10 && a.SkuCount == 2,
			new { a.TransitQty, a.PositiveQty, a.NegativeQty, a.SkuCount });
		a.IsTarget = true;
		session.Check("停止確認なしは実行不可", !d.Vm.ExecuteCommand.CanExecute(null));
		d.Vm.OperationStopped = true;
		d.Vm.Rows.Single(row => row.WarehouseCode == "UATVM-TC-B").IsTarget = true;
		await HaibunScreenScenario.CaptureAsync(session, d.View, Path.Combine("..", ".tmp_ui_check", "intransit-screens"), "InTransitClearBefore");
		await d.RunAsync("A/Bをクリア", vm => vm.ExecuteCommand);
		var slips = await session.QueryAsync<Tran11IdoIn>("order by Id");
		session.Check("選択倉庫だけ移動受作成", slips.Count == 2 && slips.All(s => s.Id_Ido == seed.A || s.Id_Ido == seed.B)
			&& slips.Sum(s => s.Jmeisai?.Count ?? 0) == 3, new { count = slips.Count });
		session.Check("監査備考・倉庫・紐付け・金額", slips.Count > 0 && slips.All(s => s.Id_Soko == s.Id_Ido && s.RelateNo1 == 0
			&& Regex.IsMatch(s.Memo, @"^積送中クリア実行 \d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2}$")
			&& s.Jmeisai is { Count: > 0 } && s.Jmeisai.All(m => m.Kingaku == 0)), slips.Select(s => new { s.Id, s.Id_Soko, s.Id_Ido, s.RelateNo1, s.Memo }));
		await CheckStock(session, seed, "A正SKU", seed.A, seed.Product1, 0, 10);
		await CheckStock(session, seed, "A負SKU", seed.A, seed.Product2, 0, -10);
		await CheckStock(session, seed, "B正SKU", seed.B, seed.Product1, 0, 7);
		await CheckStock(session, seed, "未選択C", seed.C, seed.Product1, -3, 0);
		session.Check("成功後に選択解除", d.Vm.Rows.All(row => !row.IsTarget) && !d.Vm.HasUncertainResult);
		var rebuild = session.OpenView<StockKakeUpdateView, StockKakeUpdateViewModel>();
		await rebuild.RunAsync("再集計初期化", vm => vm.InitCommand);
		rebuild.Input("再集計対象", vm => { vm.YearMonthFrom = "2026/09"; vm.YearMonthTo = DateTime.Today.ToString("yyyy/MM"); vm.UpdateTarget = "在庫のみ"; });
		await rebuild.RunAsync("移動受登録後の在庫再集計", vm => vm.ExecuteCommand);
		session.Check("再集計完了", rebuild.Vm.ProgressValue == 100 && rebuild.Vm.StatusMessage.Contains("完了", StringComparison.Ordinal));
		await CheckStock(session, seed, "再集計後A正SKU", seed.A, seed.Product1, 0, 10);
		await CheckStock(session, seed, "再集計後A負SKU", seed.A, seed.Product2, 0, -10);
		await CheckStock(session, seed, "再集計後B正SKU", seed.B, seed.Product1, 0, 7);
		await CheckStock(session, seed, "再集計後未選択C", seed.C, seed.Product1, -3, 0);
		rebuild.View.Close();
		await d.RunAsync("無選択再実行", vm => vm.ExecuteCommand);
		session.CheckEqual("再実行による伝票追加なし", slips.Count, (await session.QueryAsync<Tran11IdoIn>("order by Id")).Count);

		// Aを再度正負同量にし、一覧取得後にSKU内訳だけ変える。倉庫合計では競合を見抜けないケース。
		await session.InsertAsync(InTransitClearSeeder.CreateOut(seed, seed.A, seed.Product1, 4));
		await session.InsertAsync(InTransitClearSeeder.CreateOut(seed, seed.A, seed.Product2, -4));
		await d.RunAsync("競合前一覧", vm => vm.LoadStatusCommand);
		d.Vm.Rows.Single(row => row.WarehouseCode == "UATVM-TC-A").IsTarget = true;
		await session.InsertAsync(InTransitClearSeeder.CreateOut(seed, seed.A, seed.Product1, 1));
		await session.InsertAsync(InTransitClearSeeder.CreateOut(seed, seed.A, seed.Product2, -1));
		await d.RunAsync("同合計SKU差で実行中止", vm => vm.ExecuteCommand);
		session.CheckEqual("SKU内訳競合時は登録なし", slips.Count, (await session.QueryAsync<Tran11IdoIn>("order by Id")).Count);
		await CheckStock(session, seed, "競合後A正SKU", seed.A, seed.Product1, 5, 10);
		await CheckStock(session, seed, "競合後A負SKU", seed.A, seed.Product2, -5, -10);
		await CheckStock(session, seed, "競合後未選択C", seed.C, seed.Product1, -3, 0);
		session.Check("競合は画面へ通知し再取得を要求", d.Vm.StatusMessage.Contains("再取得", StringComparison.Ordinal) && !d.Vm.ExecuteCommand.CanExecute(null), new { d.Vm.StatusMessage });
		await HaibunScreenScenario.CaptureAsync(session, d.View, Path.Combine("..", ".tmp_ui_check", "intransit-screens"), "InTransitClearAfter");
		await session.InsertAsync(InTransitClearSeeder.CreateOut(seed, seed.C, seed.Product2, int.MaxValue));
		await session.InsertAsync(InTransitClearSeeder.CreateOut(seed, seed.C, seed.Product2, 1));
		await d.RunAsync("int上限超過SKUの一覧取得", vm => vm.LoadStatusCommand);
		session.Check("数量上限超過は取得エラーで登録禁止", d.Vm.StatusMessage.Contains("失敗", StringComparison.Ordinal)
			&& !d.Vm.ExecuteCommand.CanExecute(null), new { d.Vm.StatusMessage });
		session.CheckEqual("数量上限超過時は登録追加なし", slips.Count, (await session.QueryAsync<Tran11IdoIn>("order by Id")).Count);
		session.SetDialogResponder(null);
	}

	private static async Task CheckStock(VmSession session, InTransitClearSeeder.Result seed, string name, long warehouse, long product, int transit, int real) {
		var monthly = await session.QueryAsync<SummaryStock>("where Id_Soko=@0 and Id_Shohin=@1 and Id_Col=@2 and Id_Siz=@3",
			warehouse.ToString(), product.ToString(), seed.Color.ToString(), seed.Size.ToString());
		var stock = await session.QueryAsync<SummaryRealStock>("where Id_Soko=@0 and Id_Shohin=@1 and Id_Col=@2 and Id_Siz=@3",
			warehouse.ToString(), product.ToString(), seed.Color.ToString(), seed.Size.ToString());
		session.Check(name, monthly.Sum(row => row.TransitQty) == transit && monthly.Sum(row => row.Su) == real
			&& stock.Sum(row => row.Su) == real, new { expectedTransit = transit, actualTransit = monthly.Sum(row => row.TransitQty),
				expectedReal = real, actualMonthly = monthly.Sum(row => row.Su), actualReal = stock.Sum(row => row.Su) });
	}
}
