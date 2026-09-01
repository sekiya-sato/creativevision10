using CvBase;
using CvWpfclient.ViewModels._00System;
using CvWpfclient.Views._00System;
using System.Windows;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>
/// C-04 明細別消費税（標準／軽減混在）を「伝票税額再更新」画面（システム管理）から検証する。
/// </summary>
/// <remarks>
/// <para>
/// `TranTaxRebuildDb`は対象6伝票の期首日以降を全件走査して再計算する冪等な一括再計算処理であり、
/// 実データも対象に含まれる。そこで<see cref="TaxMixSeeder"/>がヘッダTax未設定の伝票を1件だけ
/// 投入し、その伝票が期待通りに再計算されることを確認する。
/// </para>
/// <para>
/// 対象画面（`SysExecMiscViewModel`）は`InitCommand`を持たずモーダル前提の初期化も無いため、
/// 他のシナリオより単純にコマンドを1つ実行するだけで済む。
/// </para>
/// </remarks>
public static class TaxMixScenario {
	private static TaxMixSeeder.Result? _seeded;

	/// <summary>C-01と同じ専用得意先を使う。既に締日20の売上があること。</summary>
	public static void Seeder(string dbPath) {
		ShimeBoundarySeeder.Seed(dbPath, message => Console.WriteLine($"[seed:shime20] {message}"));
		_seeded = TaxMixSeeder.Seed(dbPath, message => Console.WriteLine($"[seed:taxmix] {message}"));
	}

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded
			?? throw new InvalidOperationException("シードが実行されていません。Options.Seed に Seeder を設定してください。");

		session.Note("seed:投入内容", new {
			seeded.DenId,
			seeded.ExpectedTax,
			seeded.ExpectedTotal,
			standard = new { Kingaku = TaxMixSeeder.StandardKingaku, Rate = TaxMixSeeder.StandardRate },
			reduced = new { Kingaku = TaxMixSeeder.ReducedKingaku, Rate = TaxMixSeeder.ReducedRate },
		});

		var before = await ReadUriageAsync(session, seeded.DenId);
		var beforeTax = before == null ? 0 : before.Tax1 + before.Tax2 + before.Tax3;
		session.Check("C-04 投入直後は明細税額が未設定", before != null && beforeTax == 0 && before.Jmeisai.All(m => m.Tax == 0),
			new { beforeTax, meisaiTax = before?.Jmeisai.Select(m => m.Tax) });

		var d = session.OpenView<SysExecMiscView, SysExecMiscViewModel>();

		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo
			? (request.Message.Contains("明細の消費税が未設定の伝票", StringComparison.Ordinal)
				? MessageBoxResult.Yes
				: MessageBoxResult.No)
			: MessageBoxResult.OK);

		await d.RunAsync("execute:伝票税額再更新", vm => vm.TranTaxRebuildCommand);
		d.Snapshot("execute後", vm => new { vm.ResultMessage, vm.IsProcessing });

		var errors = session.Dialogs.Where(x => x.Request.Image == MessageBoxImage.Error).ToList();
		if (!session.Check("C-04 エラーなく完走", errors.Count == 0 && !d.Vm.IsProcessing,
			new { errors = errors.Select(x => x.Request.Message), d.Vm.ResultMessage })) {
			return;
		}

		var completed = session.Dialogs.FirstOrDefault(x => x.Request.Image == MessageBoxImage.Information);
		session.Check("C-04 完了ダイアログが出る", completed != null,
			new { message = completed?.Request.Message });

		var after = await ReadUriageAsync(session, seeded.DenId);
		if (!session.Check("C-04 投入した伝票が読み戻せる", after != null, new { seeded.DenId })) return;

		session.CheckEqual("C-04 ヘッダTaxが標準10%+軽減8%の合計になる", seeded.ExpectedTax, after!.Tax1 + after.Tax2 + after.Tax3);
		session.CheckEqual("C-04 ヘッダTotalが再計算される", seeded.ExpectedTotal, after.Total);

		var standardLine = after.Jmeisai.FirstOrDefault(m => m.Id_Shohin == seeded.StandardShohinId);
		var reducedLine = after.Jmeisai.FirstOrDefault(m => m.Id_Shohin == seeded.ReducedShohinId);
		session.Check("C-04 標準税率明細のId_Taxが1", standardLine?.Id_Tax == 1, new { standardLine?.Id_Tax });
		session.CheckEqual("C-04 標準税率明細の適用税率が10%", TaxMixSeeder.StandardRate, (int)(standardLine?.TaxRate ?? -1));
		session.CheckEqual("C-04 標準税率明細の税額",
			(int)Math.Round(TaxMixSeeder.StandardKingaku * TaxMixSeeder.StandardRate / 100.0), standardLine?.Tax ?? -1);

		session.Check("C-04 軽減税率明細のId_Taxが2", reducedLine?.Id_Tax == 2, new { reducedLine?.Id_Tax });
		session.CheckEqual("C-04 軽減税率明細の適用税率が8%（標準10%と異なる＝混在が効いている）",
			TaxMixSeeder.ReducedRate, (int)(reducedLine?.TaxRate ?? -1));
		session.CheckEqual("C-04 軽減税率明細の税額",
			(int)Math.Round(TaxMixSeeder.ReducedKingaku * TaxMixSeeder.ReducedRate / 100.0), reducedLine?.Tax ?? -1);

		session.SetDialogResponder(null);
	}

	private static async Task<Tran00Uriage?> ReadUriageAsync(VmSession session, long id) {
		var rows = await session.QueryAsync<Tran00Uriage>("SELECT * FROM Tran00Uriage WHERE Id=@0", id.ToString());
		return rows.FirstOrDefault();
	}
}
