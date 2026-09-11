using System.Windows;
using CvBase;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels._31Monthly;
using CvWpfclient.Views._31Monthly;

namespace UatVm.Scenarios;

/// <summary>
/// マニュアル排他制御の「真の同時TryBegin」を、別プロセスからの同時要求で検証する
/// （テスト計画書 `Doc/test/2026-09-07_マニュアル排他制御_テスト計画.md` のE-04・E-02の実プロセス版）。
/// 正典は `Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md`。
/// </summary>
/// <remarks>
/// <para>
/// <b>1プロセス起動あたり「1回だけ排他を取る処理を撃つ」だけの最小シナリオ</b>である。
/// 既存の<see cref="ManualLockScenario"/>（E-01〜E-07を1本のシナリオ内で順に検証する）とは別物で、
/// こちらは <c>Run-ManualLockRace.ps1</c> が本シナリオを2プロセス起動し、共通の壁時計時刻
/// （<see cref="FireAt"/>、<c>--fire-at</c>）まで待ってから同時に撃たせることで
/// 「真の同時TryBegin」を作る。CvServerは1本のみとし（<c>README.md</c>「並列実行はできない」）、
/// 排他は<c>SysSequence</c>の行1本で効くため、同一サーバへ別プロセスから同時要求を投げれば条件を満たす。
/// </para>
/// <para>
/// 撃つ処理は既存<see cref="ManualLockScenario"/>のE-01（先行=請求計算・得意先<c>000002</c>）・
/// E-02（後発=支払計算・仕入先1件目）と同じ画面・同じ狭い対象を再利用する。役割は
/// <see cref="RaceLabel"/>で決める（既定＝請求計算役、<c>"B"</c>のときだけ支払計算役）。
/// 2つの異なる画面を割り当てるのは、勝者・敗者どちらになっても、
/// エラー本文中の先行<c>TableName</c>から「相手に先を越された」ことを一意に確認できるようにするため
/// （両方が同じ画面だと先行・後発の別が文言からだけでは判別できない）。
/// </para>
/// <para>
/// <b>1プロセス単体では「勝ち」も「負け」もPASSとする</b>（どちらになるかは競争の結果であり、
/// 片方が負けるのが正しい挙動である）。<see cref="VmSession.Check"/>は
/// 「勝ったのに自分の排他行が一度も観測できない」「負けたのに自分の行が残っている」等の
/// <b>矛盾</b>にだけ使い、win/lose自体をPASS/FAILの条件にはしない。
/// </para>
/// </remarks>
public static class ManualLockRaceScenario {
	private const string BillingMonth = "2026/07";
	private const string TokuiCode = "000002";

	/// <summary>
	/// 同期発火の壁時計時刻（同日）。<c>Program.cs</c>が<c>--fire-at</c>から設定する。
	/// 未指定（<c>null</c>）ならこの時刻待ちをせず即座に撃つ（単体デバッグ用）。
	/// </summary>
	public static DateTime? FireAt { get; set; }

	/// <summary>
	/// 証跡・記録上の自分の名前（例 <c>"A"</c> / <c>"B"</c>）。<c>Program.cs</c>が<c>--race-label</c>から設定する。
	/// <c>"B"</c>（大小無視）のときだけ支払計算役になり、それ以外（既定含む）は請求計算役になる。
	/// </summary>
	public static string RaceLabel { get; set; } = "?";

	private const string PaymentRoleLabel = "B";
	private const string BillingTableName = "請求計算";
	private const string PaymentTableName = "支払計算";

	public static async Task RunAsync(VmSession session) {
		var isPaymentRole = string.Equals(RaceLabel, PaymentRoleLabel, StringComparison.OrdinalIgnoreCase);
		var ownRole = isPaymentRole ? PaymentTableName : BillingTableName;
		session.Note("race:開始", new {
			label = RaceLabel,
			role = ownRole,
			fireAt = FireAt?.ToString("HH:mm:ss.fff"),
			now = DateTime.Now.ToString("HH:mm:ss.fff"),
		});

		// 発火(fire-at待ち)の直前に呼ぶと、そのgRPC往復時間ぶん発火が遅れて2プロセス間のズレが増える。
		// そのため「撃つ直前」の状態は、View初期化より前のこの時点でまとめて採っておく
		// （役割決定直後・まだ何も待っていない段階なので、実質的に「前回実行の後始末漏れが無いか」の確認に近い）。
		var beforeFire = await FetchLockRowsAsync(session);
		session.Note("race:前回実行の後始末確認(発火前)", new { label = RaceLabel, beforeFire });

		if (isPaymentRole) {
			await RunPaymentRoleAsync(session);
		}
		else {
			await RunBillingRoleAsync(session);
		}

		session.Note("race:終了", new { label = RaceLabel });
	}

