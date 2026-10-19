using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels._23AiQuery;
using CvWpfclient.Views._23AiQuery;
using Microsoft.Data.Sqlite;

namespace UatVm.Scenarios;

/// <summary>
/// 専用DBでAI照会（CSV）画面を確認する（Doc/spec/2026-10-19_AI-query_最小変更設計案.md）。
/// <para>
/// AIを使わない経路（共有保存SQLを開く→Msg092検査→Msg101実行→結果、更新SQLの拒否、共有・個人保存と削除）を固定データで照合し、
/// 標準・最小サイズの画面画像を保存する。環境変数 CV_AIQUERY_LIVE=1 のときだけ、サーバ設定の社内LLMで会話→SQL作成→実行まで確認する。
/// 個人保存は利用者の実ファイルを使わず、画像フォルダ内の personal.json へ差し替える。
/// </para>
/// </summary>
public static class AiQueryScenario {
	static string? databasePath;
	const string ShopA = "000701";
	const string ShopB = "000702";
	const string SharedTitle = "UAT 2026年9月 店舗別純売上";

	/// <summary>店舗売上の純額（返品はCalcFlagで控除）を店舗別に出す保存SQL。業務辞書の規約どおりの形</summary>
	const string SharedSql = """
		SELECT ifnull(json_extract(h.VTenpo,'$.Cd'),'') AS 店舗コード,
		       ifnull(json_extract(h.VTenpo,'$.Mei'),'') AS 店舗名,
		       SUM(CASE WHEN h.CalcFlag = 1 THEN 1 ELSE 0 END) AS 伝票数,
		       SUM(h.CalcFlag * h.KingakuTotal) AS 純売上
		  FROM Tran01Tenuri h
		 WHERE h.DenDay BETWEEN @0 AND @1
		 GROUP BY 1, 2
		 ORDER BY 1
		""";

	public static void Seeder(string dbPath) {
		var path = Path.GetFullPath(dbPath);
		if (!Path.GetFileName(path).StartsWith("aiquery-uat-", StringComparison.OrdinalIgnoreCase)
			|| !path.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
			|| path.Split(Path.DirectorySeparatorChar).Any(x => x.Equals("CvServer", StringComparison.OrdinalIgnoreCase))
			|| (File.Exists(path) && new FileInfo(path).Length > 0))
			throw new InvalidOperationException("未作成/空の専用aiquery-uat-*.dbだけ使用できます。");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
		connection.Open();
		using var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		if (!new DefineDataTable().InitializeAsync(db, false).GetAwaiter().GetResult()) throw new InvalidOperationException("専用DB初期化失敗");
		var a = new MasterTokui { Code = ShopA, Name = "AI確認店A", TenType = 6 }; db.Insert(a);
		var b = new MasterTokui { Code = ShopB, Name = "AI確認店B", TenType = 6 }; db.Insert(b);
		CodeNameView V(MasterTokui t) => new() { Sid = t.Id, Cd = t.Code, Mei = t.Name };
		// A: 売上1000+2000、返品500（Kubun20は正値保持・CalcFlag=-1）→ 純売上2500・伝票数2。B: 3000。期間外(10月)は除外
		foreach (var (shop, day, kubun, kingaku) in new[] {
			(a, "20260905", 10, 1000L), (a, "20260910", 10, 2000L), (a, "20260912", 20, 500L), (b, "20260915", 10, 3000L), (b, "20261001", 10, 9999L) }) {
			db.Insert(new Tran01Tenuri { DenDay = day, Kubun = kubun, Id_Tenpo = shop.Id, VTenpo = V(shop), Id_Soko = shop.Id, KingakuTotal = kingaku,
				Memo = "AI照会UAT", Jmeisai = [new Tran99Meisai { Su = 1, Tanka = (int)kingaku, Kingaku = kingaku }] });
		}
		db.Insert(new SysAiQuery {
			SaveDate = "20261001", Title = SharedTitle, SqlText = SharedSql, Description = "店舗売上を計上日で2026年9月に絞り、返品をCalcFlagで控除した純売上と伝票数を店舗別に出す",
			SqlParams = Common.SerializeObject(new List<AiQueryParam> {
				new() { Name = "from", Label = "期間開始", Type = EnumAiQueryParamType.Date, Value = "20260901" },
				new() { Name = "to", Label = "期間終了", Type = EnumAiQueryParamType.Date, Value = "20260930", AskOnRun = true },
			}),
		});
		databasePath = path;
	}

