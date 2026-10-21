using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using CodeShare;
using CvBase;
using CvBase.Share;
using CvWpfclient;
using CvWpfclient.Models;
using CvWpfclient.Services;
using CvWpfclient.ViewModels;
using CvWpfclient.ViewModels._00System;
using CvWpfclient.Views._00System;

namespace UatVm.Scenarios;

/// <summary>
/// メニュー実行権限と個人メニュー（Doc/spec/2026-10-20_メニュー実行権限と個人メニュー_最小変更設計案.md）を実View→gRPC→DBで確認する。
/// <para>
/// 1. 権限プロファイル設定画面で新規プロファイルを作り、フォルダ一括許可・個別禁止・連動を保存して明細を読み戻す。
/// 2. FullAccess は明細を変更できない。
/// 3. 既定ポリシー Audit / Deny それぞれで、MainMenuViewModel がログイン時に作るメニューの絞り込み・空フォルダ除去・起動時の再確認を見る。
/// 4. お気に入りの追加・解除と、権限で使えないメニューがお気に入りに出ないことを見る。
/// 既定ポリシーと権限プロファイルを書き換えるので、専用DB（開発DBの複製）と専用サーバで実行する。終了時に既定ポリシーは元へ戻す。
/// </para>
/// </summary>
public static class MenuPermissionScenario {
	const string ShopFolder = "■ 店舗業務";
	const string DeniedFunction = "40Shop.SalesQuickReportCostless";
	const string ZaikoQuery = "08Zaiko.ZaikoQuery";
	const string ShopUriageInput = "06Uriage.ShopUriageInput";

	public static async Task RunAsync(VmSession session) {
		var jpgDir = Path.Combine(Path.GetDirectoryName(session.Evidence.FilePath)!, $"menuperm-{DateTime.Now:yyyyMMdd-HHmmss}");
		Directory.CreateDirectory(jpgDir);
		MenuFavoriteStore.Directory = Path.Combine(jpgDir, "favorites");
		var sysman = (await session.QueryAsync<MasterSysman>("SELECT * FROM MasterSysman")).First();
		var originalMode = sysman.PermissionDefaultMode;
		try {
			var profileId = await AdminScreenAsync(session, jpgDir);
			await FullAccessAsync(session);
			await MainMenuAsync(session, profileId, EnumPermissionDefaultMode.Audit);
			await SetModeAsync(session, EnumPermissionDefaultMode.Deny);
			await MainMenuAsync(session, profileId, EnumPermissionDefaultMode.Deny);
		}
		finally {
			await SetModeAsync(session, (EnumPermissionDefaultMode)originalMode);
		}
	}