	// ==================================================================
	// 役割ごとの画面駆動（既存ManualLockScenarioのE-01/E-02と同じ画面・同じ狭い対象を再利用）
	// ==================================================================

	private static async Task RunBillingRoleAsync(VmSession session) {
		var d = session.OpenView<BillingCalculationView, BillingCalculationViewModel>();
		// --hide-views指定時はView.Show()を呼ばないため、BaseWindow.OnContentRenderedが表示時に自動実行する
		// InitCommandが走らず、ShimeItemsが空のままになる。ViewModel自身のInitCommand
		// （BaseBillingCalculationViewModel.InitAsyncに[RelayCommand]が生成するIAsyncRelayCommand）を
		// RunAsyncで明示実行し、完了を待つことで代替する。
		await d.RunAsync("init:締日一覧の取得", vm => vm.InitCommand);
		d.Input("対象(請求計算)", vm => {
			vm.BillingMonth = BillingMonth;
			vm.TorihikiCodeFrom = TokuiCode;
			vm.TorihikiCodeTo = TokuiCode;
		}, new { BillingMonth, TokuiCode, label = RaceLabel });

		await FireAndJudgeAsync(session, ownTableName: BillingTableName, otherTableName: PaymentTableName,
			executeAsync: parameter => d.Vm.ExecuteCommand.ExecuteAsync(parameter));
	}

	private static async Task RunPaymentRoleAsync(VmSession session) {
		var d = session.OpenView<PaymentCalculationView, PaymentCalculationViewModel>();
		// --hide-views指定時はView.Show()を呼ばないため、BaseWindow.OnContentRenderedが表示時に自動実行する
		// InitCommandが走らず、ShimeItemsが空のままになる。ViewModel自身のInitCommand
		// （BaseBillingCalculationViewModel.InitAsyncに[RelayCommand]が生成するIAsyncRelayCommand）を
		// RunAsyncで明示実行し、完了を待つことで代替する。
		await d.RunAsync("init:締日一覧の取得", vm => vm.InitCommand);

		var shiireRows = await session.QueryAsync<MasterShiire>($"SELECT * FROM {nameof(MasterShiire)} ORDER BY Id LIMIT 1");
		if (shiireRows.Count == 0) {
			session.Fail("race:支払計算役の準備", "MasterShiireに1件も無いため、支払計算を起動できません。");
			return;
		}
		var shiireCode = shiireRows[0].Code;
		d.Input("対象(支払計算)", vm => {
			vm.BillingMonth = BillingMonth;
			vm.TorihikiCodeFrom = shiireCode;
			vm.TorihikiCodeTo = shiireCode;
		}, new { BillingMonth, shiireCode, label = RaceLabel });

		await FireAndJudgeAsync(session, ownTableName: PaymentTableName, otherTableName: BillingTableName,
			executeAsync: parameter => d.Vm.ExecuteCommand.ExecuteAsync(parameter));
	}

	// ==================================================================
	// 共通: 同時刻まで待って撃ち、勝敗を判定する
	// ==================================================================

	/// <summary>
	/// 入力済みのViewModelに対して、fire-at時刻まで待ってから<c>ExecuteCommand</c>を撃ち、
	/// 勝敗と整合性を判定する。
	/// </summary>
	private static async Task FireAndJudgeAsync(
		VmSession session, string ownTableName, string otherTableName, Func<object?, Task> executeAsync) {

		session.ClearDialogs();
		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);

		await WaitUntilFireTimeAsync();
		var firedAt = DateTime.Now;

		var task = executeAsync(null);

		// S1(CV10_LOCK_SLEEP_BEGIN_MS)が設定されていれば、勝者確定後に排他行がしばらく残るため、
		// 処理完了を待つ前に「自分の行が実際に見えるか」を軽くポーリングしておく（負けた側は行が無いので何も見えない）。
		var hasS1 = TryPositiveInt(Environment.GetEnvironmentVariable("CV10_LOCK_SLEEP_BEGIN_MS"), out _);
		SysSequence? seenOwn = null;
		if (hasS1) {
			var sw = System.Diagnostics.Stopwatch.StartNew();
			while (sw.ElapsedMilliseconds < 5_000 && !task.IsCompleted) {
				var rows = await FetchLockRowsAsync(session);
				seenOwn = rows.FirstOrDefault(x => x.TableName == ownTableName);
				if (seenOwn != null) break;
				await Task.Delay(50);
			}
		}