	public static async Task RunAsync(VmSession session) {
		if (databasePath is null) throw new InvalidOperationException("専用Seederを実行してください。");
		var screens = Path.Combine(Path.GetDirectoryName(databasePath)!, "aiquery-screens-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
		Directory.CreateDirectory(screens);
		AiQueryPersonalStore.FilePath = Path.Combine(screens, "personal.json");
		session.Note("対象", new { Database = databasePath, Screens = screens, Personal = AiQueryPersonalStore.FilePath });
		// 削除確認は Yes、共有の上書き確認は No（新規追加）、警告は OK
		session.SetDialogResponder(req => req.Message.Contains("削除しますか") ? MessageBoxResult.Yes
			: req.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.No : MessageBoxResult.OK);

		var d = session.OpenView<AiQueryView, AiQueryViewModel>();
		try {
			await d.WaitAsync("共有保存の読込", vm => !vm.IsBusy && vm.SharedList.Count == 1);
			Check(session, "初期は実行不可", !d.Vm.RunCommand.CanExecute(null) && !d.Vm.SaveSharedCommand.CanExecute(null));
			await Capture(session, d.View, screens, "aiquery_standard_initial");

			// 保存SQLを開く（AIなし）→ 検査・実行
			d.Input("共有保存を選択", vm => vm.SelectedShared = vm.SharedList[0]);
			d.Run("開く", vm => vm.OpenSharedCommand);
			Check(session, "SQL・パラメータの読込", d.Vm.SqlText.Contains("Tran01Tenuri") && d.Vm.Parameters.Count == 2 && d.Vm.SqlTitle == SharedTitle && !d.Vm.IsSqlValidated,
				new { d.Vm.SqlTitle, Params = d.Vm.Parameters.Count, d.Vm.IsSqlValidated });
			Check(session, "実行時入力の引継ぎ", d.Vm.Parameters[1].AskOnRun && d.Vm.Parameters[1].Type == EnumAiQueryParamType.Date);
			await d.RunAsync("実行", vm => vm.RunCommand);
			await d.WaitAsync("結果", vm => !vm.IsBusy && vm.HasResult);
			var rows = d.Vm.ResultView!.Table!.Rows.Cast<DataRow>().ToList();
			CheckEqual(session, "結果2行", 2, rows.Count);
			CheckEqual(session, "列見出しはSQL別名", "店舗コード,店舗名,伝票数,純売上", string.Join(",", d.Vm.ResultView.Table.Columns.Cast<DataColumn>().Select(x => x.Caption)));
			CheckEqual(session, "A: 伝票数2・純売上2500（返品500控除）", "000701|AI確認店A|2|2500", string.Join("|", rows[0].ItemArray));
			CheckEqual(session, "B: 期間外を除き3000", "000702|AI確認店B|1|3000", string.Join("|", rows[1].ItemArray));
			Check(session, "検査済みになる", d.Vm.IsSqlValidated && d.Vm.SqlMessage.Contains("Tran01Tenuri"), new { d.Vm.SqlMessage });
			Check(session, "CSV保存・保存が可能", d.Vm.ExportCsvCommand.CanExecute(null) && d.Vm.SaveSharedCommand.CanExecute(null) && d.Vm.SavePersonalCommand.CanExecute(null));
			Check(session, "SQLファイル本文にパラメータ値", AiQueryViewModel.BuildSqlFileText(new AiQuerySqlDraft {
				Sql = d.Vm.SqlText, Title = d.Vm.SqlTitle, Parameters = [.. d.Vm.Parameters.Select(x => x.ToParam())] }, new DateTime(2026, 10, 19))
				.Contains("-- パラメータ: @1 期間終了(Date) = 20260930"));
			CheckResultFiles(session, d.Vm, screens);
			await LayoutAsync(session, d.View, screens, "result");

			// 実行時入力のパラメータが空なら実行しない
			session.ClearDialogs();
			d.Input("期間終了を空にする", vm => vm.Parameters[1].Value = string.Empty);
			await d.RunAsync("空パラメータで実行", vm => vm.RunCommand);
			Check(session, "空パラメータは警告して結果を残す", session.Dialogs.Any(x => x.Request.Message.Contains("期間終了")) && d.Vm.HasResult);
			d.Input("期間終了を戻す", vm => vm.Parameters[1].Value = "20260930");

			// 手修正した更新SQLは検査で拒否し、DBを変えない
			session.ClearDialogs();
			d.Input("更新SQLへ手修正", vm => { vm.IsSqlEditable = true; vm.SqlText = "DELETE FROM Tran01Tenuri"; });
			Check(session, "手修正で検査済み・結果を外す", !d.Vm.IsSqlValidated && !d.Vm.HasResult && !d.Vm.SaveSharedCommand.CanExecute(null));
			await d.RunAsync("更新SQLを実行", vm => vm.RunCommand);
			Check(session, "検査で拒否", !d.Vm.HasResult && d.Vm.SqlMessage.Contains("検査エラー") && session.Dialogs.Any(x => x.Request.Kind == "ShowWarningDialog"),
				new { d.Vm.SqlMessage });
			CheckEqual(session, "伝票は削除されていない", 5L, CountRows("Tran01Tenuri"));
			await Capture(session, d.View, screens, "aiquery_standard_rejected");

			// 開き直して共有保存（上書き確認は「いいえ」＝新規追加）・個人保存・削除
			d.Run("開き直す", vm => vm.OpenSharedCommand);
			await d.RunAsync("再実行", vm => vm.RunCommand);
			await d.WaitAsync("再実行結果", vm => !vm.IsBusy && vm.HasResult);
			d.Input("タイトル変更", vm => vm.SqlTitle = SharedTitle + "（コピー）");
			await d.RunAsync("共有保存", vm => vm.SaveSharedCommand);
			await d.WaitAsync("共有一覧2件", vm => !vm.IsBusy && vm.SharedList.Count == 2);
			var copy = d.Vm.SharedList.Single(x => x.Title.EndsWith("（コピー）"));
			Check(session, "保存日はサーバ打刻・元は残る", copy.SaveDate == DateTime.Today.ToString("yyyyMMdd") && d.Vm.SharedList.Any(x => x.Title == SharedTitle),
				new { copy.SaveDate, copy.Id_Login });
			session.Note("認証解除中のUATのため保存者(Id_Login)はJWTが無く0", new { copy.Id_Login });

			d.Run("個人保存", vm => vm.SavePersonalCommand);
			Check(session, "個人保存1件・ファイル作成", d.Vm.PersonalList.Count == 1 && File.Exists(AiQueryPersonalStore.FilePath));
			d.Input("タイトル変更（個人2件目）", vm => vm.SqlTitle = SharedTitle + "（個人2）");
			d.Run("個人保存（2件目）", vm => vm.SavePersonalCommand);
			CheckEqual(session, "個人保存2件", 2, d.Vm.PersonalList.Count);
			SimulatePersonalTransfer(session, d.Vm, screens);

			d.Input("個人保存（コピー）を選択", vm => vm.SelectedPersonal = vm.PersonalList.Single(x => x.Title.EndsWith("（コピー）")));
			d.Run("個人保存を開く", vm => vm.OpenPersonalCommand);
			Check(session, "個人保存から開ける", d.Vm.SqlTitle.EndsWith("（コピー）") && d.Vm.Parameters.Count == 2);
			d.Run("個人保存を削除", vm => vm.DeletePersonalCommand);
			CheckEqual(session, "個人保存1件（選択した1件だけ削除）", "1|" + SharedTitle + "（個人2）",
				$"{d.Vm.PersonalList.Count}|{string.Join(",", d.Vm.PersonalList.Select(x => x.Title))}");
			d.Input("残りを選択", vm => vm.SelectedPersonal = vm.PersonalList[0]);
			d.Run("残りを削除", vm => vm.DeletePersonalCommand);
			CheckEqual(session, "個人保存0件", 0, d.Vm.PersonalList.Count);
			d.Input("コピーを選択", vm => vm.SelectedShared = vm.SharedList.Single(x => x.Title.EndsWith("（コピー）")));
			await d.RunAsync("共有保存を削除", vm => vm.DeleteSharedCommand);
			await d.WaitAsync("共有一覧1件", vm => !vm.IsBusy && vm.SharedList.Count == 1);
			CheckEqual(session, "DB上の共有保存1件", 1L, CountRows("SysAiQuery"));

			d.Run("新しい会話", vm => vm.NewConversationCommand);
			Check(session, "新しい会話でSQL・結果を消す", d.Vm.SqlText.Length == 0 && !d.Vm.HasResult && d.Vm.ChatLines.Count == 0 && !d.Vm.RunCommand.CanExecute(null));

			if (Environment.GetEnvironmentVariable("CV_AIQUERY_LIVE") == "1") {
				await LiveAsync(session, d, screens);
			}
			else {
				session.Note("社内LLMの実接続は省略（CV_AIQUERY_LIVE=1 で実行）");
			}
		}
		catch (Exception ex) {
			Record("例外", false, ex.ToString());
			throw;
		}
		finally {
			d.View.Close();
			WriteReport(session, screens);
		}
	}

	/// <summary>社内LLMで会話→（確認）→SQL作成→実行。AIの文言は固定しないため、状態遷移と実行結果だけを見る</summary>
	static async Task LiveAsync(VmSession session, ViewDriver<AiQueryViewModel> d, string screens) {
		const string firstRequest = "2026年9月の店舗別の売上（税抜、返品は差し引く、計上日基準）を店舗コード順で欲しい";
		d.Input("要求入力", vm => vm.InputText = firstRequest);
		var watch = System.Diagnostics.Stopwatch.StartNew();
		var linesBefore = d.Vm.ChatLines.Count;
		// 応答待ちの間に待機表示（進行帯・経過時間）を確認するため、送信は待たずに開始する
		var sending = d.Vm.SendCommand.ExecuteAsync(null);
		await d.WaitAsync("待機表示", vm => vm.IsAiWaiting, timeoutMs: 10_000);
		await Task.Delay(2500);
		Check(session, "待機表示に経過時間", d.Vm.IsAiWaiting && d.Vm.WaitingText.Contains("AIが考えています") && !d.Vm.WaitingText.Contains("経過 0:00"),
			new { d.Vm.WaitingText });
		Check(session, "待機中は送信不可・取消可", !d.Vm.SendCommand.CanExecute(null) && d.Vm.CancelChatCommand.CanExecute(null));
		await Capture(session, d.View, screens, "aiquery_standard_live_waiting");
		await sending;
		await d.WaitAsync("AI応答", vm => !vm.IsBusy, timeoutMs: 600_000);
		Check(session, "応答後は待機表示を消す", !d.Vm.IsAiWaiting);
		RecordTurn(session, "送信", firstRequest, d.Vm, linesBefore, watch.Elapsed);
		for (var i = 0; i < 2 && d.Vm.ChatStatus == EnumAiQueryStatus.Asking; i++) {
			const string answer = "店舗売上（Tran01Tenuri）です。税抜の純売上と伝票数を、店舗コード・店舗名ごとに出してください。他は条件どおりで確定してよいです。";
			d.Input("回答", vm => vm.InputText = answer);
			watch.Restart(); linesBefore = d.Vm.ChatLines.Count;
			await d.RunAsync("回答送信", vm => vm.SendCommand);
			await d.WaitAsync("AI応答", vm => !vm.IsBusy, timeoutMs: 600_000);
			RecordTurn(session, "送信", answer, d.Vm, linesBefore, watch.Elapsed);
		}
		await Capture(session, d.View, screens, "aiquery_standard_live_chat");
		if (!Check(session, "条件確認まで進む", d.Vm.ChatStatus == EnumAiQueryStatus.Confirming, new { d.Vm.ChatStatus })) return;
		watch.Restart(); linesBefore = d.Vm.ChatLines.Count;
		await d.RunAsync("SQL作成", vm => vm.GenerateCommand);
		await d.WaitAsync("SQL作成完了", vm => !vm.IsBusy, timeoutMs: 600_000);
		RecordTurn(session, "SQL作成", "（［SQL作成］ボタン）", d.Vm, linesBefore, watch.Elapsed);
		if (!Check(session, "SQL作成・検査済み", d.Vm.ChatStatus == EnumAiQueryStatus.Generated && d.Vm.IsSqlValidated, new { d.Vm.SqlText, d.Vm.SqlMessage })) return;
		await d.RunAsync("AI作成SQLを実行", vm => vm.RunCommand);
		await d.WaitAsync("AI作成SQLの結果", vm => !vm.IsBusy && (vm.HasResult || vm.ResultInfo.Length > 0), timeoutMs: 120_000);
		var table = d.Vm.ResultView?.Table;
		Generated = new GeneratedSql(d.Vm.SqlTitle, d.Vm.SqlDescription, d.Vm.SqlText,
			[.. d.Vm.Parameters.Select(x => $"@{x.Index} {x.Label}({x.Type}) = {x.Value}")],
			table == null ? [] : [.. table.Columns.Cast<DataColumn>().Select(x => x.Caption)],
			table == null ? [] : [.. table.Rows.Cast<DataRow>().Select(r => string.Join(" | ", r.ItemArray))]);
		session.Note("AI作成SQLと結果", Generated);
		// 純売上（2500/3000）が結果のどこかの列に出ていること（列名・列順はAIに任せる）
		Check(session, "AI作成SQLの集計値", table != null && table.Rows.Count == 2
			&& table.Rows.Cast<DataRow>().Any(r => r.ItemArray.Any(v => Convert.ToString(v) == "2500"))
			&& table.Rows.Cast<DataRow>().Any(r => r.ItemArray.Any(v => Convert.ToString(v) == "3000")));
		await Capture(session, d.View, screens, "aiquery_standard_live_result");
	}

	/// <summary>
	/// 保存ダイアログの選択後と同じ書込み処理で CSV（主形式）と JSON（型保持）を出し、内容を照合する。
	/// ダイアログ自体は操作できないため、書込み処理（WriteResultFile）を直接呼ぶ。
	/// </summary>
	static void CheckResultFiles(VmSession session, AiQueryViewModel vm, string screens) {
		var csvPath = Path.Combine(screens, "result.csv");
		var jsonPath = Path.Combine(screens, "result.json");
		AiQueryViewModel.WriteResultFile(csvPath, vm.ResultRows, json: false);
		AiQueryViewModel.WriteResultFile(jsonPath, vm.ResultRows, json: true);
		var csvBytes = File.ReadAllBytes(csvPath);
		var csvLines = File.ReadAllLines(csvPath, System.Text.Encoding.UTF8);
		Check(session, "CSV: UTF-8 BOM付き", csvBytes.Length > 3 && csvBytes[0] == 0xEF && csvBytes[1] == 0xBB && csvBytes[2] == 0xBF);
		CheckEqual(session, "CSV: 見出し＋全件", "店舗コード,店舗名,伝票数,純売上|000701,AI確認店A,2,2500|000702,AI確認店B,1,3000", string.Join("|", csvLines));
		var jsonBytes = File.ReadAllBytes(jsonPath);
		var array = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(jsonPath, System.Text.Encoding.UTF8));
		var first = (Newtonsoft.Json.Linq.JObject)array[0];
		Check(session, "JSON: BOMなし・2件・列順どおり", jsonBytes[0] == (byte)'[' && array.Count == 2
			&& string.Join(",", first.Properties().Select(x => x.Name)) == "店舗コード,店舗名,伝票数,純売上");
		Check(session, "JSON: コードは先頭0の文字列、件数・金額は数値のまま", first["店舗コード"]!.Type == Newtonsoft.Json.Linq.JTokenType.String
			&& first.Value<string>("店舗コード") == "000701"
			&& first["伝票数"]!.Type == Newtonsoft.Json.Linq.JTokenType.Integer && first.Value<long>("純売上") == 2500,
			new { Json = first.ToString(Newtonsoft.Json.Formatting.None) });
		session.Note("結果ファイル", new { csvPath, jsonPath });
	}

