using System.Data;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels._00System;
using CvWpfclient.ViewModels._30HHT;
using CvWpfclient.ViewModels._31Monthly;
using CvWpfclient.Views._00System;
using CvWpfclient.Views._30HHT;
using CvWpfclient.Views._31Monthly;

namespace UatVm.Scenarios;

/// <summary>
/// マニュアル排他制御（正典 `Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md`）の網羅性検証（E-12）。
/// テスト観点は `Doc/test/2026-09-07_マニュアル排他制御_手動観測手順.md` §5、
/// テスト計画は `Doc/test/2026-09-07_マニュアル排他制御_テスト計画.md` を参照する。
/// </summary>
/// <remarks>
/// <para>
/// 設計§5 L-02「既に<c>SysSeqType=1</c>の行があるとき、後発が中断し自分の行を残さない」を、
/// 設計§2.4「適用対象」の全処理へ網羅的に適用する。原価4項目（消化仕入更新・最終仕入原価更新・
/// 総平均原価更新・評価替え）は共通ランナー（<c>StreamStepProgressRunner</c>）非経由の個別実装であり、
/// 排他の適用漏れが最も起こりやすい箇所である（手動観測手順書§5.1）。
/// </para>
/// <para>
/// #4「現在庫再集計」はWPFのどの画面からも呼ばれないため対象外。本ケースの合格条件は
/// 手動観測手順書§5.1のとおり12処理（13操作、評価替えは適用・取消の両方）とする。
/// </para>
/// <para>
/// 監視の実行フラグ（<see cref="MasterConfig"/>、Category=<see cref="MasterConfig.CategoryAutoExec"/>、
/// Name=<see cref="MasterConfig.NameAutoExecEnabledPrefix"/>+<see cref="MasterConfig.AutoExecTaskIdManualLockMonitor"/>先頭8文字）
/// を試験開始前に<c>0</c>へ変更し、試験中に監視が自動解放してしまわないようにする（手動観測手順書§5.2）。
/// 実行フラグは発火の都度DBから読まれるため、S1〜S5の環境変数スイッチと異なりCvServer再起動なしに
/// 即時反映される（<see cref="ManualLockScenario"/>のE-14と同じ手法）。必ず<c>finally</c>で<c>1</c>へ復元する。
/// </para>
/// <para>
/// 占有行は<see cref="SysSequence"/>への直接INSERT（<see cref="ExDatabaseSqlite"/>経由、CvServerとは別接続）で
/// 作る（<see cref="ManualLockScenario"/>のE-10・E-03等と同じ、指示書が明示的に許可した手法）。
/// <c>TableName</c>はテスト用と分かる名前（<c>UATVM-ManualLockTest-E12</c>）にし、<c>ExpectedDuration=3600</c>、
/// <c>Vdu</c>は現在時刻にする。必ず<c>finally</c>で削除する。
/// </para>
/// <para>
/// 各処理は起動画面・ViewModel・コマンド・最小入力がそれぞれ異なるため、差異だけを吸収する小さな
/// ヘルパー（<c>Run*Async</c>群、うち棚卸2画面・原価3画面は共通基底を使う薄いジェネリックヘルパー
/// <see cref="RunStocktakeAsync{TView}"/>・<see cref="RunCostUpdateAsync{TView}"/>で束ねる）を用意し、
/// 本体は表形式（<c>cases</c>配列）で並べる。前提が満たせず排他判定に到達できない処理（原価系の
/// <c>UpdateCommand</c>は<c>CanUpdate</c>を満たさないと実行できない、評価替え取消は履歴が無いと
/// 実行できない、HHTは未変換データが無いと到達しない）は<c>Fail</c>ではなく<c>Note</c>でスキップし、
/// 理由を証跡へ残す。どの処理に到達できたかが本ケースの結果レポートの要点である。
/// </para>
/// <para>
/// 危険度対策: 排他がブロックすればDB書き込みは無い（<c>TryBegin</c>は必ず書き込みより前）という前提だが、
/// 万一ブロックされなかった場合の被害を小さくするため対象範囲は単一月（<see cref="TargetMonth"/>）・
/// 単一得意先/仕入先（<see cref="TokuiCode"/>、支払計算は<see cref="CvBase.MasterShiire"/>の先頭1件）・
/// 在庫のみ（在庫・掛再集計）・単一店舗（棚卸2画面はチェック取得後、先頭の1店舗だけを対象にし他は
/// 対象から外す）に絞る。
/// </para>
/// </remarks>
public static class ManualLockCoverageScenario {
	// 実データ(server-user163.db)調査の根拠は各定数のコメントを参照。値はすべてSELECTで確認済み。
	private const string TargetMonth = "2026/07"; // SummaryStock.SumMonth='202607'に4店舗ぶんのデータがあり、棚卸店舗一覧が0件にならない唯一に近い直近月
	private const string TokuiCode = "000002";
	private const string LockTableName = "UATVM-ManualLockTest-E12";
	// TranVulcanHtt.VdCnvDate=0(未変換)の行はDenDay='20260130'にのみ存在する(181件、最小=最大日付)。単一日に絞り被害を最小化する。
	private const string HhtTargetDate = "2026/01/30";

