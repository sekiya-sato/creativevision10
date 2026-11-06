using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.ViewModels._23AiQuery;
using CvWpfclient.Views._23AiQuery;

namespace UatVm.Scenarios;

/// <summary>
/// AI照会（CSV）を接続先DB（SQLite・PostgreSQL・MariaDB）ごとに確認する。サーバDBは更新しない（シーダーなし）。
/// <para>
/// CvServer の接続先は環境変数 Database__Provider（Sqlite / Postgres / MariaDb）で切り替える（管理対象サーバへ引き継がれる）。
/// SQLite方言の固定SQL（json_extract・ifnull・json_each・WITH・JOIN・CAST・パラメータ）を個人保存から開き、
/// Msg092 検査 → Msg101 実行 が接続先で通ることを見る。DBごとにデータが異なるため件数・値は照合しない。
/// 更新・システム表・システムスキーマ・副作用関数・コメント記法のSQLは検査で拒否されることを見る。
/// CV_AIQUERY_LIVE=1 のときだけ、サーバ設定のLLMで会話 → SQL作成 → 実行まで確認する（件数は問わない）。
/// 個人保存は利用者の実ファイルを使わず、出力フォルダ内の personal.json へ差し替える。共有保存はしない。
/// </para>
/// </summary>
public static class AiQueryDbScenario {
	static readonly string Provider = Environment.GetEnvironmentVariable("Database__Provider") is { Length: > 0 } p ? p : "Sqlite";

	static AiQueryParam Date(string name, string label, string value) => new() { Name = name, Label = label, Type = EnumAiQueryParamType.Date, Value = value };

	/// <summary>SQLite方言の固定SQL。(タイトル, SQL, パラメータ, 検査結果に含まれるべき参照表)</summary>
	static readonly (string Title, string Sql, List<AiQueryParam> Params, string[] Tables)[] Queries = [
		("最終計上日", """
			SELECT max(h.DenDay) AS 最終計上日, COUNT(*) AS 伝票数
			  FROM Tran01Tenuri h
			""", [], ["Tran01Tenuri"]),
		("店舗別純売上", """
			SELECT ifnull(json_extract(h.VTenpo,'$.Cd'),'') AS 店舗コード,
			       ifnull(json_extract(h.VTenpo,'$.Mei'),'') AS 店舗名,
			       SUM(CASE WHEN h.CalcFlag = 1 THEN 1 ELSE 0 END) AS 伝票数,
			       SUM(h.CalcFlag * h.KingakuTotal) AS 純売上
			  FROM Tran01Tenuri h
			 WHERE h.DenDay BETWEEN @0 AND @1
			 GROUP BY 1, 2
			 ORDER BY 1
			""", [Date("from", "期間開始", "20000101"), Date("to", "期間終了", "20991231")], ["Tran01Tenuri"]),
		("年月・区分別（CAST数値パラメータ）", """
			SELECT substr(h.DenDay,1,6) AS 年月, h.Kubun AS 区分, COUNT(*) AS 件数, SUM(h.CalcFlag * h.KingakuTotal) AS 純額
			  FROM Tran01Tenuri h
			 WHERE h.DenDay >= @0 AND h.Kubun >= CAST(@1 AS INTEGER)
			 GROUP BY substr(h.DenDay,1,6), h.Kubun
			 ORDER BY 1 DESC, 2
			 LIMIT 50
			""", [Date("from", "期間開始", "20000101"), new() { Name = "kubun", Label = "区分下限", Type = EnumAiQueryParamType.Integer, Value = "10" }], ["Tran01Tenuri"]),
		("明細展開: 商品別純数量（直近月）", """
			SELECT ifnull(json_extract(m.value,'$.Code_Shohin'),'') AS 商品コード,
			       SUM(h.CalcFlag * CAST(ifnull(json_extract(m.value,'$.Su'),0) AS INTEGER)) AS 純数量,
			       SUM(h.CalcFlag * CAST(ifnull(json_extract(m.value,'$.Kingaku'),0) AS INTEGER)) AS 純金額
			  FROM Tran01Tenuri h, json_each(h.Jmeisai) m
			 WHERE json_valid(h.Jmeisai) AND h.DenDay BETWEEN @0 AND @1
			 GROUP BY 1
			 ORDER BY 2 DESC
			 LIMIT 20
			""", [Date("from", "期間開始", "20000101"), Date("to", "期間終了", "20991231")], ["Tran01Tenuri"]),
		("WITH と マスタ結合", """
			WITH s AS (
			  SELECT h.Id_Tenpo, COUNT(*) AS cnt, SUM(h.CalcFlag * h.KingakuTotal) AS kin
			    FROM Tran01Tenuri h
			   WHERE h.DenDay BETWEEN @0 AND @1
			   GROUP BY h.Id_Tenpo)
			SELECT t.Code AS 店舗コード, t.Name AS 店舗名, s.cnt AS 伝票数, s.kin AS 純売上
			  FROM s JOIN MasterTokui t ON t.Id = s.Id_Tenpo
			 ORDER BY 1
			""", [Date("from", "期間開始", "20000101"), Date("to", "期間終了", "20991231")], ["MasterTokui", "Tran01Tenuri"]),
	];