	// ---- 個人保存の移行（ダイアログを開かずに tmp フォルダで読み書き） ----------------------

	/// <summary>
	/// 個人保存のエクスポート→別PC（空の個人保存）へのインポートを、保存ダイアログで選んだ後と同じ処理で再現する。
	/// 重複を追加しないこと、壊れたファイルでは個人保存を変えないことも確認し、最後に元の個人保存へ戻す。
	/// </summary>
	static void SimulatePersonalTransfer(VmSession session, AiQueryViewModel vm, string screens) {
		var tmp = Path.Combine(screens, "tmp");
		Directory.CreateDirectory(tmp);
		var original = AiQueryPersonalStore.FilePath;
		var exportPath = Path.Combine(tmp, "AI照会_個人保存_export.json");
		CheckEqual(session, "エクスポート: 2件を書出し", 2, vm.ExportPersonalTo(exportPath));
		var exported = Common.DeserializeObject<List<AiQuerySaved>>(File.ReadAllText(exportPath)) ?? [];
		Check(session, "エクスポート: 6項目が元と一致", exported.Count == 2 && exported.SequenceEqual(AiQueryPersonalStore.Load(original)),
			new { Export = Path.GetFileName(exportPath), Titles = exported.Select(x => x.Title) });

		// 別PC: 個人保存が空の状態へインポート
		AiQueryPersonalStore.FilePath = Path.Combine(tmp, "other-pc", "personal.json");
		try {
			CheckEqual(session, "インポート: 空の個人保存へ2件追加", 2, vm.ImportPersonalFrom(exportPath));
			Check(session, "インポート: 一覧とファイルに反映", vm.PersonalList.Count == 2 && AiQueryPersonalStore.Load().SequenceEqual(exported),
				new { vm.SavedMessage });
			CheckEqual(session, "再インポート: 同じ内容は追加しない", 0, vm.ImportPersonalFrom(exportPath));
			Check(session, "再インポート: 件数そのまま・メッセージ", vm.PersonalList.Count == 2 && vm.SavedMessage.Contains("同じ内容の2件は追加していません"),
				new { vm.SavedMessage });
			var broken = Path.Combine(tmp, "broken.json");
			File.WriteAllText(broken, "{ 壊れたファイル");
			var before = File.ReadAllBytes(AiQueryPersonalStore.FilePath);
			var rejected = false;
			try { vm.ImportPersonalFrom(broken); }
			catch (Newtonsoft.Json.JsonException) { rejected = true; }
			Check(session, "壊れたファイル: 例外にして個人保存を変えない", rejected && before.SequenceEqual(File.ReadAllBytes(AiQueryPersonalStore.FilePath)));
		}
		finally {
			// 元の個人保存へ戻し、一覧を読み直す（同じ内容なので追加は0件）
			AiQueryPersonalStore.FilePath = original;
			vm.ImportPersonalFrom(exportPath);
		}
		CheckEqual(session, "元の個人保存へ戻す", 2, vm.PersonalList.Count);
	}