	public static async Task RunAsync(VmSession session) {
		await SetAutoExecEnabledAsync("0");
		session.Note("E-12 監視の実行フラグを0へ変更した(即時反映・試験中の自動解放を防止)", (object?)null);

		var lockId = 0L;
		try {
			var before = await FetchLockRowsAsync(session);
			if (!session.Check("E-12 開始前に排他行(SysSeqType=1)が無い", before.Count == 0, new { before })) {
				return;
			}

			lockId = await InsertFakeLockRowAsync(LockTableName, "占有中(E-12)", expectedDurationSeconds: 3600,
				memo: "E-12: 13処理の網羅性確認用（直接INSERT）");
			session.Note("E-12 占有行を直接INSERTした", new { lockId, LockTableName });

			// ==============================================================
			// 対象12処理（13操作）。手動観測手順書§5.3の表と同じ順序で並べる。
			// ==============================================================
			(string Label, Func<VmSession, string, Task<bool>> Run)[] cases = [
				("在庫・掛再集計", RunStockKakeAsync),
				("請求計算", RunBillingAsync),
				("支払計算", RunPaymentAsync),
				("棚卸開始処理", (s, t) => RunStocktakeAsync<StockTakeInitiationView>(s, "棚卸開始処理", t)),
				("棚卸確定処理", (s, t) => RunStocktakeAsync<StockTakeFinalizationView>(s, "棚卸確定処理", t)),
				("HHT取込反映", RunHhtAsync),
				("最終仕入原価更新", (s, t) => RunCostUpdateAsync<LastPurchaseCostRefreshView>(s, "最終仕入原価更新", t)),
				("総平均原価更新", (s, t) => RunCostUpdateAsync<TotalAverageCostUpdateView>(s, "総平均原価更新", t)),
				("消化仕入更新", (s, t) => RunCostUpdateAsync<ConsumptionPurchaseUpdateView>(s, "消化仕入更新", t)),
				("評価替え(適用)", RunCostRevaluationApplyAsync),
				("評価替え(取消)", RunCostRevaluationCancelAsync),
			];

			List<string> reached = [];
			List<string> skipped = [];
			foreach (var (label, run) in cases) {
				session.Note("case:開始", label);
				session.ClearDialogs();
				var isReached = await run(session, LockTableName);
				(isReached ? reached : skipped).Add(label);
			}
			session.Note("E-12 到達可否のまとめ(合格条件は12処理)", new { reachedCount = reached.Count, reached, skipped });

			var after = await FetchLockRowsAsync(session);
			session.Check("E-12 占有行が終始1件のまま増減していない", after.Count == 1 && after[0].Id == lockId, new { after });
		}
		finally {
			if (lockId != 0) {
				await DeleteLockRowByIdAsync(lockId);
			}
			var remaining = await FetchLockRowsAsync(session);
			session.Check("E-12 後始末:テスト用占有行を削除した", !remaining.Any(x => x.TableName == LockTableName), new { remaining });
			session.SetDialogResponder(null);
			await SetAutoExecEnabledAsync("1");
			session.Note("E-12 監視の実行フラグを1へ復元した", (object?)null);
		}

		session.Note("case:終了", "manuallock2 全ケース完了");
	}

