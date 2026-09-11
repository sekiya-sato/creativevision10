using CvBase;
using CvBase.Share;
using CvWpfclient.ViewModels._31Monthly;
using CvWpfclient.Views._31Monthly;
using System.Windows;
using UatVm.Seed;

namespace UatVm.Scenarios;

public static class CostUatScenario {
	private static CostUatSeeder.Result? _seeded;
	public static void Seeder(string dbPath) => _seeded = CostUatSeeder.Seed(dbPath, Console.WriteLine);
	public static async Task RunAsync(VmSession session) {
		var seed = _seeded ?? throw new InvalidOperationException("シードが実行されていません。");
		session.SetDialogResponder(_ => MessageBoxResult.Yes);
		var consumption = session.OpenView<ConsumptionPurchaseUpdateView, ConsumptionPurchaseUpdateViewModel>();
		consumption.Vm.TargetMonth = "2026/09";
		await consumption.RunAsync("UAT-10 消化仕入確認", vm => vm.ConfirmCommand);
		if (!session.Check("UAT-10 消化仕入プレビュー", consumption.Vm.Rows.Count == 1, new { consumption.Vm.TargetCount, consumption.Vm.ErrorCount })) return;
		await consumption.RunAsync("UAT-10 消化仕入更新", vm => vm.UpdateCommand);
		var sundry = session.OpenView<SundryChargesUpdateView, SundryChargesUpdateViewModel>();
		sundry.Vm.TargetMonth = "2026/09";
		await sundry.RunAsync("UAT-10 諸掛確認", vm => vm.ConfirmCommand);
		if (!session.Check("UAT-10 諸掛100", sundry.Vm.SummaryRows.Any(x => x.SundryAmount == 100) && sundry.Vm.ErrorCount == 0, new { sundry.Vm.ErrorCount })) return;
		var average = session.OpenView<TotalAverageCostUpdateView, TotalAverageCostUpdateViewModel>();
		average.Vm.TargetMonth = "2026/09";
		await average.RunAsync("UAT-10 総平均確認", vm => vm.ConfirmCommand);
		if (!session.Check("UAT-10 総平均原価5004", average.Vm.Rows.Any(x => x.Id_Shohin == seed.NormalId && x.AfterCost == 5004), new { average.Vm.ErrorCount })) return;
		await average.RunAsync("UAT-10 総平均更新", vm => vm.UpdateCommand);
		var genka = await session.QueryAsync<TranGenka>("where Id_Shohin=@0 AND SumMonth=@1", seed.NormalId.ToString(), seed.Month);
		if (!session.Check("UAT-10 原価履歴", genka.SingleOrDefault()?.AfterCost == 5004, new { genka = genka.Select(x => x.AfterCost) })) return;
		var revaluation = session.OpenView<CostRevaluationView, CostRevaluationViewModel>();
		revaluation.Vm.TargetMonth = "2026/09";
		revaluation.Vm.RatePercent = 80;
		revaluation.Vm.CondRows.Add(new CostRevalCondRowVm {
			Field = CostRevaluationViewModel.CondFieldOptionsStatic.Single(x => x.Value == EnumCostRevalCondField.ShohinCode),
			CodeFrom = CostUatSeeder.NormalCode,
			CodeTo = CostUatSeeder.NormalCode,
		});
		await revaluation.RunAsync("UAT-10 評価替え確認", vm => vm.ConfirmCommand);
		if (!session.Check("UAT-10 評価替え4003", revaluation.Vm.TargetCount == 1 && revaluation.Vm.ErrorCount == 0 && revaluation.Vm.DetailRows.SingleOrDefault()?.AfterCost == 4003,
			new { revaluation.Vm.TargetCount, revaluation.Vm.ErrorCount, costs = revaluation.Vm.DetailRows.Select(x => x.AfterCost) })) return;
		await revaluation.RunAsync("UAT-10 評価替え更新", vm => vm.UpdateCommand);
		var applied = await session.QueryAsync<TranGenka>("where Id_Shohin=@0 AND SumMonth=@1 AND ChangeKind=@2", seed.NormalId.ToString(), seed.Month, ((int)EnumCostChangeKind.Reval).ToString());
		var appliedRow = applied.SingleOrDefault();
		if (!session.Check("UAT-10 評価替え履歴", appliedRow?.AfterCost == 4003 && appliedRow.SourceRevalId > 0, new { applied = applied.Select(x => new { x.AfterCost, x.SourceRevalId }) })) return;
		revaluation.Vm.SelectedHistoryRow = revaluation.Vm.HistoryRows.SingleOrDefault(x => x.Id == appliedRow!.SourceRevalId);
		await revaluation.RunAsync("UAT-10 評価替え取消", vm => vm.CancelHistoryCommand);
		var canceled = await session.QueryAsync<TranGenka>("where Id_Shohin=@0 AND SumMonth=@1", seed.NormalId.ToString(), seed.Month);
		session.Check("UAT-10 評価替え取消復元", canceled.SingleOrDefault()?.AfterCost == 5004 && !canceled.Any(x => x.ChangeKind == (int)EnumCostChangeKind.Reval), new { costs = canceled.Select(x => new { x.ChangeKind, x.AfterCost }) });
		session.SetDialogResponder(null);
	}
}