	// ---- UAT結果の記録 ------------------------------------------------------------------------

	sealed record CheckRow(string Name, bool Pass, string Detail);
	sealed record ChatTurn(string Kind, string UserText, string AiText, string Status, double Seconds);
	sealed record GeneratedSql(string Title, string Description, string Sql, List<string> Parameters, List<string> Columns, List<string> Rows);

	static readonly List<CheckRow> Checks = [];
	static readonly List<ChatTurn> Transcript = [];
	static GeneratedSql? Generated;
	static readonly DateTime StartedAt = DateTime.Now;

	static void Record(string name, bool pass, object? detail) =>
		Checks.Add(new CheckRow(name, pass, detail switch { null => string.Empty, string text => text, _ => Common.SerializeObject(detail) }));

	static bool Check(VmSession session, string name, bool condition, object? detail = null) {
		Record(name, condition, detail);
		return session.Check(name, condition, detail);
	}

	static bool CheckEqual<T>(VmSession session, string name, T expected, T actual) {
		Record(name, EqualityComparer<T>.Default.Equals(expected, actual), new { expected, actual });
		return session.CheckEqual(name, expected, actual);
	}

	/// <summary>問い合わせ1回分（利用者の文言・AIの応答・状態・所要時間）を記録する</summary>
	static void RecordTurn(VmSession session, string kind, string userText, AiQueryViewModel vm, int linesBefore, TimeSpan elapsed) {
		var aiText = string.Join("\n\n", vm.ChatLines.Skip(linesBefore).Where(x => !x.IsUser).Select(x => (x.IsError ? "[エラー] " : string.Empty) + x.Text));
		var turn = new ChatTurn(kind, userText, aiText, vm.ChatStatus?.ToString() ?? "なし", Math.Round(elapsed.TotalSeconds, 1));
		Transcript.Add(turn);
		session.Note($"問い合わせ{Transcript.Count}", turn);
	}