	// ==================================================================
	// 個別処理のヘルパー（View/ViewModel・コマンド・最小入力の差異だけを吸収する）
	// ==================================================================

	private static async Task<bool> RunStockKakeAsync(VmSession session, string lockTableName) {
		var d = session.OpenView<StockKakeUpdateView, StockKakeUpdateViewModel>();
		await d.RunAsync("init:自社締日の取得", vm => vm.InitCommand);
		d.Input("対象(在庫のみ・単一月)", vm => {
			vm.YearMonthFrom = TargetMonth;
			vm.YearMonthTo = TargetMonth;
			vm.UpdateTarget = "在庫のみ";
		}, new { TargetMonth });
		session.SetDialogResponder(YesResponder);
		await d.RunAsync("execute:在庫・掛再集計", vm => vm.ExecuteCommand);
		return CheckBlocked(session, "在庫・掛再集計", lockTableName);
	}

	private static async Task<bool> RunBillingAsync(VmSession session, string lockTableName) {
		var d = session.OpenView<BillingCalculationView, BillingCalculationViewModel>();
		await d.RunAsync("init:締日一覧の取得", vm => vm.InitCommand);
		d.Input("対象(単一得意先・単一月)", vm => {
			vm.BillingMonth = TargetMonth;
			vm.TorihikiCodeFrom = TokuiCode;
			vm.TorihikiCodeTo = TokuiCode;
		}, new { TargetMonth, TokuiCode });
		session.SetDialogResponder(YesResponder);
		await d.RunAsync("execute:請求計算", vm => vm.ExecuteCommand);
		return CheckBlocked(session, "請求計算", lockTableName);
	}

	private static async Task<bool> RunPaymentAsync(VmSession session, string lockTableName) {
		var shiireRows = await session.QueryAsync<MasterShiire>($"SELECT * FROM {nameof(MasterShiire)} ORDER BY Id LIMIT 1");
		if (shiireRows.Count == 0) {
			Skip(session, "支払計算", "MasterShiireに1件も無いため起動できません。");
			return false;
		}
		var shiireCode = shiireRows[0].Code;
		var d = session.OpenView<PaymentCalculationView, PaymentCalculationViewModel>();
		await d.RunAsync("init:締日一覧の取得", vm => vm.InitCommand);
		d.Input("対象(単一仕入先・単一月)", vm => {
			vm.BillingMonth = TargetMonth;
			vm.TorihikiCodeFrom = shiireCode;
			vm.TorihikiCodeTo = shiireCode;
		}, new { TargetMonth, shiireCode });
		session.SetDialogResponder(YesResponder);
		await d.RunAsync("execute:支払計算", vm => vm.ExecuteCommand);
		return CheckBlocked(session, "支払計算", lockTableName);
	}