	/// <summary>検査で拒否されるべきSQL（3DB共通）</summary>
	static readonly (string Name, string Sql)[] Rejects = [
		("更新SQL", "DELETE FROM Tran01Tenuri"),
		("データ変更CTE", "WITH d AS (DELETE FROM Tran01Tenuri RETURNING Id) SELECT COUNT(*) FROM d"),
		("SELECT INTO", "SELECT * INTO tmp_aiquery FROM Tran01Tenuri"),
		("行ロック", "SELECT Id FROM Tran01Tenuri FOR UPDATE"),
		("システム表", "SELECT * FROM SysAiQuery"),
		("システムスキーマ", "SELECT table_name FROM information_schema.tables"),
		("待機関数(PostgreSQL)", "SELECT pg_sleep(1)"),
		("待機関数(MariaDB)", "SELECT SLEEP(1)"),
		("#コメント", "SELECT COUNT(*) FROM Tran01Tenuri # x"),
		("存在しない表", "SELECT * FROM NoSuchTableAiQuery"),
		("存在しない列", "SELECT h.NoSuchColumnAiQuery FROM Tran01Tenuri h"),
	];

	static readonly List<string> Report = [];

	public static async Task RunAsync(VmSession session) {
		var screens = Path.GetFullPath(Path.Combine("..", "Doc", "test", "UatVm", "out", $"aiquerydb-{Provider.ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmmss}"));
		Directory.CreateDirectory(screens);
		AiQueryPersonalStore.FilePath = Path.Combine(screens, "personal.json");
		AiQueryPersonalStore.Save(Queries.Select(q => new AiQuerySaved {
			SaveDate = "20261106", Title = q.Title, SqlText = q.Sql, SqlParams = Common.SerializeObject(q.Params), Description = "AI照会 3DB確認用",
		}));
		session.Note("対象", new { Provider, Screens = screens });
		Report.Add($"# AI照会 接続先DB確認（{Provider}） {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		Report.Add(string.Empty);
		session.SetDialogResponder(req => req.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.No : MessageBoxResult.OK);

		var d = session.OpenView<AiQueryView, AiQueryViewModel>();
		try {
			await d.WaitAsync("初期化", vm => !vm.IsBusy && vm.PersonalList.Count == Queries.Length);
			Report.Add("## 固定SQL（SQLite方言）の検査・実行");
			Report.Add(string.Empty);
			foreach (var q in Queries) {
				await RunSavedAsync(session, d, q.Title, q.Tables);
			}
			await Capture(session, d.View, screens, "aiquerydb_result");

			Report.Add(string.Empty);
			Report.Add("## 検査で拒否するSQL");
			Report.Add(string.Empty);
			foreach (var (name, sql) in Rejects) {
				session.ClearDialogs();
				// 前に開いたSQLのパラメータを持ち越さないよう、新しい会話（SQL・パラメータなし）から手入力する
				d.Run("新しい会話", vm => vm.NewConversationCommand);
				d.Input($"拒否: {name}", vm => { vm.IsSqlEditable = true; vm.SqlText = sql; });
				await d.RunAsync($"検査: {name}", vm => vm.ValidateCommand);
				await d.WaitAsync("検査完了", vm => !vm.IsBusy);
				var ok = !d.Vm.IsSqlValidated && d.Vm.SqlMessage.Contains("検査エラー") && !d.Vm.SqlMessage.Contains("パラメータ数");
				session.Check($"拒否: {name}", ok, new { sql, d.Vm.SqlMessage });
				Report.Add($"- {(ok ? "PASS" : "**FAIL**")} {name}: `{sql}` → {d.Vm.SqlMessage}");
			}

			if (Environment.GetEnvironmentVariable("CV_AIQUERY_LIVE") == "1") {
				d.Run("新しい会話", vm => vm.NewConversationCommand);
				await LiveAsync(session, d, screens);
			}
			else {
				session.Note("LLMの実接続は省略（CV_AIQUERY_LIVE=1 で実行）");
				Report.Add(string.Empty);
				Report.Add("LLMの実接続は省略。");
			}
		}
		finally {
			d.View.Close();
			var md = Path.Combine(screens, "aiquerydb-result.md");
			File.WriteAllText(md, string.Join("\r\n", Report) + "\r\n", new System.Text.UTF8Encoding(false));
			session.Note("結果ファイル", new { md });
		}
	}

	/// <summary>個人保存のSQLを開き、検査→実行する。件数は問わず、検査済み・実行成功・参照表を確認する</summary>
	static async Task RunSavedAsync(VmSession session, ViewDriver<AiQueryViewModel> d, string title, string[] tables) {
		session.ClearDialogs();
		d.Input($"個人保存を選択: {title}", vm => vm.SelectedPersonal = vm.PersonalList.Single(x => x.Title == title));
		d.Run("開く", vm => vm.OpenPersonalCommand);
		await d.RunAsync($"実行: {title}", vm => vm.RunCommand);
		await d.WaitAsync("実行完了", vm => !vm.IsBusy, timeoutMs: 300_000);
		var vm = d.Vm;
		var executed = vm.IsSqlValidated && !vm.ResultInfo.StartsWith("実行失敗") && !vm.ResultInfo.StartsWith("SQLが検査")
			&& !session.Dialogs.Any(x => x.Request.Kind is "ShowWarningDialog" or "ShowErrorDialog");
		var tablesOk = tables.All(t => vm.SqlMessage.Contains(t));
		session.Check($"検査・実行: {title}", executed && tablesOk, new { vm.SqlMessage, vm.ResultInfo });
		var table = vm.ResultView?.Table;
		var columns = table == null ? string.Empty : string.Join(",", table.Columns.Cast<DataColumn>().Select(x => x.Caption));
		var first = table == null || table.Rows.Count == 0 ? string.Empty : string.Join(" | ", table.Rows[0].ItemArray);
		Report.Add($"- {(executed && tablesOk ? "PASS" : "**FAIL**")} {title}: {vm.SqlMessage} / {vm.ResultInfo} / 列: {columns} / 先頭行: {first}");
	}

	/// <summary>LLMで会話→（確認）→SQL作成→実行。AIの文言・件数は固定せず、状態遷移と検査・実行の成否だけを見る</summary>
	static async Task LiveAsync(VmSession session, ViewDriver<AiQueryViewModel> d, string screens) {
		Report.Add(string.Empty);
		Report.Add("## LLMでの会話・SQL作成・実行");
		Report.Add(string.Empty);
		const string request = "2026年9月の店舗別の売上（税抜、返品は差し引く、計上日基準）を店舗コード順で欲しい。明細の商品別ではなく伝票ヘッダで集計して。";
		const string answer = "店舗売上（Tran01Tenuri）です。税抜の純売上と伝票数を、店舗コード・店舗名ごとに出してください。他は条件どおりで確定してよいです。";
		d.Input("要求入力", vm => vm.InputText = request);
		await d.RunAsync("送信", vm => vm.SendCommand);
		await d.WaitAsync("AI応答", vm => !vm.IsBusy, timeoutMs: 600_000);
		Report.Add($"- 要求: {request}");
		Report.Add($"  - AI: {LastAi(d.Vm)}（{d.Vm.ChatStatus}）");
		for (var i = 0; i < 3 && d.Vm.ChatStatus == EnumAiQueryStatus.Asking; i++) {
			d.Input("回答", vm => vm.InputText = answer);
			await d.RunAsync("回答送信", vm => vm.SendCommand);
			await d.WaitAsync("AI応答", vm => !vm.IsBusy, timeoutMs: 600_000);
			Report.Add($"- 回答: {answer}");
			Report.Add($"  - AI: {LastAi(d.Vm)}（{d.Vm.ChatStatus}）");
		}
		if (!session.Check("LLM: 条件確認まで進む", d.Vm.ChatStatus == EnumAiQueryStatus.Confirming, new { d.Vm.ChatStatus, Ai = LastAi(d.Vm) })) {
			Report.Add("- **FAIL** 条件確認まで進まなかった");
			return;
		}
		await d.RunAsync("SQL作成", vm => vm.GenerateCommand);
		await d.WaitAsync("SQL作成完了", vm => !vm.IsBusy, timeoutMs: 600_000);
		var generated = session.Check("LLM: SQL作成・検査済み", d.Vm.ChatStatus == EnumAiQueryStatus.Generated && d.Vm.IsSqlValidated,
			new { d.Vm.ChatStatus, d.Vm.SqlText, d.Vm.SqlMessage, Ai = LastAi(d.Vm) });
		Report.Add($"- SQL作成: {(generated ? "PASS" : "**FAIL**")} {d.Vm.SqlMessage}");
		Report.Add(string.Empty);
		Report.Add("```sql");
		Report.Add(d.Vm.SqlText);
		Report.Add("```");
		Report.Add($"パラメータ: {string.Join(" / ", d.Vm.Parameters.Select(x => $"@{x.Index} {x.Label}({x.Type}) = {x.Value}"))}");
		if (!generated) return;
		session.ClearDialogs();
		await d.RunAsync("AI作成SQLを実行", vm => vm.RunCommand);
		await d.WaitAsync("実行完了", vm => !vm.IsBusy, timeoutMs: 300_000);
		var ran = !d.Vm.ResultInfo.StartsWith("実行失敗") && !session.Dialogs.Any(x => x.Request.Kind is "ShowWarningDialog" or "ShowErrorDialog");
		session.Check("LLM: AI作成SQLの実行", ran, new { d.Vm.ResultInfo });
		var table = d.Vm.ResultView?.Table;
		Report.Add(string.Empty);
		Report.Add($"- 実行: {(ran ? "PASS" : "**FAIL**")} {d.Vm.ResultInfo}");
		if (table != null) {
			Report.Add($"  - 列: {string.Join(",", table.Columns.Cast<DataColumn>().Select(x => x.Caption))}");
			foreach (var row in table.Rows.Cast<DataRow>().Take(5)) Report.Add($"  - {string.Join(" | ", row.ItemArray)}");
		}
		await Capture(session, d.View, screens, "aiquerydb_live_result");
	}

	static string LastAi(AiQueryViewModel vm) =>
		(vm.ChatLines.LastOrDefault(x => !x.IsUser)?.Text ?? string.Empty).Replace("\r", " ").Replace("\n", " ");

	static async Task Capture(VmSession session, Window view, string screens, string name) {
		view.UpdateLayout();
		await view.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
		await Task.Delay(150);
		session.Note(name + ":画面画像", new { Path = ScreenLayoutCheck.SaveJpeg(view, screens, name) });
	}
}