	/// <summary>権限プロファイル設定で新規プロファイルを作る。店舗業務フォルダを一括許可し、1件だけ禁止する</summary>
	static async Task<long> AdminScreenAsync(VmSession session, string jpgDir) {
		session.SetDialogResponder(VmSession.StrictResponder);
		var d = session.OpenView<SysPermissionProfileMenteView, SysPermissionProfileMenteViewModel>();
		await d.WaitAsync("初期化", vm => vm.Profiles.Count > 0 && vm.MenuRows.Count > 0 && !vm.IsBusy);
		session.Check("メニュー全項目が一覧に出る", d.Vm.MenuRows.Count(r => !r.IsFolder) == CountLeaves(MenuData.CreateDefault()),
			new { rows = d.Vm.MenuRows.Count(r => !r.IsFolder) });
		SaveJpeg(d.View, jpgDir, "permission_std");
		// 行の生成・再利用で設定欄が値を書き戻さないこと（読込直後とスクロール後に未保存にならない）
		var settingsBefore = d.Vm.MenuRows.Select(r => r.Setting).ToArray();
		if (FindDescendant<System.Windows.Controls.DataGrid>(d.View, g => ReferenceEquals(g.ItemsSource, d.Vm.MenuRows)) is { } grid) {
			grid.ScrollIntoView(d.Vm.MenuRows[^1]);
			await Pump();
			grid.ScrollIntoView(d.Vm.MenuRows[0]);
			await Pump();
		}
		session.Check("読込直後・スクロール後も未保存にならず設定も変わらない", !d.Vm.IsDirty && d.Vm.MenuRows.Select(r => r.Setting).SequenceEqual(settingsBefore));

		await d.RunAsync("新規", vm => vm.NewProfileCommand);
		var code = $"Uat{DateTime.Now:MMddHHmmss}";
		d.Input("コード・名称", vm => { vm.EditProfile!.Code = code; vm.EditProfile.Name = "UAT店舗スタッフ"; }, code);
		var folder = d.Vm.MenuRows.First(r => r.IsFolder && r.Header == ShopFolder);
		d.Input("店舗業務フォルダを選択", vm => vm.SelectedRow = folder, folder.Header);
		d.Run("一括許可", vm => vm.SetAllowCommand);
		session.Check("フォルダ配下が全件許可", folder.Leaves.Count > 0 && folder.Leaves.All(r => r.Setting == PermissionMenuRow.Allow), folder.Leaves.Count);
		var deny = d.Vm.MenuRows.First(r => r.FunctionId == DeniedFunction);
		d.Input("1件だけ禁止", vm => deny.Setting = PermissionMenuRow.Deny, DeniedFunction);
		var zaikoRows = d.Vm.MenuRows.Where(r => r.FunctionId == ZaikoQuery).ToList();
		var denyRows = d.Vm.MenuRows.Where(r => r.FunctionId == DeniedFunction).ToList();
		// 禁止はショートカット側の1行だけに入れたので、標準メニュー側の行が連動していれば連動の確認になる
		session.Check("同じ機能Idの行は連動する（ショートカットと標準メニュー）", zaikoRows.Count >= 2 && zaikoRows.All(r => r.Setting == PermissionMenuRow.Allow)
			&& denyRows.Count == 2 && denyRows.All(r => r.Setting == PermissionMenuRow.Deny), new { zaiko = zaikoRows.Select(r => r.Setting), deny = denyRows.Select(r => r.Setting) });
		session.Check("結果列: 許可=可 禁止=不可 既定=可（既定）", deny.ResultText == "不可" && zaikoRows[0].ResultText == "可"
			&& d.Vm.MenuRows.First(r => !r.IsFolder && r.Setting == PermissionMenuRow.Default).ResultText == "可（既定）");

		await d.RunAsync("保存", vm => vm.SaveCommand);
		await d.WaitAsync("保存後の再読込", vm => !vm.IsBusy && !vm.IsDirty && vm.EditProfile?.Code == code);
		var saved = (await session.QueryAsync<SysPermissionProfile>("SELECT * FROM SysPermissionProfile WHERE Code = @0", code)).Single();
		var details = await session.QueryAsync<SysPermissionProfileDetail>(
			$"SELECT * FROM SysPermissionProfileDetail WHERE Id_PermissionProfile = {saved.Id} ORDER BY FunctionId");
		var expectedAllow = folder.Leaves.Select(r => r.FunctionId!).Distinct().Count() - 1;
		session.Check("明細はView・重複なしで保存", details.All(x => x.PermissionType == (int)EnumPermissionType.View)
			&& details.Count == expectedAllow + 1 && details.Count(x => !x.IsAllowed) == 1 && details.Single(x => !x.IsAllowed).FunctionId == DeniedFunction,
			details.Select(x => new { x.FunctionId, x.IsAllowed }));

		// 未保存の変更があるときにプロファイルを切り替えると確認が出て、Noなら元のまま
		d.Input("未保存の変更", vm => vm.MenuRows.First(r => r.FunctionId == ZaikoQuery).Setting = PermissionMenuRow.Deny);
		session.ClearDialogs();
		d.Input("別プロファイルを選択", vm => vm.SelectedProfile = vm.Profiles.First(p => p.Id == 2));
		await Pump();
		session.Check("破棄確認が出てNoで編集を保持", session.Dialogs.Any(x => x.Request.Message.Contains("破棄")) && d.Vm.EditProfile?.Code == code && d.Vm.IsDirty);

		// 未保存のまま閉じようとすると確認が出て、Noなら閉じない・Yesなら閉じる
		session.ClearDialogs();
		d.View.Close();
		await Pump();
		session.Check("未保存で閉じると確認が出てNoなら閉じない", d.View.IsVisible && session.Dialogs.Any(x => x.Request.Message.Contains("破棄")));
		session.SetDialogResponder(r => r.Button == MessageBoxButton.YesNo && r.Message.Contains("破棄") ? MessageBoxResult.Yes : VmSession.StrictResponder(r));
		d.View.Close();
		await Pump();
		session.Check("Yesなら閉じる", !d.View.IsVisible);
		session.SetDialogResponder(VmSession.StrictResponder);
		session.CloseViews();
		return saved.Id;
	}

