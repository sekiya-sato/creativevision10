using System.IO;
using System.Windows;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
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
		// 削除用・競合用の未完了行を追加
		var open3 = await session.InsertAsync(NewHaibun(seeded, seeded.TokuiId, DenDay, 3));
		var open4 = await session.InsertAsync(NewHaibun(seeded, seeded.DirectStoreId, DenDay, 1));
		session.Check("削除用・競合用の2行を追加", open3.Id > 0 && open4.Id > 0, new { open3 = open3.Id, open4 = open4.Id });

		var d = session.OpenView<HaibunDataMenteView, HaibunDataMenteViewModel>();
		await HaibunScreenScenario.CaptureAsync(session, d.View, screens, "01_HaibunDataMente_Empty");

		// 選択Win（RangeInputParamView）を入れ子ループ中に捕まえる。待たずに開始し、DoList 完了後に結果を確認する
		var dialogTask = HandleRangeDialogAsync(session, screens);
		await d.RunAsync("一覧(DoList)", vm => vm.DoListCommand);
		var dialogHandled = await dialogTask;
		session.Check("選択Winを検出しOKで閉じた", dialogHandled);
		session.Check("一覧に5行以上", d.Vm.ListData.Count >= 5, new { d.Vm.ListData.Count, d.Vm.Message });
		await HaibunScreenScenario.CaptureAsync(session, d.View, screens, "03_HaibunDataMente_List");

		// 完了行を選択して右側編集フォームに表示
		var target = d.Vm.ListData.FirstOrDefault(x => x.Id == done1.Id) ?? d.Vm.ListData.FirstOrDefault();
		if (target != null) {
			d.Input("完了行を選択", vm => vm.Current = target, new { target.Id });
			session.Check("編集フォームに選択行", d.Vm.CurrentEdit.Id == target.Id,
				new { d.Vm.CurrentEdit.Id, d.Vm.CurrentEdit.SokoName, d.Vm.CurrentEdit.TenpoName, d.Vm.CurrentEdit.ShohinName });
			await HaibunScreenScenario.CaptureAsync(session, d.View, screens, "04_HaibunDataMente_Selected");
		}

		await RunEditUatAsync(session, seeded, d, screens, open1, open2, open3, open4);
		session.SetDialogResponder(null);
	}

	/// <summary>修正・削除の実行UAT。DoUpdateCommand / DoDeleteCommand を実際に通す</summary>
	static async Task RunEditUatAsync(VmSession session, JuchuShippingSeeder.Result seeded, ViewDriver<HaibunDataMenteViewModel> d, string screens,
		TranHaibun open1, TranHaibun open2, TranHaibun open3, TranHaibun open4) {
		var dialogs = new List<MessageExTestRoute.Request>();
		// 既定: 確認(Yes/No)は Yes、それ以外は OK。整合警告だけ answerMismatch で応答を切り替える
		var answerMismatch = MessageBoxResult.Yes;
		session.SetDialogResponder(request => {
			dialogs.Add(request);
			if (request.Button is not (MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel)) return MessageBoxResult.OK;
			return request.Message.Contains("完了なのに") ? answerMismatch : MessageBoxResult.Yes;
		});

		// ---- 1. 修正の正常系（未完了 Su=5 → 完了 実3+欠2） ----
		var before1 = await LoadAsync(session, open1.Id);
		var reserve0 = await ReserveAsync(session, seeded);
		Select(d, open1.Id, "1.修正:未完了行を選択");
		d.Input("1.修正:確定日・実数量・欠品数・完了・メモ", vm => {
			vm.CurrentEdit.KakuteiDay = "20260906";
			vm.CurrentEdit.JitsuSu = 3;
			vm.CurrentEdit.ShortSu = 2;
			vm.CurrentEdit.EndFlag = 1;
			vm.CurrentEdit.Memo = "UAT 修正正常系";
		});
		dialogs.Clear();
		await d.RunAsync("1.修正:DoUpdate", vm => vm.DoUpdateCommand);
		var after1 = await LoadAsync(session, open1.Id);
		session.Check("1.修正:DBへ反映", after1 is { KakuteiDay: "20260906", JitsuSu: 3, ShortSu: 2, EndFlag: 1, Memo: "UAT 修正正常系" },
			new { after1?.KakuteiDay, after1?.JitsuSu, after1?.ShortSu, after1?.EndFlag, after1?.Memo });
		session.Check("1.修正:Vduが増える", after1 != null && before1 != null && after1.Vdu > before1.Vdu, new { before = before1?.Vdu, after = after1?.Vdu });
		var listRow1 = d.Vm.ListData.FirstOrDefault(x => x.Id == open1.Id);
		session.Check("1.修正:一覧行が更新", listRow1 is { KakuteiDay: "20260906", JitsuSu: 3, ShortSu: 2, EndFlag: 1 } && listRow1.Vdu == after1?.Vdu,
			new { listRow1?.KakuteiDay, listRow1?.JitsuSu, listRow1?.ShortSu, listRow1?.EndFlag, listRow1?.Vdu });
		session.Check("1.修正:Message", d.Vm.Message == $"修正しました (Id={open1.Id})", new { d.Vm.Message });
		session.Check("1.修正:確認ダイアログは修正確認の1回のみ", dialogs.Count == 1 && dialogs[0].Message.StartsWith("修正しますか"),
			new { dialogs = dialogs.Select(x => x.Message) });
		var reserve1 = await ReserveAsync(session, seeded);
		CheckReserveDelta(session, "1.修正:引当数が5減る", reserve0, reserve1, -5);
		await HaibunScreenScenario.CaptureAsync(session, d.View, screens, "05_Update_Done");

		// ---- 2. 整合警告（完了で Su=2 ≠ 実1+欠0） ----
		var before2 = await LoadAsync(session, open2.Id);
		Select(d, open2.Id, "2.整合警告:未完了行を選択");
		d.Input("2.整合警告:完了・実1・欠0", vm => {
			vm.CurrentEdit.EndFlag = 1;
			vm.CurrentEdit.JitsuSu = 1;
			vm.CurrentEdit.ShortSu = 0;
			vm.CurrentEdit.KakuteiDay = "20260906";
		});
		dialogs.Clear();
		answerMismatch = MessageBoxResult.No;
		await d.RunAsync("2.整合警告:DoUpdate(いいえ)", vm => vm.DoUpdateCommand);
		var afterNo = await LoadAsync(session, open2.Id);
		session.Check("2.整合警告:確認ダイアログが出る", dialogs.Any(x => x.Message.Contains("完了なのに") && x.Message.Contains("数量(2)")),
			new { dialogs = dialogs.Select(x => x.Message) });
		session.Check("2.整合警告:いいえで保存されない", afterNo != null && before2 != null && afterNo.Vdu == before2.Vdu && afterNo.EndFlag == 0,
			new { afterNo?.Vdu, afterNo?.EndFlag, afterNo?.JitsuSu });
		session.Check("2.整合警告:いいえで修正確認まで進まない", !dialogs.Any(x => x.Message.StartsWith("修正しますか")), new { dialogs = dialogs.Select(x => x.Message) });
		var reserve2a = await ReserveAsync(session, seeded);
		CheckReserveDelta(session, "2.整合警告:いいえで引当数不変", reserve1, reserve2a, 0);
		dialogs.Clear();
		answerMismatch = MessageBoxResult.Yes;
		await d.RunAsync("2.整合警告:DoUpdate(はい)", vm => vm.DoUpdateCommand);
		var afterYes = await LoadAsync(session, open2.Id);
		session.Check("2.整合警告:はいで保存される", afterYes is { EndFlag: 1, JitsuSu: 1, ShortSu: 0 } && afterYes.Vdu > before2!.Vdu,
			new { afterYes?.Vdu, afterYes?.EndFlag, afterYes?.JitsuSu, afterYes?.ShortSu });
		var reserve2b = await ReserveAsync(session, seeded);
		CheckReserveDelta(session, "2.整合警告:はいで引当数が2減る", reserve2a, reserve2b, -2);

		// ---- 3. 入力エラー（open4 を使う） ----
		var before3 = await LoadAsync(session, open4.Id);
		foreach (var (label, apply, expected) in new (string, Action<TranHaibun>, string)[] {
			("確定日7桁 2026131", x => x.KakuteiDay = "2026131", "確定日は空、または yyyyMMdd"),
			("確定日不正 20261399", x => x.KakuteiDay = "20261399", "確定日は空、または yyyyMMdd"),
			("納品日不正 20260230", x => x.NouhinDay = "20260230", "納品日は空、または yyyyMMdd"),
			("実数量マイナス", x => x.JitsuSu = -1, "実数量・欠品数は0以上"),
			("欠品数マイナス", x => x.ShortSu = -1, "実数量・欠品数は0以上"),
		}) {
			Select(d, open4.Id, $"3.入力エラー:{label}:選択");
			d.Input($"3.入力エラー:{label}", vm => apply(vm.CurrentEdit));
			dialogs.Clear();
			await d.RunAsync($"3.入力エラー:{label}:DoUpdate", vm => vm.DoUpdateCommand);
			var now = await LoadAsync(session, open4.Id);
			session.Check($"3.入力エラー:{label}:警告が出て保存されない",
				dialogs.Count == 1 && dialogs[0].Kind.Contains("Warning") && dialogs[0].Message.StartsWith(expected)
				&& d.Vm.Message.StartsWith(expected) && now?.Vdu == before3?.Vdu,
				new { dialogs = dialogs.Select(x => new { x.Kind, x.Message }), d.Vm.Message, vdu = now?.Vdu });
		}
		await HaibunScreenScenario.CaptureAsync(session, d.View, screens, "06_InputError");

		// ---- 4. 削除（open3 Su=3） ----
		var reserve4a = await ReserveAsync(session, seeded);
		Select(d, open3.Id, "4.削除:未完了行を選択");
		var count4 = d.Vm.ListData.Count;
		dialogs.Clear();
		await d.RunAsync("4.削除:DoDelete", vm => vm.DoDeleteCommand);
		var after4 = await LoadAsync(session, open3.Id);
		session.Check("4.削除:削除確認ダイアログ", dialogs.Any(x => x.Message.StartsWith("削除しますか") && x.Message.Contains("引当数を引き直します")),
			new { dialogs = dialogs.Select(x => x.Message) });
		session.Check("4.削除:DBから消える", after4 == null);
		session.Check("4.削除:一覧から消え件数が減る", d.Vm.ListData.All(x => x.Id != open3.Id) && d.Vm.ListData.Count == count4 - 1 && d.Vm.Count == count4 - 1,
			new { before = count4, after = d.Vm.ListData.Count, d.Vm.Count });
		session.Check("4.削除:Message", d.Vm.Message == $"削除しました (Id={open3.Id})", new { d.Vm.Message });
		var reserve4b = await ReserveAsync(session, seeded);
		CheckReserveDelta(session, "4.削除:引当数が3減る", reserve4a, reserve4b, -3);
		await HaibunScreenScenario.CaptureAsync(session, d.View, screens, "07_Delete_Done");

		// ---- 5. 競合（別経路で open4 を更新してから画面で修正） ----
		Select(d, open4.Id, "5.競合:行を選択（画面側は古いVdu）");
		var stale = d.Vm.CurrentEdit.Vdu;
		var latest = await LoadAsync(session, open4.Id) ?? throw new InvalidOperationException("open4がありません");
		latest.Memo = "UAT 別経路で更新";
		await session.UpdateAsync(latest);
		var other = await LoadAsync(session, open4.Id);
		session.Check("5.競合:別経路でVduが進む", other != null && other.Vdu > stale, new { stale, other = other?.Vdu });
		d.Input("5.競合:画面でメモ変更", vm => vm.CurrentEdit.Memo = "UAT 画面で更新");
		dialogs.Clear();
		await d.RunAsync("5.競合:DoUpdate", vm => vm.DoUpdateCommand);
		var after5 = await LoadAsync(session, open4.Id);
		session.Check("5.競合:競合エラー表示", dialogs.Any(x => x.Kind.Contains("Error") && x.Message.Contains("他端末で更新された")),
			new { dialogs = dialogs.Select(x => new { x.Kind, x.Message }) });
		session.Check("5.競合:保存されない", after5 is { Memo: "UAT 別経路で更新" } && after5.Vdu == other!.Vdu, new { after5?.Memo, after5?.Vdu });
		session.Check("5.競合:編集状態が破棄される", d.Vm.CurrentEdit.Id == 0, new { d.Vm.CurrentEdit.Id, d.Vm.Message });
		await HaibunScreenScenario.CaptureAsync(session, d.View, screens, "08_Concurrent");
		session.Note("ダイアログ記録(最後の手順)", dialogs.Select(x => new { x.Kind, x.Button, x.Message }));
	}

	static void Select(ViewDriver<HaibunDataMenteViewModel> d, long id, string name) {
		var row = d.Vm.ListData.First(x => x.Id == id);
		d.Input(name, vm => {
			vm.Current = row;
			vm.CurrentEdit = Common.CloneObject(row);
		}, new { id });
	}

	static async Task<TranHaibun?> LoadAsync(VmSession session, long id) =>
		(await session.QueryAsync<TranHaibun>("where Id=@0", id.ToString())).SingleOrDefault();

	/// <summary>倉庫+SKU の引当数。R=SummaryRealStock、M:yyyyMM=SummaryStock</summary>
	static async Task<Dictionary<string, int>> ReserveAsync(VmSession session, JuchuShippingSeeder.Result seeded) {
		var args = new[] { seeded.WarehouseId.ToString(), seeded.ShohinId.ToString(), seeded.Id_Col.ToString(), seeded.Id_Siz.ToString() };
		const string where = "where Id_Soko=@0 AND Id_Shohin=@1 AND Id_Col=@2 AND Id_Siz=@3";
		var real = await session.QueryAsync<SummaryRealStock>(where, args);
		var monthly = await session.QueryAsync<SummaryStock>(where + " order by SumMonth", args);
		var result = new Dictionary<string, int>();
		foreach (var r in real) result["R"] = (int)r.ReserveQty;
		foreach (var m in monthly) result[$"M:{m.SumMonth}"] = (int)m.ReserveQty;
		return result;
	}

	/// <summary>SummaryRealStock は必ず、SummaryStock は変化した月がすべて expected だけ動くこと</summary>
	static void CheckReserveDelta(VmSession session, string name, Dictionary<string, int> before, Dictionary<string, int> after, int expected) {
		int Get(Dictionary<string, int> x, string k) => x.TryGetValue(k, out var v) ? v : 0;
		var keys = before.Keys.Union(after.Keys).OrderBy(x => x).ToList();
		var deltas = keys.ToDictionary(k => k, k => Get(after, k) - Get(before, k));
		var realOk = after.ContainsKey("R") && deltas["R"] == expected;
		var monthKeys = keys.Where(k => k.StartsWith("M:")).ToList();
		var monthOk = expected == 0
			? monthKeys.All(k => deltas[k] == 0)
			: monthKeys.Any(k => deltas[k] == expected) && monthKeys.All(k => deltas[k] == 0 || deltas[k] == expected);
		session.Check(name, realOk && monthOk, new { before, after, deltas });
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