		await task;

		var errorDialogs = session.Dialogs.Where(x => x.Request.Kind == nameof(MessageEx.ShowErrorDialog)).ToList();
		var after = await FetchLockRowsAsync(session);

		if (!session.Check($"race({RaceLabel}) エラーダイアログは0件または1件(2件以上は矛盾)", errorDialogs.Count <= 1,
			new { errorDialogs = errorDialogs.Select(x => x.Request.Message) })) {
			session.Note("race:結果", new {
				label = RaceLabel, outcome = "ambiguous", errorCount = errorDialogs.Count,
				firedAt = firedAt.ToString("HH:mm:ss.fff"), afterRows = after.Count,
			});
			session.SetDialogResponder(null);
			return;
		}

		if (errorDialogs.Count == 0) {
			// 勝った: 自分がTryBeginを取得できた側
			session.Note("race:結果", new {
				label = RaceLabel, outcome = "win", ownTableName,
				firedAt = firedAt.ToString("HH:mm:ss.fff"), afterRows = after.Count,
			});
			if (hasS1) {
				// 矛盾チェック: 勝ったのにS1設定下で一度も自分の排他行が観測できないのはおかしい
				session.Check($"race({RaceLabel}) 勝った場合、処理中に自分(TableName={ownTableName})の排他行が観測できる(S1設定時)",
					seenOwn != null, new { seenOwn });
			}
			else {
				session.Note($"race({RaceLabel}) 処理中の自分の排他行の観測をスキップ",
					"CV10_LOCK_SLEEP_BEGIN_MS(S1)が未設定のため、処理が短時間で完了した可能性があります。");
			}
			session.Check($"race({RaceLabel}) 勝った場合、完了直後のSysSeqType=1行数は0または1(自分は解放済みのはず)",
				after.Count <= 1, new { after });
		}
		else {
			// 負けた: 先行（相手）が既に排他を取得していたため中断された側
			var body = errorDialogs[0].Request.Message;
			session.Note("race:結果", new {
				label = RaceLabel, outcome = "lose", ownTableName, body,
				firedAt = firedAt.ToString("HH:mm:ss.fff"), afterRows = after.Count,
			});
			session.Check($"race({RaceLabel}) 負けた場合、エラー本文に相手(先行={otherTableName})のTableNameが含まれる",
				body.Contains(otherTableName, StringComparison.Ordinal), new { body });
			session.Check($"race({RaceLabel}) 負けた場合、エラー本文に相手のColumnName相当(「{otherTableName} - 」)が含まれる",
				body.Contains($"{otherTableName} - ", StringComparison.Ordinal), new { body });
			session.Check($"race({RaceLabel}) 負けた場合、エラー本文に開始時刻・最終更新の文言が含まれる",
				body.Contains("開始", StringComparison.Ordinal) && body.Contains("最終更新", StringComparison.Ordinal), new { body });
			// 矛盾チェック: 負けた(=TryBeginでINSERTした自分の行はTOCTOU対策で即削除されている)のに、
			// 自分のTableNameの行が残っているのはおかしい
			session.Check($"race({RaceLabel}) 負けたのに自分(TableName={ownTableName})の行が残っていない",
				!after.Any(x => x.TableName == ownTableName), new { after });
		}

		session.SetDialogResponder(null);
	}

	/// <summary>
	/// <see cref="FireAt"/>まで待つ。残り時間が長い間は<see cref="Task.Delay(TimeSpan)"/>、
	/// 最後の約100msはスピンウェイトへ切り替え、プロセス間のズレを数十ms以内に収める。
	/// 未指定なら待たずに直ちに戻る。
	/// </summary>
	private static async Task WaitUntilFireTimeAsync() {
		if (FireAt is not { } fireAt) {
			return;
		}
		var spinMargin = TimeSpan.FromMilliseconds(100);
		var delay = fireAt - DateTime.Now - spinMargin;
		if (delay > TimeSpan.Zero) {
			await Task.Delay(delay);
		}
		while (DateTime.Now < fireAt) {
			System.Threading.Thread.SpinWait(200);
		}
	}

	private static Task<List<SysSequence>> FetchLockRowsAsync(VmSession session) =>
		session.QueryAsync<SysSequence>($"SELECT * FROM {nameof(SysSequence)} WHERE SysSeqType=1 ORDER BY Id");

	private static bool TryPositiveInt(string? raw, out int value) {
		value = 0;
		return !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out value) && value > 0;
	}
}
