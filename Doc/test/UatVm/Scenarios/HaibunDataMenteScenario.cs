using System.IO;
using System.Windows;
using CvBase;
using CvWpfclient.ViewModels._07Haibun;
using CvWpfclient.ViewModels.Sub;
using CvWpfclient.Views._07Haibun;
using CvWpfclient.Views.Sub;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>
/// 配分データメンテ（HaibunDataMenteView）の実画面を表示し、JPG保存と表示崩れの自動判定を行う。
/// <para>
/// 一覧は実際の DoListCommand で取得する。BeforeListAsync が出す RangeInputParamView（選択Win）は
/// ShowDialog の入れ子メッセージループ中に検出し、条件を入れて撮影してから OK で閉じる。
/// データは HaibunScreenScenario と同じシード（JuchuShippingSeeder）に配分4行を登録して作る。
/// </para>
/// </summary>
public static class HaibunDataMenteScenario {
	const string ScreenDirectory = "..\\Doc\\test\\uat20261006\\haibun-data-mente\\screens";
	const string DenDay = "20260905";
	const string OldDenDay = "20260901";
	static JuchuShippingSeeder.Result? _seeded;

	public static void Seeder(string dbPath) =>
		_seeded = JuchuShippingSeeder.Seed(dbPath, message => Console.WriteLine($"[seed] {message}"));

	public static async Task RunAsync(VmSession session) {
		var seeded = _seeded ?? throw new InvalidOperationException("シードが実行されていません。");
		var screens = Path.Combine(Path.GetFullPath(ScreenDirectory), DateTime.Now.ToString("yyyyMMdd_HHmmss"));
		Directory.CreateDirectory(screens);
		session.Note("実行方法", new { Command = "UatVm haibun-data-mente --sqlite <複製DB> --manage-server", Screens = screens });
		session.SetDialogResponder(request => MessageBoxResult.OK);

		var open1 = await session.InsertAsync(NewHaibun(seeded, seeded.TokuiId, DenDay, 5));
		var open2 = await session.InsertAsync(NewHaibun(seeded, seeded.DirectStoreId, DenDay, 2));
		var done = NewHaibun(seeded, seeded.TokuiId, OldDenDay, 4);
		done.EndFlag = 1;
		done.JitsuSu = 3;
		done.ShortSu = 1;
		done.KakuteiDay = OldDenDay;
		done.Memo = "UAT 配分データメンテ確認用";
		var done1 = await session.InsertAsync(done);
		session.Check("配分3行を登録", open1.Id > 0 && open2.Id > 0 && done1.Id > 0, new { open1 = open1.Id, open2 = open2.Id, done = done1.Id });

		var d = session.OpenView<HaibunDataMenteView, HaibunDataMenteViewModel>();
		await HaibunScreenScenario.CaptureAsync(session, d.View, screens, "01_HaibunDataMente_Empty");

		// 選択Win（RangeInputParamView）を入れ子ループ中に捕まえる。待たずに開始し、DoList 完了後に結果を確認する
		var dialogTask = HandleRangeDialogAsync(session, screens);
		await d.RunAsync("一覧(DoList)", vm => vm.DoListCommand);
		var dialogHandled = await dialogTask;
		session.Check("選択Winを検出しOKで閉じた", dialogHandled);
		session.Check("一覧に3行以上", d.Vm.ListData.Count >= 3, new { d.Vm.ListData.Count, d.Vm.Message });
		await HaibunScreenScenario.CaptureAsync(session, d.View, screens, "03_HaibunDataMente_List");

		// 完了行を選択して右側編集フォームに表示
		var target = d.Vm.ListData.FirstOrDefault(x => x.Id == done1.Id) ?? d.Vm.ListData.FirstOrDefault();
		if (target != null) {
			d.Input("完了行を選択", vm => vm.Current = target, new { target.Id });
			session.Check("編集フォームに選択行", d.Vm.CurrentEdit.Id == target.Id,
				new { d.Vm.CurrentEdit.Id, d.Vm.CurrentEdit.SokoName, d.Vm.CurrentEdit.TenpoName, d.Vm.CurrentEdit.ShohinName });
			await HaibunScreenScenario.CaptureAsync(session, d.View, screens, "04_HaibunDataMente_Selected");
		}
		session.SetDialogResponder(null);
	}

	/// <summary>RangeInputParamView が開くまで待ち、条件入力→撮影→OK。開かなければ false</summary>
	static async Task<bool> HandleRangeDialogAsync(VmSession session, string screens) {
		for (var i = 0; i < 200; i++) {
			await Task.Delay(100);
			var win = Application.Current.Windows.OfType<RangeInputParamView>().FirstOrDefault(w => w.IsLoaded);
			if (win?.DataContext is not RangeInputParamViewModel vm) continue;
			vm.Parameter.FromDate = OldDenDay;
			vm.Parameter.ToDate = DenDay;
			session.Note("選択Win:条件", new { vm.Parameter.FromDate, vm.Parameter.ToDate, vm.Parameter.ToriLabel, vm.Parameter.MaxCount });
			await HaibunScreenScenario.CaptureAsync(session, win, screens, "02_RangeInputParam");
			vm.OkCommand.Execute(null);
			return true;
		}
		return false;
	}

	static TranHaibun NewHaibun(JuchuShippingSeeder.Result seeded, long idTenpo, string denDay, int su) => new() {
		DenDay = denDay,
		NouhinDay = denDay,
		Id_Soko = seeded.WarehouseId,
		Id_Tenpo = idTenpo,
		Kubun = (int)EnumHaibun.Zaiko,
		Id_Shohin = seeded.ShohinId,
		Id_Col = seeded.Id_Col,
		Id_Siz = seeded.Id_Siz,
		JanCode = JuchuShippingSeeder.JanCode,
		Su = su,
		Tanka = 2000,
		Kingaku = su * 2000,
		Jodai = 2000,
		Gedai = 1000,
		Id_Shain = seeded.EmployeeId,
	};
}