	/// <summary>検査結果・問い合わせとAIの応答・生成SQLを Markdown と JSON に保存する</summary>
	static void WriteReport(VmSession session, string screens) {
		var md = new System.Text.StringBuilder();
		var pass = Checks.Count(x => x.Pass);
		md.AppendLine("# AI照会（CSV）UAT結果（自動出力）");
		md.AppendLine();
		md.AppendLine("| 項目 | 内容 |");
		md.AppendLine("|---|---|");
		md.AppendLine($"| 実行開始 | {StartedAt:yyyy-MM-dd HH:mm:ss} |");
		md.AppendLine("| シナリオ | `aiquery`（Doc/test/UatVm/Scenarios/AiQueryScenario.cs） |");
		md.AppendLine($"| 専用DB | `{Path.GetFileName(databasePath)}` |");
		md.AppendLine($"| 社内LLMの実接続 | {(Environment.GetEnvironmentVariable("CV_AIQUERY_LIVE") == "1" ? "あり（CV_AIQUERY_LIVE=1）" : "なし")} |");
		// 管理対象のCvServerは環境変数を引き継ぐため、AiQuery__Model があればそれが使われる
		md.AppendLine($"| モデル | {Environment.GetEnvironmentVariable("AiQuery__Model") ?? "サーバ設定（appsettings）の値"} |");
		md.AppendLine($"| 判定 | {(Checks.All(x => x.Pass) ? "PASS" : "FAIL")}（{pass} / {Checks.Count}） |");
		md.AppendLine($"| 証跡 | `{Path.GetFileName(session.Evidence.FilePath)}`、画像・CSV/JSON・tmp は `{Path.GetFileName(screens)}` |");
		md.AppendLine();
		md.AppendLine("## 1. 検査結果");
		md.AppendLine();
		md.AppendLine("| No | 判定 | 検査 | 詳細 |");
		md.AppendLine("|---|---|---|---|");
		for (var i = 0; i < Checks.Count; i++) {
			var c = Checks[i];
			md.AppendLine($"| {i + 1} | {(c.Pass ? "PASS" : "**FAIL**")} | {Cell(c.Name)} | {Cell(c.Detail, 200)} |");
		}
		md.AppendLine();
		md.AppendLine("## 2. 問い合わせとAIの応答");
		md.AppendLine();
		if (Transcript.Count == 0) md.AppendLine("社内LLMの実接続なし。");
		for (var i = 0; i < Transcript.Count; i++) {
			var t = Transcript[i];
			md.AppendLine($"### 2.{i + 1} {t.Kind}（応答 {t.Seconds} 秒、状態 {t.Status}）");
			md.AppendLine();
			md.AppendLine("**問い合わせ**");
			md.AppendLine();
			md.AppendLine(Quote(t.UserText));
			md.AppendLine();
			md.AppendLine("**AIの応答**");
			md.AppendLine();
			md.AppendLine(Quote(t.AiText));
			md.AppendLine();
		}
		md.AppendLine("## 3. AIが作成したSQLと実行結果");
		md.AppendLine();
		if (Generated == null) {
			md.AppendLine("SQL作成まで進んでいない。");
		}
		else {
			md.AppendLine($"- タイトル: {Generated.Title}");
			md.AppendLine($"- 説明: {Generated.Description}");
			md.AppendLine($"- パラメータ: {string.Join(" / ", Generated.Parameters)}");
			md.AppendLine();
			md.AppendLine("```sql");
			md.AppendLine(Generated.Sql);
			md.AppendLine("```");
			md.AppendLine();
			md.AppendLine("| " + string.Join(" | ", Generated.Columns) + " |");
			md.AppendLine("|" + string.Concat(Generated.Columns.Select(_ => "---|")));
			foreach (var row in Generated.Rows) md.AppendLine("| " + row + " |");
		}
		var mdPath = Path.Combine(screens, "aiquery-uat-result.md");
		File.WriteAllText(mdPath, md.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"), new System.Text.UTF8Encoding(false));
		var jsonPath = Path.Combine(screens, "aiquery-uat-result.json");
		File.WriteAllText(jsonPath, Newtonsoft.Json.JsonConvert.SerializeObject(new { StartedAt, Checks, Transcript, Generated }, Newtonsoft.Json.Formatting.Indented),
			new System.Text.UTF8Encoding(false));
		session.Note("UAT結果ファイル", new { mdPath, jsonPath });
	}

	static string Cell(string text, int max = int.MaxValue) {
		var t = text.Replace("\r", " ").Replace("\n", " ").Replace("|", "／");
		return t.Length > max ? t[..max] + "…" : t;
	}

	static string Quote(string text) =>
		string.Join("\r\n", text.Replace("\r\n", "\n").Split('\n').Select(x => x.Length == 0 ? ">" : "> " + x));

	static long CountRows(string table) {
		using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
		connection.Open();
		using var cmd = connection.CreateCommand();
		cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
		return Convert.ToInt64(cmd.ExecuteScalar());
	}

	/// <summary>標準・最小サイズで「条件・SQL」「結果」の両タブを画像保存し、表示崩れを検査する</summary>
	static async Task LayoutAsync(VmSession session, Window view, string screens, string label) {
		var vm = (AiQueryViewModel)view.DataContext;
		foreach (var minimum in new[] { false, true }) {
			if (minimum) { view.Width = view.MinWidth; view.Height = view.MinHeight; }
			var size = minimum ? "minimum" : "standard";
			foreach (var tab in new[] { 0, 1 }) {
				vm.DetailTabIndex = tab;
				await Capture(session, view, screens, $"aiquery_{size}_{label}_tab{tab}");
			}
		}
		view.Width = 1244; view.Height = 860;
		await Settle(view);
	}

	static async Task Capture(VmSession session, Window view, string screens, string name) {
		await Settle(view); await Task.Delay(150);
		session.Note(name + ":画面画像", new { Path = ScreenLayoutCheck.SaveJpeg(view, screens, name) });
		var issues = ScreenLayoutCheck.Inspect(view).Where(x => x.Kind != ScreenLayoutCheck.LongCellKind && x.Kind != "列が横スクロール外").ToList();
		Check(session, name + ":layout", issues.Count == 0, new { Issues = issues });
	}

	static async Task Settle(Window view) {
		view.UpdateLayout(); await view.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
	}
}