	/// <summary>FullAccess は明細を変えられない（画面は編集不可、サーバも拒否）</summary>
	static async Task FullAccessAsync(VmSession session) {
		var d = session.OpenView<SysPermissionProfileMenteView, SysPermissionProfileMenteViewModel>();
		await d.WaitAsync("初期化", vm => vm.Profiles.Count > 0 && vm.MenuRows.Count > 0 && !vm.IsBusy);
		d.Input("FullAccessを選択", vm => vm.SelectedProfile = vm.Profiles.First(p => p.Id == SysPermissionProfile.FullAccessId));
		await d.WaitAsync("読込", vm => vm.EditProfile?.Id == SysPermissionProfile.FullAccessId && !vm.IsBusy);
		session.Check("FullAccessはメニュー権限を編集できない", d.Vm.IsFullAccess && !d.Vm.CanEditRules);
		var reply = await AppGlobal.GetGrpcService<ICoreService>().QueryMsgAsync(new CvMsg {
			Flag = CvFlag.Msg201_Op_Execute,
			DataType = typeof(PermissionProfileSaveParam),
			DataMsg = CvAsset.Common.SerializeObject(new PermissionProfileSaveParam(d.Vm.EditProfile!,
				[new SysPermissionProfileDetail { FunctionId = ZaikoQuery, IsAllowed = false }])),
		}, AppGlobal.GetDefaultCallContext());
		session.CheckEqual("サーバもFullAccessの明細変更を拒否", CvMsgErrorCode.InvalidParameter, reply.Code);
		session.CloseViews();
	}

