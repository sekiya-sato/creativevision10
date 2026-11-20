using System.Text;
using System.Windows;
using CvWpfclient;
using CvWpfclient.Models;
using CvWpfclient.ViewModels;
using CvWpfclient.Views._01Master;

namespace UatVm.Scenarios;

/// <summary>
/// メニュー起動前のログイン確認（MainMenuViewModel.EnsureLoginForMenuAsync）を確認する。
/// <para>
/// 認証ありのサーバ（--expect-auth on）: 未ログインは「ログインしていません」、JWT期限切れは「ログイン期限が切れています」で止め、画面を開かない。
/// 期限内のJWTがあれば確認を通す（署名の正否はサーバが判定するのでここでは見ない）。
/// 認証を解除したUAT用サーバ（--expect-auth off）: 未ログイン・期限切れでも止めずに画面を開く。
/// 更新操作はしない。
/// </para>
/// </summary>
public static class LoginGuardScenario {
	const string NotLoggedInMessage = "ログインしていません";
	const string ExpiredMessage = "ログイン期限が切れています";

	public static async Task RunAsync(VmSession session) {
		var expectAuth = ReadExpectAuth();
		var originalJwt = AppGlobal.LoginJwt;
		try {
			AppGlobal.ClearServerAuthMode();
			var authRequired = await AppGlobal.GetServerAuthRequiredAsync(CancellationToken.None);
			session.Note("サーバの認証モード", new { authRequired, expectAuth });
			if (expectAuth is bool expected) {
				session.CheckEqual("サーバの認証モードが期待どおり", expected, authRequired);
			}

			var vm = new MainMenuViewModel();
			var menu = Leaves(MenuData.CreateDefault()).First(x => x.ViewType == typeof(MasterShainMenteView));

			AppGlobal.ClearLoginJwt();
			session.CheckEqual("JWTなしは未ログイン", EnumClientLoginState.NotLoggedIn, AppGlobal.GetLoginState());
			await RunMenuAsync(session, vm, menu, "未ログイン", authRequired ? NotLoggedInMessage : null);

			AppGlobal.SetLoginJwt(CreateUnsignedJwt(DateTimeOffset.UtcNow.AddMinutes(-5)));
			session.CheckEqual("期限切れJWTは期限切れ", EnumClientLoginState.Expired, AppGlobal.GetLoginState());
			await RunMenuAsync(session, vm, menu, "期限切れ", authRequired ? ExpiredMessage : null);

			AppGlobal.SetLoginJwt("not-a-jwt");
			session.CheckEqual("読めないJWTは未ログイン", EnumClientLoginState.NotLoggedIn, AppGlobal.GetLoginState());

			AppGlobal.SetLoginJwt(CreateUnsignedJwt(null));
			session.CheckEqual("expの無いJWTは未ログイン", EnumClientLoginState.NotLoggedIn, AppGlobal.GetLoginState());

			// 期限内: クライアントの確認は通る。署名不正のためサーバ認証ありなら画面側で401になるが、それは従来の経路
			AppGlobal.SetLoginJwt(CreateUnsignedJwt(DateTimeOffset.UtcNow.AddHours(1)));
			session.CheckEqual("期限内JWTはログイン済み", EnumClientLoginState.LoggedIn, AppGlobal.GetLoginState());
			session.ClearDialogs();
			vm.SelectedMenu = menu;
			await vm.DoMenuCommand.ExecuteAsync(null);
			await Idle();
			session.Check("期限内: ログイン確認のメッセージは出ない",
				!session.Dialogs.Any(x => x.Request.Message.Contains(NotLoggedInMessage) || x.Request.Message.Contains(ExpiredMessage)),
				session.Dialogs.Select(x => x.Request.Message));
			CloseMenuViews();
		}
		finally {
			AppGlobal.SetLoginJwt(originalJwt);
			CloseMenuViews();
		}
	}

	static async Task RunMenuAsync(VmSession session, MainMenuViewModel vm, MenuData menu, string label, string? expectedMessage) {
		session.ClearDialogs();
		vm.SelectedMenu = menu;
		await vm.DoMenuCommand.ExecuteAsync(null);
		await Idle();
		var messages = session.Dialogs.Select(x => x.Request.Message).ToList();
		var opened = Application.Current.Windows.OfType<MasterShainMenteView>().Any();
		if (expectedMessage != null) {
			session.Check($"{label}: 「{expectedMessage}」で止める", messages.Any(m => m.Contains(expectedMessage)), messages);
			session.Check($"{label}: 画面を開かない", !opened);
		}
		else {
			session.Check($"{label}: ログイン確認のメッセージは出ない",
				!messages.Any(m => m.Contains(NotLoggedInMessage) || m.Contains(ExpiredMessage)), messages);
			session.Check($"{label}: 画面を開く", opened);
		}
		CloseMenuViews();
	}

	static void CloseMenuViews() {
		foreach (var w in Application.Current.Windows.OfType<MasterShainMenteView>().ToList()) {
			try { w.Close(); }
			catch (InvalidOperationException) { /* 既に閉じている */ }
		}
	}

	static async Task Idle() {
		// 画面の初期化（InitCommand）を一巡させる
		for (var i = 0; i < 20; i++) {
			await Task.Delay(100);
		}
	}

	/// <summary>--expect-auth on|off を読む。指定なしは判定しない</summary>
	static bool? ReadExpectAuth() {
		var args = Environment.GetCommandLineArgs();
		var i = Array.FindIndex(args, a => string.Equals(a, "--expect-auth", StringComparison.OrdinalIgnoreCase));
		if (i < 0 || i + 1 >= args.Length) return null;
		return args[i + 1].ToLowerInvariant() switch {
			"on" => true,
			"off" => false,
			_ => null,
		};
	}

	/// <summary>署名なしのJWT（alg=none）。exp=null なら exp を入れない</summary>
	static string CreateUnsignedJwt(DateTimeOffset? exp) {
		var header = Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}");
		var payload = Base64Url(exp is { } e
			? $"{{\"name\":\"uat-loginguard\",\"exp\":{e.ToUnixTimeSeconds()}}}"
			: "{\"name\":\"uat-loginguard\"}");
		return $"{header}.{payload}.";
	}

	static string Base64Url(string json) =>
		Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	static IEnumerable<MenuData> Leaves(IEnumerable<MenuData> items) =>
		items.SelectMany(x => x.SubItems is { Count: > 0 } sub ? Leaves(sub) : [x]);
}