	/// <summary>
	/// 棚卸開始処理・棚卸確定処理の共通部分（<see cref="BaseStocktakeViewModel"/>）。
	/// <c>InitCommand</c>が無いため<c>LoadStatusCommand</c>を先に実行するが、<c>FallbackMonth</c>は
	/// その"前"に設定しなければならない（<c>LoadStatusCommand</c>が<c>FallbackMonth</c>をサーバーへ渡し、
	/// 対象倉庫を<c>SummaryStock.SumMonth</c>等から決めるため。既定値のまま呼ぶと当月扱いになり
	/// 実データが無く店舗一覧が0件になる。実データ調査でSummaryStock.SumMonth='202607'=<see cref="TargetMonth"/>
	/// にのみ対象店舗があると確認済み）。店舗一覧の先頭1件だけを対象にする（被害を最小に絞るため。
	/// 他の店舗は<c>IsTarget=false</c>にする）。
	/// </summary>
	private static async Task<bool> RunStocktakeAsync<TView>(VmSession session, string label, string lockTableName)
			where TView : Window, new() {
		var d = session.OpenView<TView, BaseStocktakeViewModel>();
		d.Input("対象月(店舗一覧取得の前に設定)", vm => vm.FallbackMonth = TargetMonth, new { TargetMonth });
		await d.RunAsync("list:棚卸状況取得", vm => vm.LoadStatusCommand);
		if (d.Vm.Rows.Count == 0) {
			Skip(session, label, $"店舗一覧が0件のため対象店舗を選べません(FallbackMonth={TargetMonth})。");
			return false;
		}
		d.Input("対象(1店舗のみ)", vm => {
			for (var i = 0; i < vm.Rows.Count; i++) {
				vm.Rows[i].IsTarget = i == 0;
			}
		}, new { targetShop = d.Vm.Rows[0].ShopCode });
		session.SetDialogResponder(YesResponder);
		await d.RunAsync($"execute:{label}", vm => vm.ExecuteCommand);
		return CheckBlocked(session, label, lockTableName);
	}

	/// <summary>
	/// <c>InitCommand</c>は既定で当月1日～当日を対象にするため未変換件数が0になる(実データはすべて
	/// <see cref="HhtTargetDate"/>にある)。<c>DateFrom</c>/<c>DateTo</c>を実データの日付へ入力し直してから
	/// 件数を取り直す。
	/// </summary>
	private static async Task<bool> RunHhtAsync(VmSession session, string lockTableName) {
		var d = session.OpenView<HhtDataUpdateView, HhtDataUpdateViewModel>();
		await d.RunAsync("init:未変換件数の取得", vm => vm.InitCommand);
		d.Input("対象日付(未変換データが存在する日のみ)", vm => {
			vm.DateFrom = HhtTargetDate;
			vm.DateTo = HhtTargetDate;
		}, new { HhtTargetDate });
		await d.RunAsync("refresh:未変換件数の再取得", vm => vm.RefreshCountCommand);
		if (d.Vm.UnconvertedCount <= 0) {
			Skip(session, "HHT取込反映", $"未変換件数が0のため警告のみでサーバーを呼ばず排他判定に到達しません(現在値={d.Vm.UnconvertedCount})。");
			return false;
		}
		session.SetDialogResponder(YesResponder);
		await d.RunAsync("execute:HHTデータ更新", vm => vm.ExecuteCommand);
		return CheckBlocked(session, "HHT取込反映", lockTableName);
	}

	/// <summary>
	/// 原価4項目のうち共通基底(<see cref="BaseCostUpdateViewModel"/>)を使う3画面
	/// （最終仕入原価更新・総平均原価更新・消化仕入更新）の共通部分。「確認」→「更新」の二段階で、
	/// <c>CanUpdate</c>（確認済み＋エラー0件）を満たさなければ更新を実行できないため、その前提を確認できた
	/// ときだけ排他判定（更新）まで進める。
	/// <para>
	/// 実データ調査の結論: 最終仕入原価更新・総平均原価更新は<see cref="TargetMonth"/>をどう変えても到達不能。
	/// 実DBの<c>MasterSysman.CostMethod=0(固定原価)</c>であり、両画面のプレビュー(<c>CostUpdateDbCost.cs</c>の
	/// <c>PreviewLastPurchaseCost</c>/<c>PreviewTotalAverageCost</c>)は現在方式がそれぞれ1/2と一致しない限り
	/// 「原価方式不一致」1行(対象0件・エラー1件)だけを返す固定の判定であり、対象月には依存しない。
	/// 消化仕入更新はこの判定を持たないため<see cref="TargetMonth"/>='2026/07'で到達できている。
	/// </para>
	/// </summary>
	private static async Task<bool> RunCostUpdateAsync<TView>(VmSession session, string label, string lockTableName)
			where TView : Window, new() {
		var d = session.OpenView<TView, BaseCostUpdateViewModel>();
		await d.RunAsync("init:状態取得", vm => vm.InitCommand);
		d.Input("対象月", vm => vm.TargetMonth = TargetMonth, new { TargetMonth });
		session.SetDialogResponder(YesResponder);
		await d.RunAsync("confirm:確認(プレビュー)", vm => vm.ConfirmCommand);
		if (!d.Vm.CanUpdate) {
			Skip(session, label, $"確認結果がCanUpdateを満たさないため更新を実行できません(対象={d.Vm.TargetCount}件, エラー={d.Vm.ErrorCount}件)。");
			return false;
		}
		session.ClearDialogs();
		await d.RunAsync("update:更新実行", vm => vm.UpdateCommand);
		return CheckBlocked(session, label, lockTableName);
	}

