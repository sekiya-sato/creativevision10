using System.Diagnostics;
using CvBase;
using CvWpfclient;
using CvWpfclient.Helpers;
using CvWpfclient.Services;
using CvWpfclient.Views._00System;

namespace UatVm.Scenarios;

/// <summary>
/// 操作履歴（Doc/spec/2026-10-20_メニュー利用履歴_最小変更設計案.md）を実View→gRPC→DBで確認する。
/// <para>
/// MainMenuViewModel.DoMenu と同じ <see cref="OperationLogClient.Track"/> で2画面を開き、
/// 照会・画面切替・終了が同じ画面セッションに Kind 1,3,…,2 の順で並ぶこと、
/// 2画面目の照会が2画面目に、再アクティブ化後の照会が1画面目に付くことを確認する。
/// 専用DB（開発DBの複製など）と専用サーバで実行する。UATは認証を外すため利用者（Id_Shain）は0になる。
/// 環境変数 OperationLog__Level=0 でサーバを起動した場合は記録の確認を省き、照会の応答時間だけを測る。
/// </para>
/// </summary>
public static class OperationLogScenario {
	const string FunctionA = "00System.SysLoginHistory";
	const string FunctionB = "00System.SysAutoExecHistory";
	const int MeasureCount = 30;

	public static async Task RunAsync(VmSession session) {
		var measureOnly = Environment.GetEnvironmentVariable("OperationLog__Level") == "0";
		var originalJwt = AppGlobal.LoginJwt;
		// 起動・終了の送信は未ログインでは行わない。UATは認証を外しているので仮のトークンで送る
		if (string.IsNullOrEmpty(originalJwt)) {
			AppGlobal.SetLoginJwt("uat-operation-log");
		}
		try {
			var marker = $"oplog-{DateTime.Now:HHmmssfff}";
			var a = session.OpenView<SysLoginHistoryView, BaseViewModel>();
			OperationLogClient.Track(a.View, FunctionA, "■ UAT › ログイン履歴情報", "{\"initParam\":0}");
			await session.QueryAsync<SysLogin>("SELECT * FROM SysLogin WHERE LoginId = @0", marker);
			await session.QueryAsync<SysLogin>("SELECT * FROM SysLogin WHERE LoginId <> @0", marker);

			var b = session.OpenView<SysAutoExecHistoryView, BaseViewModel>();
			OperationLogClient.Track(b.View, FunctionB, "■ UAT › 自動実行履歴", string.Empty);
			b.View.Activate();
			await Pump();
			await session.QueryAsync<SysHistAutoexec>("SELECT * FROM SysHistAutoexec WHERE TaskName = @0", marker);

			a.View.Activate();
			await Pump();
			await session.QueryAsync<SysLogin>("SELECT * FROM SysLogin WHERE LoginId = @0", marker + "-reactivated");

			// 応答時間（記録あり／なしを別起動で比べる）
			var sw = Stopwatch.StartNew();
			for (var i = 0; i < MeasureCount; i++) {
				await session.QueryAsync<SysLogin>("SELECT * FROM SysLogin WHERE LoginId = @0", $"{marker}-m{i}");
			}
			sw.Stop();
			session.Note("照会の平均応答時間", new { level = measureOnly ? 0 : 3, count = MeasureCount, averageMs = sw.Elapsed.TotalMilliseconds / MeasureCount });

			session.CloseViews();
			// 終了の送信は待たずに行うので、記録されるまで待つ
			await Task.Delay(1500);
			if (measureOnly) {
				return;
			}

			var rows = await session.QueryAsync<SysHistOperation>(
				"SELECT * FROM SysHistOperation WHERE FunctionId IN (@0, @1) AND OpTime >= @2 ORDER BY Id",
				FunctionA, FunctionB, DateTime.Now.AddMinutes(-10).ToString("yyyyMMddHHmmssfff"));
			var sessionA = rows.LastOrDefault(r => r.FunctionId == FunctionA && r.Kind == OperationKind.MenuOpen)?.ScreenSession ?? "";
			var sessionB = rows.LastOrDefault(r => r.FunctionId == FunctionB && r.Kind == OperationKind.MenuOpen)?.ScreenSession ?? "";
			var rowsA = rows.Where(r => r.ScreenSession == sessionA).ToList();
			var rowsB = rows.Where(r => r.ScreenSession == sessionB).ToList();
			session.Note("画面1の記録", rowsA.Select(r => new { r.OpTime, r.Kind, r.Op, r.DataType, r.Condition, r.ResultCount, r.ElapsedMs, r.ClientId, r.Ip, r.MenuPath }));
			session.Note("画面2の記録", rowsB.Select(r => new { r.OpTime, r.Kind, r.Op, r.DataType, r.Condition, r.ResultCount, r.ElapsedMs }));

			session.Check("画面1・画面2で別の画面セッション", sessionA.Length == 36 && sessionB.Length == 36 && sessionA != sessionB, new { sessionA, sessionB });
			var kindsA = rowsA.Select(r => r.Kind).ToArray();
			session.Check("画面1は 起動→照会2件→(再アクティブ後)照会→計測照会→終了 の順",
				kindsA.Length >= 3 + 1 + MeasureCount + 1 && kindsA[0] == OperationKind.MenuOpen && kindsA[^1] == OperationKind.MenuClose
					&& kindsA[1..^1].All(k => k == OperationKind.Query), kindsA);
			session.Check("画面1の起動行にメニュー階層と起動条件", rowsA.FirstOrDefault()?.MenuPath == "■ UAT › ログイン履歴情報" && rowsA[0].Condition == "{\"initParam\":0}", rowsA.FirstOrDefault()?.Condition);
			var firstQuery = rowsA.FirstOrDefault(r => r.Kind == OperationKind.Query && r.Condition.Contains(marker));
			session.Check("画面1の照会に条件（SQLとパラメータ）", firstQuery?.Condition.Contains("SysLogin") == true && firstQuery.DataType == nameof(QueryListSqlParam), firstQuery?.Condition);
			session.Check("再アクティブ後の照会は画面1に付く", rowsA.Any(r => r.Condition.Contains(marker + "-reactivated")));
			var kindsB = rowsB.Select(r => r.Kind).ToArray();
			// 画面2は初期表示で自身の照会も出すので、起動で始まり終了で終わり、間は照会だけであることを見る
			session.Check("画面2は 起動→照会(初期表示を含む)→終了", kindsB.Length >= 3 && kindsB[0] == OperationKind.MenuOpen && kindsB[^1] == OperationKind.MenuClose
				&& kindsB[1..^1].All(k => k == OperationKind.Query), kindsB);
			session.Check("画面2の照会は画面2に付く", rowsB.Any(r => r.Kind == OperationKind.Query && r.Condition.Contains(marker)));
			session.Check("端末Id・IPがサーバに届く", rowsA.All(r => r.ClientId.Length == 36 && r.Ip.Length > 0), rowsA.FirstOrDefault()?.ClientId);
			session.Check("照会の件数を記録（0件は0）", rowsA.Where(r => r.Condition.Contains(marker + "-reactivated")).All(r => r.ResultCount == 0));
		}
		finally {
			AppGlobal.SetLoginJwt(originalJwt);
		}
	}

	/// <summary>Activated イベントを処理させる</summary>
	static Task Pump() => System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle).Task;
}