	/// <summary>ログイン後のメインメニュー（絞り込み・起動時確認・お気に入り）</summary>
	static async Task MainMenuAsync(VmSession session, long profileId, EnumPermissionDefaultMode mode) {
		var label = mode.ToString();
		session.SetDialogResponder(VmSession.StrictResponder);
		var vm = new MainMenuViewModel();
		var login = new LoginViewModel { LoginId = $"uat-menuperm-{label}", LoginData = new LoginReply { JwtMessage = "", PermissionProfileId = profileId } };
		var update = typeof(MainMenuViewModel).GetMethod("UpdateMenuForLoginAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
		await (Task)update.Invoke(vm, [login])!;

		var all = CountLeaves(MenuData.CreateDefault());
		var shown = CountLeaves(vm.MenuItems);
		var shownIds = Leaves(vm.MenuItems).Select(x => x.FunctionId).ToHashSet();
		session.Note($"{label}: 表示メニュー", new { all, shown, topFolders = vm.MenuItems.Select(x => x.Header) });
		session.Check($"{label}: 禁止したメニューは出ない", !shownIds.Contains(DeniedFunction));
		session.Check($"{label}: 許可したメニューは出る", shownIds.Contains(ZaikoQuery));
		session.Check($"{label}: 空のフォルダは出ない", AllFolders(vm.MenuItems).All(f => f.SubItems!.Count > 0));
		if (mode == EnumPermissionDefaultMode.Deny) {
			session.Check("Deny: 許可した店舗業務の画面と、それと同じ機能Idの標準メニューだけが出る", shown < all && Leaves(vm.MenuItems).All(x => x.FunctionId != null),
				new { all, shown });
			session.Check("Deny: 権限プロファイル設定も出ない（FullAccess以外）", !shownIds.Contains("00System.SysPermissionProfileMente"));
		}
		else {
			session.CheckEqual("Audit: 禁止した1件（2か所）以外はすべて出る", all - 2, shown);
		}

		// 表示されていないメニューへ到達しても開かない
		session.ClearDialogs();
		var denied = Leaves(MenuData.CreateDefault()).First(x => x.FunctionId == DeniedFunction);
		vm.SelectedMenu = denied;
		await vm.DoMenuCommand.ExecuteAsync(null);
		session.Check($"{label}: 禁止メニューは起動時に止める", session.Dialogs.Any(x => x.Request.Message.Contains("権限がありません")),
			session.Dialogs.Select(x => x.Request.Message));

		// お気に入り: 追加 → 先頭に出る → 外す → 消える
		var target = Leaves(vm.MenuItems).First(x => x.FunctionId == ZaikoQuery && x.Header == "在庫問合せ");
		vm.SelectedMenu = target;
		session.Check($"{label}: 追加できる", vm.AddFavoriteCommand.CanExecute(null));
		vm.AddFavoriteCommand.Execute(null);
		session.Check($"{label}: お気に入りが先頭に出る", vm.MenuItems[0].Header == MenuData.FavoriteHeader
			&& vm.MenuItems[0].SubItems!.Single().FunctionId == ZaikoQuery, vm.MenuItems[0].Header);
		session.Check($"{label}: 二重追加はできない", !vm.AddFavoriteCommand.CanExecute(null));
		var stored = MenuFavoriteStore.Load(login.LoginId!);
		session.Check($"{label}: お気に入りはログインID別ファイルへ保存", stored.SequenceEqual([ZaikoQuery]), MenuFavoriteStore.GetFilePath(login.LoginId!));
		session.Check($"{label}: 初めて出たお気に入りフォルダは開いている", vm.MenuItems[0].IsExpanded);
		vm.MenuItems[0].IsExpanded = false;
		vm.SelectedMenu = Leaves(vm.MenuItems).First(x => x.FunctionId == ShopUriageInput && !x.IsFavoriteEntry);
		vm.AddFavoriteCommand.Execute(null);
		session.Check($"{label}: 追加でフォルダを作り直しても閉じた状態を保つ", !vm.MenuItems[0].IsExpanded && vm.MenuItems[0].SubItems!.Count == 2);

		// 権限で使えないメニューはお気に入りに登録されていても出ない（ファイルを直接書いて再ログイン）
		MenuFavoriteStore.Save(login.LoginId!, [DeniedFunction, ZaikoQuery]);
		await (Task)update.Invoke(vm, [login])!;
		session.Check($"{label}: 使えないお気に入りは出さない", vm.MenuItems[0].Header == MenuData.FavoriteHeader
			&& vm.MenuItems[0].SubItems!.Select(x => x.FunctionId).SequenceEqual([ZaikoQuery]));

		vm.SelectedMenu = vm.MenuItems[0].SubItems![0];
		session.Check($"{label}: お気に入り項目は外せる", vm.RemoveFavoriteCommand.CanExecute(null) && !vm.AddFavoriteCommand.CanExecute(null));
		vm.RemoveFavoriteCommand.Execute(null);
		session.Check($"{label}: 外すとお気に入りフォルダが消える", vm.MenuItems[0].Header != MenuData.FavoriteHeader);
		session.Check($"{label}: 外しても使えないお気に入りの登録は残す", MenuFavoriteStore.Load(login.LoginId!).SequenceEqual([DeniedFunction]));

		// トークン更新(F11)の画面は設定ファイルのIDを出すが、お気に入りはログインしたIDのまま
		var refresh = new LoginViewModel { InitParam = 1, LoginId = $"uat-menuperm-other-{label}", LoginData = new LoginReply { JwtMessage = "", PermissionProfileId = profileId } };
		await (Task)update.Invoke(vm, [refresh])!;
		vm.SelectedMenu = Leaves(vm.MenuItems).First(x => x.FunctionId == ZaikoQuery);
		vm.AddFavoriteCommand.Execute(null);
		session.Check($"{label}: トークン更新後もログインしたIDのファイルへ保存", MenuFavoriteStore.Load(login.LoginId!).SequenceEqual([DeniedFunction, ZaikoQuery])
			&& !File.Exists(MenuFavoriteStore.GetFilePath(refresh.LoginId!)));

		// 壊れたファイルは上書きしない
		File.WriteAllText(MenuFavoriteStore.GetFilePath(login.LoginId!), "{broken");
		session.ClearDialogs();
		await (Task)update.Invoke(vm, [login])!;
		vm.SelectedMenu = Leaves(vm.MenuItems).First(x => x.FunctionId == ZaikoQuery);
		session.Check($"{label}: 壊れたお気に入りは通知して追加・解除を止める", session.Dialogs.Any(x => x.Request.Message.Contains("お気に入りを読み込めません"))
			&& !vm.AddFavoriteCommand.CanExecute(null) && File.ReadAllText(MenuFavoriteStore.GetFilePath(login.LoginId!)) == "{broken");
		vm.Dispose();
	}

	static async Task SetModeAsync(VmSession session, EnumPermissionDefaultMode mode) {
		var sysman = (await session.QueryAsync<MasterSysman>("SELECT * FROM MasterSysman")).First();
		if (sysman.PermissionDefaultMode == (int)mode) return;
		sysman.PermissionDefaultMode = (int)mode;
		await session.UpdateAsync(sysman);
		session.Note("既定ポリシーを変更", mode.ToString());
	}

	static IEnumerable<MenuData> Leaves(IEnumerable<MenuData> nodes) =>
		nodes.SelectMany(n => n.SubItems != null ? Leaves(n.SubItems) : n.IsExecutable ? [n] : []);

	static IEnumerable<MenuData> AllFolders(IEnumerable<MenuData> nodes) =>
		nodes.Where(n => n.SubItems != null).SelectMany(n => AllFolders(n.SubItems!).Prepend(n));

	static int CountLeaves(IEnumerable<MenuData> nodes) => Leaves(nodes).Count();

	static void SaveJpeg(Window view, string dir, string name) {
		view.UpdateLayout();
		ScreenLayoutCheck.SaveJpeg(view, dir, name);
	}

	static T? FindDescendant<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject {
		for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++) {
			var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
			if (child is T found && match(found)) return found;
			if (FindDescendant(child, match) is T nested) return nested;
		}
		return null;
	}

	static Task Pump() => Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
}