	/// <summary>
	/// 評価替え(適用)。<see cref="CostRevaluationViewModel"/>は原価4項目の他3画面と独自DTOのため基底を
	/// 共有しない（<c>CostRevaluationViewModel</c>のコメント参照）。対象月は既定(前月)のままにする。
	/// <para>
	/// 実データ調査の結論: この画面も対象月・掛率をどう変えても到達不能。実DBの<c>TranGenka</c>は0行であり、
	/// <c>CostUpdateDbReval.ComputeRevaluation</c>のBeforeCost解決(<c>ResolveCostAsOf</c>)は履歴が無い商品を
	/// 0円のまま返す。評価替えは(最終仕入原価・総平均原価と異なり)<c>MasterShohin.TankaGenka</c>へのフォールバックを
	/// 意図的に持たない(§16.9)ため、原価0円で全商品が対象外になり<c>TargetCount</c>は常に0になる。
	/// </para>
	/// </summary>
	private static async Task<bool> RunCostRevaluationApplyAsync(VmSession session, string lockTableName) {
		var d = session.OpenView<CostRevaluationView, CostRevaluationViewModel>();
		await d.RunAsync("init:期間・履歴の取得", vm => vm.InitCommand);
		session.SetDialogResponder(YesResponder);
		await d.RunAsync("confirm:確認(プレビュー)", vm => vm.ConfirmCommand);
		if (!d.Vm.CanUpdate) {
			Skip(session, "評価替え(適用)", $"確認結果がCanUpdateを満たさないため適用を実行できません(対象={d.Vm.TargetCount}件, エラー={d.Vm.ErrorCount}件)。");
			return false;
		}
		session.ClearDialogs();
		await d.RunAsync("update:適用実行", vm => vm.UpdateCommand);
		return CheckBlocked(session, "評価替え(適用)", lockTableName);
	}

	/// <summary>
	/// 評価替え(取消)。<c>TranGenkaReval</c>(Status=有効)が1件も無ければ取消対象を選べず到達不能。
	/// 実DBには現状0行のため到達不能になる見込みだが、投入されていれば先頭1件を選んで取り消す。
	/// </summary>
	private static async Task<bool> RunCostRevaluationCancelAsync(VmSession session, string lockTableName) {
		var d = session.OpenView<CostRevaluationView, CostRevaluationViewModel>();
		await d.RunAsync("init:期間・履歴の取得", vm => vm.InitCommand);
		if (d.Vm.HistoryRows.Count == 0) {
			Skip(session, "評価替え(取消)", $"TranGenkaRevalが0行のため取消対象を選べません(現在件数={d.Vm.HistoryRows.Count})。");
			return false;
		}
		d.Input("取消対象(先頭1件)", vm => vm.SelectedHistoryRow = vm.HistoryRows[0]);
		session.SetDialogResponder(YesResponder);
		session.ClearDialogs();
		await d.RunAsync("cancel:取消実行", vm => vm.CancelHistoryCommand);
		return CheckBlocked(session, "評価替え(取消)", lockTableName);
	}

	// ==================================================================
	// 共通ヘルパー
	// ==================================================================

	private static MessageBoxResult YesResponder(MessageExTestRoute.Request request) =>
		request.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK;

	/// <summary>
	/// 直前の操作が排他エラーで中断され、本文に占有行の<c>TableName</c>が含まれることを確認する
	/// （既存<c>ManualLockScenario</c>のE-02と同じ手法。<c>session.Dialogs</c>で<c>ShowErrorDialog</c>を数える）。
	/// </summary>
	private static bool CheckBlocked(VmSession session, string label, string lockTableName) {
		var errors = session.Dialogs.Where(x => x.Request.Kind == nameof(MessageEx.ShowErrorDialog)).ToList();
		return session.Check($"{label} 排他エラーで中断され、本文に占有行のTableNameが含まれる",
			errors.Count >= 1 && errors.Any(x => x.Request.Message.Contains(lockTableName, StringComparison.Ordinal)),
			new { errors = errors.Select(x => x.Request.Message) });
	}

	/// <summary>前提が満たせず排他判定に到達できない処理を、Failではなく理由付きのNoteで記録する。</summary>
	private static void Skip(VmSession session, string label, string reason) =>
		session.Note($"{label} スキップ(排他判定に到達不能)", reason);

	// ==================================================================
	// DB直接操作（ManualLockScenarioと同じ手法。CvServerとは別のSQLite接続を使う）
	// ==================================================================

	private static string ResolveDbPath() {
		var repoRoot = Directory.GetParent(Environment.CurrentDirectory)?.FullName
			?? throw new InvalidOperationException("リポジトリルートを解決できませんでした。");
		return Path.Combine(repoRoot, "CvServer", "server-user163.db");
	}

	private static Task<List<SysSequence>> FetchLockRowsAsync(VmSession session) =>
		session.QueryAsync<SysSequence>($"SELECT * FROM {nameof(SysSequence)} WHERE SysSeqType=1 ORDER BY Id");

	private static Task<long> InsertFakeLockRowAsync(string tableName, string columnName, long expectedDurationSeconds, string memo) =>
		Task.Run(() => {
			var dbPath = ResolveDbPath();
			using var db = ExDatabaseSqlite.GetDbConn(dbPath);
			var now = DateTime.UtcNow.Ticks;
			var row = new SysSequence {
				SysSeqType = (int)EmSysSeqType.ManualLock,
				TableName = tableName,
				ColumnName = columnName,
				SeqNo = 1,
				Memo = memo,
				ExpectedDuration = expectedDurationSeconds,
				Vdc = now,
				Vdu = now,
			};
			try {
				db.BeginTransaction(IsolationLevel.Serializable);
				db.Insert(row);
				db.CompleteTransaction();
			}
			catch {
				db.AbortTransaction();
				throw;
			}
			return row.Id;
		});

	private static Task DeleteLockRowByIdAsync(long id) =>
		Task.Run(() => {
			var dbPath = ResolveDbPath();
			using var db = ExDatabaseSqlite.GetDbConn(dbPath);
			db.Execute($"DELETE FROM {nameof(SysSequence)} WHERE Id=@0", id);
		});

	/// <summary>
	/// 監視タスクの実行フラグ(MasterConfig)をCvServerとは別接続で書き換える。実行フラグは発火の都度
	/// DBから読まれるため、静的初期化で読むS1〜S5の環境変数スイッチと異なりCvServer再起動なしに即時反映される
	/// （<see cref="ManualLockScenario"/>のE-14と同じ手法）。
	/// </summary>
	private static Task SetAutoExecEnabledAsync(string val) =>
		Task.Run(() => {
			var dbPath = ResolveDbPath();
			using var db = ExDatabaseSqlite.GetDbConn(dbPath);
			var name = MasterConfig.NameAutoExecEnabledPrefix + MasterConfig.AutoExecTaskIdManualLockMonitor[..8];
			db.Execute($"UPDATE {nameof(MasterConfig)} SET Val=@0 WHERE Category=@1 AND Name=@2",
				val, MasterConfig.CategoryAutoExec, name);
		});
}
