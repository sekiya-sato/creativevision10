using System.Data;
using System.IO;
using System.Windows;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels._00System;
using CvWpfclient.ViewModels._31Monthly;
using CvWpfclient.Views._00System;
using CvWpfclient.Views._31Monthly;

namespace UatVm.Scenarios;

/// <summary>
/// マニュアル排他制御（正典 `Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md`）の実サーバE2E検証。
/// テスト計画は `Doc/test/2026-09-07_マニュアル排他制御_テスト計画.md`、
/// テストスイッチ手順は `Doc/test/2026-09-07_マニュアル排他制御_テストスイッチ手順.md` を参照する。
/// </summary>
/// <remarks>
/// <para>
/// E-01 → E-10 → E-02 → E-09 → E-11 → E-03(+E-08) → E-14 → E-07 → E-15 の順に1本のシナリオ内で検証する。
/// 排他行（<c>SysSequence.SysSeqType=1</c>）の状態が前のケースの前提になるため、順序に意味がある
/// （テスト計画書「内部で E-01 → E-10 → E-02 → E-03 の順に検証する」）。
/// E-11・E-14・E-15は手動観測手順書（<c>Doc/test/2026-09-07_マニュアル排他制御_手動観測手順.md</c>）
/// §4・§7・§8をシナリオ用に読み替えて追加したもの。E-11はE-09同様「排他行0件」が前提のため
/// E-09の直後、E-14はE-03と同じ「直接INSERT→強制クリア」の系統のためE-03の直後、
/// E-15はE-07と同じS3/S5前提を再利用するためE-07の直後に置く。
/// </para>
/// <para>
/// 環境変数スイッチ（S1〜S5、テストスイッチ手順書§2）はCvServer起動前に外部で設定する前提であり、
/// 本シナリオ側では一切設定しない（静的初期化で読まれるため手遅れになる。指示書のとおり）。
/// 現在の設定値は冒頭で <see cref="VmSession.Note"/> へ記録し、証跡から「どのスイッチで実行したか」を
/// 読めるようにする。スイッチが必要なケースで未設定の場合はそのケースだけを警告付きでスキップし、
/// 残りは続行する（FAILにはしない。人が手順を踏み忘れただけのため）。
/// </para>
/// <para>
/// 排他を取る13処理のうち、本シナリオで実際に実行するのは次の3つだけである。いずれも
/// 「1得意先／1仕入先」または「単一月」に絞れる画面であり、CancelDuringRebuildScenarioが
/// 在庫・掛再更新を広範囲で走らせているのとは逆に、<b>可能な限り狭い範囲</b>を選んで実処理時間を
/// 抑える方針を取った（指示書「可能な限り軽い処理・狭い範囲を選び、理由をコメントに残す」）。
/// <list type="bullet">
/// <item><description>
/// E-01・E-02の先行（占有）側: <see cref="BillingCalculationView"/>（請求計算）。
/// 得意先コード範囲を単一得意先（<see cref="TokuiCode"/>）に絞ることで、対象締日は
/// その得意先の1件だけに確定し、<c>StreamStepProgressRunner</c>のステップ数が必ず1になる
/// （<c>SummaryUriSeiAsyncStream</c>は締日ごとにステップが展開されるため、絞らないと複数になり得る）。
/// 対象データが無くても排他の取得・解放そのものは実行されるため、実データの有無に処理の軽さは
/// 依存しない。既存の<c>BillingCalculationScenario</c>と同じ得意先・月を再利用し、
/// 動作実績のある組み合わせであることを優先した。
/// </description></item>
/// <item><description>
/// E-02の後発（中断される）側: <see cref="PaymentCalculationView"/>（支払計算）。請求計算とは別の
/// 画面・別の一連処理名（「支払計算」）であり、指示書の「別の画面から別の処理を実行する」を満たす。
/// マニュアル排他は<see cref="CvDomainLogic.ManualLockDb"/>のドキュメントコメントにあるとおり
/// 「全体で1行」（処理名に関係ない）であるため、先行が「請求計算」でも後発「支払計算」は
/// 同じ理由で中断される。
/// </description></item>
/// <item><description>
/// E-07: <see cref="BillingCalculationView"/>（請求計算）を、E-01・E-02の先行側と同じ得意先・月で
/// 再利用する。請求計算は単一得意先・単一月に絞ってもStreamStepProgressRunner経由のステップ数が2になる
/// （実行ログ「処理開始 ステップ数=2」→「Summary : CalcSummaryUriSei (16日) (1/2)」→「(末日) (2/2)」）ため、
/// Progressが複数回呼ばれVduが前進し続ける長時間占有を作れる。「Progressを複数回呼ぶのは総平均原価更新
/// だけである」という記述は誤りだったため訂正した。原価画面（総平均原価更新）を使わないため実DBの
/// 原価データを書き換えず、S2/S3/S5のスイッチ3つが揃っていれば実行できる。
/// </description></item>
/// </list>
/// </para>
/// <para>
/// E-10・E-03で必要な「排他行が存在する状態」は、指示書が明示的に許可しているとおり
/// <see cref="SysSequence"/>への直接INSERT（<see cref="ExDatabaseSqlite"/>経由、CvServerとは別接続）で作る。
/// 実処理を長時間占有させるより確実であり、<c>ManualLockDbTests</c>等の単体テストが使っている
/// SQLite直接操作と同じ手法（既存の<c>ShimeBoundarySeeder</c>等のSeederが起動前に使う手法を、
/// サーバー稼働中に短時間だけ流用する形）である。作った行は各ケースの最後に必ず削除する。
/// </para>
/// </remarks>
public static class ManualLockScenario {
	// ==================================================================
	// 実行対象（既存動作実績のある組み合わせを再利用）
	// ==================================================================
	private const string BillingMonth = "2026/07";
	private const string TokuiCode = "000002";

	// 直接INSERTするテスト用の排他行を、実処理の一連処理名と混同しないよう分かる名前にする。
	private const string FakeLockTablePrefix = "UATVM-ManualLockTest";

	// 監視タスクのログ目印（CvDomainLogic/ManualLockDb.cs の private const と同じ値）。
	// 製品コードは変更せず、証跡照合のためだけにここへ複製する。
	private const string MonitorDetectedMarker = "[2b:検知]";
	private const string MonitorNormalEndMarker = "[2f:正常終了]";
	private const string MonitorTimeoutMarker = "[2e:タイムアウト解放]";

	public static async Task RunAsync(VmSession session) {
		var switches = ReadSwitches();
		session.Note("switches:現在の環境変数設定値（CvServer起動前に外部で設定する前提）", switches);

		var autoExecConfig = await FetchAutoExecConfigAsync(session);
		session.Note("switches:S4 監視タスクcron・実行フラグ(MasterConfig)", autoExecConfig);

		// 前提: 開始時点で排他行が残っていないこと。もし残っていれば前回実行の後始末漏れなので、
		// テスト用行として掃除してから続行する（実処理の行が残っていた場合は流さず警告のみに留める）。
		await EnsureCleanStateAsync(session);

		session.Note("case:開始", "E-01 排他制御の正常実行");
		await RunE01Async(session, switches, autoExecConfig);

		session.Note("case:開始", "E-10 排他中でもPreviewは動く");
		await RunE10Async(session);

		session.Note("case:開始", "E-02 実行中エラー（後発が中断）");
		await RunE02Async(session, switches);

		session.Note("case:開始", "E-09 排他行0件のUI分岐");
		await RunE09Async(session);

		session.Note("case:開始", "E-11 SysSeqType=0の連番行が排他・監視の双方に無影響");
		await RunE11Async(session, switches);

		session.Note("case:開始", "E-03 管理メニューからの解除 (+E-08 監視の2f)");
		await RunE03AndE08Async(session, autoExecConfig);

		session.Note("case:開始", "E-14 監視無効時は自動解放されず強制クリアが唯一の回復手段");
		await RunE14Async(session, autoExecConfig);

		session.Note("case:開始", "E-07 Vdu前進中は解放しない");
		await RunE07Async(session, switches);

		session.Note("case:開始", "E-15 SeqNo=99が残った状態からの自動解放");
		await RunE15Async(session, switches);

		session.Note("case:終了", "manuallock 全ケース完了");
	}

	// ==================================================================
	// 環境変数・MasterConfigの読み取り
	// ==================================================================

	private sealed record Switches(string? S1BeginMs, string? S2StepMs, string? S3MinThresholdMin, string? S5ExpectedSec) {
		public bool HasS1 => TryPositiveInt(S1BeginMs, out _);
		public bool HasS2 => TryPositiveInt(S2StepMs, out _);
		public bool HasS3 => TryPositiveInt(S3MinThresholdMin, out _);
		public bool HasS5 => TryPositiveInt(S5ExpectedSec, out _);
	}

	private static Switches ReadSwitches() => new(
		Environment.GetEnvironmentVariable("CV10_LOCK_SLEEP_BEGIN_MS"),
		Environment.GetEnvironmentVariable("CV10_LOCK_SLEEP_STEP_MS"),
		Environment.GetEnvironmentVariable("CV10_LOCK_MIN_THRESHOLD_MIN"),
		Environment.GetEnvironmentVariable("CV10_LOCK_EXPECTED_SEC"));

	private static bool TryPositiveInt(string? raw, out int value) {
		value = 0;
		return !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out value) && value > 0;
	}

	private sealed record AutoExecConfig(string CronVal, string EnabledVal) {
		/// <summary>
		/// 手順書のとおりS4を毎分実行へ変更済みか（E-01・E-08の監視ログ観測に必要）。
		/// cron式は5フィールド(分 時 日 月 曜日)で、CvServerは<c>CrontabSchedule.Parse</c>を
		/// オプション無しで呼ぶため分単位固定（秒フィールドは無い）。分フィールドだけを見て判定する。
		/// 旧実装は<c>CronVal.Contains("*/1")</c>で判定していたため、実際によく使われる`* * * * *`
		/// （分フィールドが`*`、これも毎分の意味）を誤って「短縮されていない」と判定していた。
		/// </summary>
		public bool IsEveryMinute {
			get {
				var minuteField = CronVal.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
				return minuteField is "*" or "*/1";
			}
		}
		/// <summary>監視タスクが有効か（E-08の観測に必要。手順書はE-02/E-03用に停止を指示している）。</summary>
		public bool IsEnabled => EnabledVal != "0";
	}

	private static async Task<AutoExecConfig> FetchAutoExecConfigAsync(VmSession session) {
		var cronName = MasterConfig.NameAutoExecCronPrefix + MasterConfig.AutoExecTaskIdManualLockMonitor[..8];
		var enabledName = MasterConfig.NameAutoExecEnabledPrefix + MasterConfig.AutoExecTaskIdManualLockMonitor[..8];
		var rows = await session.QueryAsync<MasterConfig>(
			$"SELECT * FROM {nameof(MasterConfig)} WHERE Category=@0 AND Name IN (@1, @2)",
			MasterConfig.CategoryAutoExec, cronName, enabledName);
		var cron = rows.FirstOrDefault(x => x.Name == cronName)?.Val ?? MasterConfig.AutoExecCronManualLockMonitor;
		var enabled = rows.FirstOrDefault(x => x.Name == enabledName)?.Val ?? MasterConfig.ValAutoExecEnabled;
		return new AutoExecConfig(cron, enabled);
	}

	// ==================================================================
	// 共通ヘルパー: DB読み取り（gRPC経由、既存Msg101_Op_Query）
	// ==================================================================

	private static Task<List<SysSequence>> FetchLockRowsAsync(VmSession session) =>
		session.QueryAsync<SysSequence>($"SELECT * FROM {nameof(SysSequence)} WHERE SysSeqType=1 ORDER BY Id");

	private static async Task<int> CountManualExecHistAsync(VmSession session, string taskName) {
		// SysHistType(int)はリテラルへ埋め込み、TaskName(文字列)だけをバインドする。
		// QueryAsyncのパラメータは文字列で渡るため、INTEGER列との比較は型不一致で常に不一致になりうる
		// (VmSession.QueryAsyncのドキュメントコメント参照)。
		var rows = await session.QueryAsync<SysHistAutoexec>(
			$"SELECT * FROM {nameof(SysHistAutoexec)} WHERE SysHistType=1 AND TaskName=@0 ORDER BY Id DESC LIMIT 200",
			taskName);
		return rows.Count;
	}

	private static Task<List<SysHistAutoexec>> FetchMonitorHistAsync(VmSession session) =>
		session.QueryAsync<SysHistAutoexec>(
			$"SELECT * FROM {nameof(SysHistAutoexec)} WHERE SysHistType=0 AND TaskName=@0 ORDER BY Id DESC LIMIT 50",
			MasterConfig.AutoExecTaskNameManualLockMonitor);

	// ==================================================================
	// 共通ヘルパー: 排他行の直接INSERT/DELETE（E-10・E-03用。指示書が明示的に許可）
	// ==================================================================

	/// <summary>
	/// 対象DB(<c>CvServer/server-user163.db</c>)のパスを、ハーネスのカレントディレクトリ
	/// （<c>CvWpfclient</c>フォルダ、<c>VmHost.RunOnStaThread</c>が設定する）から逆算する。
	/// </summary>
	private static string ResolveDbPath() {
		var repoRoot = Directory.GetParent(Environment.CurrentDirectory)?.FullName
			?? throw new InvalidOperationException("リポジトリルートを解決できませんでした。");
		return Path.Combine(repoRoot, "CvServer", "server-user163.db");
	}

	/// <summary>
	/// テスト用の排他行を直接INSERTする。CvServerとは別のSQLite接続を使う（WALモードのため短時間の
	/// 競合は許容される。README§5.5参照）。<paramref name="vduAgoSeconds"/>で「最終更新からの経過時間」を作る。
	/// <paramref name="sysSeqType"/>・<paramref name="seqNo"/>は既定値で既存呼び出しを壊さない
	/// （E-11はSysSeqType=0の行、E-15はSeqNo=99の行を作るために追加した）。
	/// </summary>
	private static Task<long> InsertFakeLockRowAsync(string tableName, string columnName, long expectedDurationSeconds, double vduAgoSeconds, string memo,
			int sysSeqType = (int)EmSysSeqType.ManualLock, int seqNo = 1) =>
		Task.Run(() => {
			var dbPath = ResolveDbPath();
			using var db = ExDatabaseSqlite.GetDbConn(dbPath);
			var now = DateTime.UtcNow.Ticks;
			var vdu = now - (long)(vduAgoSeconds * TimeSpan.TicksPerSecond);
			var row = new SysSequence {
				SysSeqType = sysSeqType,
				TableName = tableName,
				ColumnName = columnName,
				SeqNo = seqNo,
				Memo = memo,
				ExpectedDuration = expectedDurationSeconds,
				Vdc = vdu,
				Vdu = vdu,
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
	/// E-11用。<c>SysSeqType=0</c>の行は<see cref="FetchLockRowsAsync"/>（SysSeqType=1限定）では
	/// 見えないため、TableNameで直接引く。
	/// </summary>
	private static Task<List<SysSequence>> FetchRowsByTableNameAsync(VmSession session, string tableName) =>
		session.QueryAsync<SysSequence>($"SELECT * FROM {nameof(SysSequence)} WHERE TableName=@0 ORDER BY Id", tableName);

	/// <summary>
	/// E-14用。監視タスクの実行フラグ(MasterConfig)をCvServerとは別接続で書き換える。
	/// 実行フラグは発火の都度DBから読まれるため、静的初期化で読むS1〜S5の環境変数スイッチと異なり
	/// CvServer再起動なしに即時反映される。
	/// </summary>
	private static Task SetAutoExecEnabledAsync(string val) =>
		Task.Run(() => {
			var dbPath = ResolveDbPath();
			using var db = ExDatabaseSqlite.GetDbConn(dbPath);
			var name = MasterConfig.NameAutoExecEnabledPrefix + MasterConfig.AutoExecTaskIdManualLockMonitor[..8];
			db.Execute($"UPDATE {nameof(MasterConfig)} SET Val=@0 WHERE Category=@1 AND Name=@2",
				val, MasterConfig.CategoryAutoExec, name);
		});

	private static Task DeleteAllLockRowsAsync() =>
		Task.Run(() => {
			var dbPath = ResolveDbPath();
			using var db = ExDatabaseSqlite.GetDbConn(dbPath);
			db.Execute($"DELETE FROM {nameof(SysSequence)} WHERE SysSeqType=1");
		});

	private static async Task EnsureCleanStateAsync(VmSession session) {
		var rows = await FetchLockRowsAsync(session);
		if (rows.Count == 0) {
			return;
		}
		var isFake = rows.All(x => x.TableName.StartsWith(FakeLockTablePrefix, StringComparison.Ordinal));
		session.Note("前提確認:開始時点で排他行が残っている", new {
			rows = rows.Select(x => new { x.Id, x.TableName, x.ColumnName, x.Vdc, x.Vdu }),
			isFake,
		});
		if (isFake) {
			// 前回実行のテスト用行の掃除残りだけなら、無条件に削除して続行する。
			await DeleteAllLockRowsAsync();
			session.Note("前提確認:前回実行のテスト用行を掃除した", rows.Count);
		}
		else {
			// 実処理の行が残っている場合は、削除すると実際に動いている処理を壊しかねないため
			// 削除せず、以降のケースが前提を満たせないことだけを記録してスキップさせる。
			session.Fail("前提確認", "実処理由来と見られる排他行が既に存在するため、開始できません。手動で状態を確認してください。");
		}
	}

	// ==================================================================
	// E-01: 排他制御の正常実行
	// ==================================================================

	private static async Task RunE01Async(VmSession session, Switches switches, AutoExecConfig autoExecConfig) {
		var before = await FetchLockRowsAsync(session);
		if (!session.Check("E-01 開始前に排他行が無い", before.Count == 0, new { before })) {
			return;
		}
		var beforeHist = await CountManualExecHistAsync(session, "請求計算");
		// 前回実行が残した2b/2fを今回のものと誤認しないための基準点。これより新しいIdだけを照合する。
		var monitorBaselineId = (await FetchMonitorHistAsync(session)).Select(x => x.Id).DefaultIfEmpty(0).Max();

		var d = session.OpenView<BillingCalculationView, BillingCalculationViewModel>();
		// --hide-views指定時はView.Show()を呼ばないため、BaseWindow.OnContentRenderedが表示時に自動実行する
		// InitCommandが走らず、ShimeItemsが空のままになる（他のシナリオがWaitAsyncで待てているのは
		// ShowViews=trueで実際に描画されるため）。ViewModel自身のInitCommand
		// （BaseBillingCalculationViewModel.InitAsyncに[RelayCommand]が生成するIAsyncRelayCommand）を
		// RunAsyncで明示実行し、完了を待つことで代替する。
		await d.RunAsync("init:締日一覧の取得", vm => vm.InitCommand);
		d.Input("対象", vm => {
			vm.BillingMonth = BillingMonth;
			vm.TorihikiCodeFrom = TokuiCode;
			vm.TorihikiCodeTo = TokuiCode;
		}, new { BillingMonth, TokuiCode });

		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);

		// RunAsyncは完了まで待ってしまうため、開始直後の排他行を観測できない。
		// ViewDriver越しではなくVm自身のコマンド(公開プロパティ)を直接ExecuteAsyncし、
		// タスクを保持したまま先へ進める（ハーネス本体は無変更、公開APIの範囲内）。
		var task = d.Vm.ExecuteCommand.ExecuteAsync(null);

		// S2（Progressでの待機）が設定されていれば、行が消えるまでの間に十分な余地がある。
		// 未設定の場合は対象データが少なく一瞬で完了しうるため、観測できなくても失敗にはしない
		// （指示書のとおりスイッチ未設定はスキップ対象）。
		var seen = await PollForSingleLockRowAsync(session, timeoutMs: switches.HasS2 ? 10_000 : 1_500);
		if (seen != null) {
			session.Check("E-01 排他行が1件だけできTableNameが期待値(請求計算)", seen.TableName == "請求計算", new { seen.TableName, seen.ColumnName });
		}
		else {
			session.Note("E-01 排他行の途中観測をスキップ", "S2未設定等により処理が短時間で完了したため観測できませんでした。");
		}

		await task;
		d.Snapshot("完了後", vm => new { vm.StatusMessage, vm.ProgressValue, vm.IsProcessing });

		var after = await FetchLockRowsAsync(session);
		session.Check("E-01 完了後に排他行が消えている", after.Count == 0, new { after });

		var afterHist = await CountManualExecHistAsync(session, "請求計算");
		session.Check("E-01 SysHistAutoexecにSysHistType=1(手動実行)の行が増えている", afterHist > beforeHist, new { beforeHist, afterHist });

		await CheckMonitorPairAsync(session, "E-01", autoExecConfig, monitorBaselineId);

		session.SetDialogResponder(null);
	}

	/// <summary>
	/// 排他行が1件出現するまで短い間隔でポーリングする。出現しなければnullを返す（失敗にはしない）。
	/// </summary>
	private static async Task<SysSequence?> PollForSingleLockRowAsync(VmSession session, int timeoutMs, int pollMs = 100) {
		var sw = System.Diagnostics.Stopwatch.StartNew();
		while (sw.ElapsedMilliseconds < timeoutMs) {
			var rows = await FetchLockRowsAsync(session);
			if (rows.Count > 0) {
				return rows[0];
			}
			await Task.Delay(pollMs);
		}
		return null;
	}

	/// <summary>
	/// 監視タスクのログ（TaskName=監視タスク名）が2b→2fの対になっているかを確認できるなら確認する。
	/// <para>
	/// 処理完了直後の1回だけの読み取りでは、2b（検知）は記録済みでも2f（正常終了）はまだ監視タスクの
	/// 次回tickを待っている途中であることが多く、「スキップ」と誤判定していた（実DBでは
	/// [2b:検知]→処理側履歴→[2f:正常終了]の順で対が成立していた）。2bを確認できたら、2fが現れるまで
	/// 最大約150秒（cron 1分の2tick分＋余裕）、10秒間隔でポーリングする。
	/// </para>
	/// 監視が無効、または毎分実行でない場合はこれまでどおり理由を残してスキップする（テスト計画書E-01の要件どおり）。
	/// 時間内に2fが出なければ理由を残してスキップする（FAILにはしない）。
	/// </summary>
	private static async Task CheckMonitorPairAsync(VmSession session, string caseId, AutoExecConfig autoExecConfig, long baselineId) {
		if (!autoExecConfig.IsEnabled) {
			session.Note($"{caseId} 監視ログ2b→2fの対の確認をスキップ", "監視タスクの実行フラグがOFFのため観測できません。");
			return;
		}
		if (!autoExecConfig.IsEveryMinute) {
			session.Note($"{caseId} 監視ログ2b→2fの対の確認をスキップ", "S4(cron)が毎分実行になっていないため、既定間隔では観測できません。");
			return;
		}

		// 対象を取り違えないよう、Memoに当該処理名(請求計算)が含まれ、かつ本ケース開始後に
		// 記録された行(Idが基準点より新しい)だけを見る。基準点が無いと前回実行が残した2fで誤PASSになる。
		bool IsTarget(SysHistAutoexec x, string marker) =>
			x.Id > baselineId
			&& x.Memo.StartsWith(marker, StringComparison.Ordinal)
			&& x.Memo.Contains("請求計算", StringComparison.Ordinal);

		var hist = await FetchMonitorHistAsync(session);
		if (!hist.Any(x => IsTarget(x, MonitorDetectedMarker))) {
			session.Note($"{caseId} 監視ログ2b→2fの対の確認をスキップ", "2b(検知)がまだ記録されていないため観測できませんでした。");
			return;
		}

		var found = false;
		List<SysHistAutoexec> recent = [];
		for (var i = 0; i < 15 && !found; i++) {
			await Task.Delay(10_000);
			recent = await FetchMonitorHistAsync(session);
			found = recent.Any(x => IsTarget(x, MonitorNormalEndMarker));
		}

		if (found) {
			session.Check($"{caseId} 監視ログが2b→2fの対になっている", true,
				new { recent = recent.Take(4).Select(x => new { x.Memo, x.StartTime, x.EndTime }) });
		}
		else {
			session.Note($"{caseId} 監視ログ2b→2fの対の確認をスキップ", "2b(検知)は確認できましたが、150秒以内に2f(正常終了)が記録されず観測できませんでした。");
		}
	}

	// ==================================================================
	// E-10: 排他中でもPreviewは動く
	// ==================================================================

	private static async Task RunE10Async(VmSession session) {
		var lockId = await InsertFakeLockRowAsync(
			$"{FakeLockTablePrefix}-E10", "確認(プレビュー)は対象外", expectedDurationSeconds: 600, vduAgoSeconds: 5,
			memo: "E-10: 排他中でもPreviewが動くことの確認用（直接INSERT）");
		session.Note("E-10 テスト用排他行を直接INSERT", new { lockId });
		try {
			var rows = await FetchLockRowsAsync(session);
			session.Check("E-10 排他行が存在する状態になっている", rows.Any(x => x.Id == lockId), new { rows });

			var d = session.OpenView<LastPurchaseCostRefreshView, LastPurchaseCostRefreshViewModel>();
			await d.WaitAsync("init:状態取得", vm => vm.ProcessStatusText != "－" || !string.IsNullOrEmpty(vm.StatusMessage));
			d.Input("対象月", vm => vm.TargetMonth = BillingMonth, new { TargetMonth = BillingMonth });

			session.ClearDialogs();
			session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.OK);
			await d.RunAsync("confirm:最終仕入原価Preview", vm => vm.ConfirmCommand);

			var errorDialogs = session.Dialogs.Where(x => x.Request.Kind == nameof(MessageEx.ShowErrorDialog)).ToList();
			session.Check("E-10 排他行が存在してもPreview(確認)がエラーにならない", errorDialogs.Count == 0,
				new { d.Vm.StatusMessage, errors = errorDialogs.Select(x => x.Request.Message) });
		}
		finally {
			await DeleteLockRowByIdAsync(lockId);
			var after = await FetchLockRowsAsync(session);
			session.Check("E-10 テスト用排他行を削除した", !after.Any(x => x.Id == lockId), new { after });
			session.SetDialogResponder(null);
		}
	}

	// ==================================================================
	// E-02: 排他制御で他タスクの実行中エラー（後発が中断）
	// ==================================================================

	private static async Task RunE02Async(VmSession session, Switches switches) {
		if (!switches.HasS1) {
			session.Note("E-02 スキップ", "CV10_LOCK_SLEEP_BEGIN_MS(S1)が未設定のため、先行処理を安定して占有できません。"
				+ "テストスイッチ手順書のE-02設定（S1=60000）を適用してCvServerを起動し直してください。");
			return;
		}
		var before = await FetchLockRowsAsync(session);
		if (!session.Check("E-02 開始前に排他行が無い", before.Count == 0, new { before })) {
			return;
		}

		var occupier = session.OpenView<BillingCalculationView, BillingCalculationViewModel>();
		// 修正1と同じ理由（--hide-viewsでは自動実行されるInitCommandが走らない）でRunAsyncにより明示実行する。
		await occupier.RunAsync("init:締日一覧の取得(先行)", vm => vm.InitCommand);
		occupier.Input("対象(先行=請求計算)", vm => {
			vm.BillingMonth = BillingMonth;
			vm.TorihikiCodeFrom = TokuiCode;
			vm.TorihikiCodeTo = TokuiCode;
		}, new { BillingMonth, TokuiCode });

		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);

		// 1プロセス内で「先行処理を走らせながら別処理を起動する」ことがハーネスAPIで可能かを確認した結果:
		// ViewDriver.RunAsyncは完了まで待つ作りだが、Vm自身のIAsyncRelayCommand.ExecuteAsyncは
		// publicプロパティ経由でawaitせずに呼べる（ハーネス本体を変更する必要はない）。
		// S1（勝者確定直後のThread.Sleep）が設定されていれば、この間ずっと排他行が残るため、
		// 後発を安全な時間差で起動できる。
		var occupierTask = occupier.Vm.ExecuteCommand.ExecuteAsync(null);

		// TryBeginがINSERTを終えるまでの猶予。S1の値が大きいほど猶予は余裕があるが、
		// 短くても実処理側のSQL自体は単一得意先のみで軽いため、TryBegin完了は数十ms程度で十分なはずである。
		var occupierLock = await PollForSingleLockRowAsync(session, timeoutMs: 5_000);
		if (!session.Check("E-02 先行処理が排他を取得した(行が1件確認できる)", occupierLock != null, new { occupierLock })) {
			await occupierTask;
			session.SetDialogResponder(null);
			return;
		}

		var beforeSecondCount = (await FetchLockRowsAsync(session)).Count;

		var second = session.OpenView<PaymentCalculationView, PaymentCalculationViewModel>();
		// 修正1と同じ理由でRunAsyncにより明示実行する。
		await second.RunAsync("init:締日一覧の取得(後発)", vm => vm.InitCommand);
		var shiireRows = await session.QueryAsync<MasterShiire>($"SELECT * FROM {nameof(MasterShiire)} ORDER BY Id LIMIT 1");
		if (shiireRows.Count == 0) {
			session.Fail("E-02", "MasterShiireに1件も無いため、支払計算を起動できません。");
			await occupierTask;
			session.SetDialogResponder(null);
			return;
		}
		var shiireCode = shiireRows[0].Code;
		second.Input("対象(後発=支払計算)", vm => {
			vm.BillingMonth = BillingMonth;
			vm.TorihikiCodeFrom = shiireCode;
			vm.TorihikiCodeTo = shiireCode;
		}, new { BillingMonth, shiireCode });

		session.ClearDialogs();
		await second.RunAsync("execute:支払計算(後発、中断される想定)", vm => vm.ExecuteCommand);

		var errorDialogs = session.Dialogs.Where(x => x.Request.Kind == nameof(MessageEx.ShowErrorDialog)).ToList();
		session.Check("E-02 後発がエラーとして画面へ戻る", errorDialogs.Count == 1, new { errors = errorDialogs.Select(x => x.Request.Message) });

		if (errorDialogs.Count > 0) {
			var body = errorDialogs[0].Request.Message;
			session.Check("E-02 エラー本文に先行処理のTableName(請求計算)が含まれる", body.Contains("請求計算", StringComparison.Ordinal), new { body });
			session.Check("E-02 エラー本文に先行処理のColumnName(締日ラベルを含むステップ名)が含まれる",
				body.Contains("Summary : CalcSummaryUriSei", StringComparison.Ordinal), new { body });
			session.Check("E-02 エラー本文に開始時刻に相当する内容(「開始」の文言と日時)が含まれる",
				body.Contains("開始", StringComparison.Ordinal) && body.Contains("最終更新", StringComparison.Ordinal), new { body });
		}

		var afterSecondCount = (await FetchLockRowsAsync(session)).Count;
		session.Check("E-02 後発の中断で排他行が増えていない(自分の行を残さない)", afterSecondCount == beforeSecondCount,
			new { beforeSecondCount, afterSecondCount });

		await occupierTask;
		var afterOccupier = await FetchLockRowsAsync(session);
		session.Check("E-02 先行処理の完了で排他行が消えている", afterOccupier.Count == 0, new { afterOccupier });

		session.SetDialogResponder(null);
	}

	// ==================================================================
	// E-09: 排他行0件のUI分岐
	// ==================================================================

	private static async Task RunE09Async(VmSession session) {
		var rows = await FetchLockRowsAsync(session);
		if (!session.Check("E-09 前提: 排他行が0件である", rows.Count == 0, new { rows })) {
			return;
		}

		var d = session.OpenView<SysExecMiscView, SysExecMiscViewModel>();

		session.ClearDialogs();
		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.OK);
		await d.RunAsync("execute:ManualLockClear(0件)", vm => vm.ManualLockClearCommand);

		var confirmDialogs = session.Dialogs.Where(x => x.Request.Button == MessageBoxButton.YesNo).ToList();
		session.Check("E-09 確認ダイアログが出ない", confirmDialogs.Count == 0, new { confirmDialogs = confirmDialogs.Select(x => x.Request.Message) });
		session.CheckEqual("E-09 ResultMessageが「マニュアル排他制御は掛かっていません。」", "マニュアル排他制御は掛かっていません。", d.Vm.ResultMessage);

		session.SetDialogResponder(null);
	}

	// ==================================================================
	// E-11: SysSeqType=0の連番行が排他・監視の双方に無影響 (L-12/L-17)
	// ==================================================================

	/// <summary>
	/// 手動観測手順書§4のシナリオ読み替え版。<c>SysSeqType=0</c>の行を作る運用経路が無いため、
	/// E-10・E-03と同様に直接INSERTで作る。
	/// </summary>
	private static async Task RunE11Async(VmSession session, Switches switches) {
		var before = await FetchLockRowsAsync(session);
		if (!session.Check("E-11 開始前に排他行(SysSeqType=1)が無い", before.Count == 0, new { before })) {
			return;
		}

		var tableName = $"{FakeLockTablePrefix}-E11";
		// 監視ログの誤判定防止（E-01のmonitorBaselineIdと同じ手法）。
		var monitorBaselineId = (await FetchMonitorHistAsync(session)).Select(x => x.Id).DefaultIfEmpty(0).Max();

		var lockId = await InsertFakeLockRowAsync(tableName, "connOK", expectedDurationSeconds: 0, vduAgoSeconds: 0,
			memo: "E-11: SysSeqType=0の連番行が排他・監視に無影響なことの確認用（直接INSERT）",
			sysSeqType: 0);
		session.Note("E-11 SysSeqType=0のテスト用行を直接INSERT", new { lockId });

		try {
			var seqRows = await FetchRowsByTableNameAsync(session, tableName);
			session.Check("E-11 SysSeqType=0の行が存在する", seqRows.Any(x => x.Id == lockId && x.SysSeqType == 0), new { seqRows });

			// (1) 排他判定に影響しないこと: 単一得意先の請求計算が正常に開始できることを確認する
			// （完走までは待たない。開始できたことが確認できたら片付ける、E-01と同じ作法）。
			var d = session.OpenView<BillingCalculationView, BillingCalculationViewModel>();
			await d.RunAsync("init:締日一覧の取得", vm => vm.InitCommand);
			d.Input("対象", vm => {
				vm.BillingMonth = BillingMonth;
				vm.TorihikiCodeFrom = TokuiCode;
				vm.TorihikiCodeTo = TokuiCode;
			}, new { BillingMonth, TokuiCode });

			session.ClearDialogs();
			session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);
			var task = d.Vm.ExecuteCommand.ExecuteAsync(null);

			var seen = await PollForSingleLockRowAsync(session, timeoutMs: switches.HasS2 ? 10_000 : 1_500);
			if (seen != null) {
				session.Check("E-11 SysSeqType=0の行があっても請求計算の排他行(SysSeqType=1)が1件できる(開始できる)",
					seen.TableName == "請求計算", new { seen.TableName });
			}
			else {
				session.Note("E-11 排他行の途中観測をスキップ", "S2未設定等により処理が短時間で完了したため観測できませんでした。");
			}

			await task;
			d.Snapshot("完了後", vm => new { vm.StatusMessage, vm.IsProcessing });

			var afterMain = await FetchLockRowsAsync(session);
			session.Check("E-11 請求計算完了後にSysSeqType=1の行が消えている", afterMain.Count == 0, new { afterMain });

			// (2) 監視が無視すること: 監視ログにE-11の行への言及が一切ないことを確認する
			// （長く待つ必要はない。数tick待つ実装にはしない）。
			var hist = await FetchMonitorHistAsync(session);
			var mentions = hist.Where(x => x.Id > monitorBaselineId && x.Memo.Contains(tableName, StringComparison.Ordinal)).ToList();
			session.Check("E-11 監視ログがSysSeqType=0の行に一切言及しない", mentions.Count == 0, new { mentions = mentions.Select(x => x.Memo) });

			// (3) 強制クリアがSysSeqType=1だけを消すこと(L-17)。
			// SysSeqType=1の行が1件も無い状態でクリアしても「何も消さなかった」だけで検証にならないため、
			// 使い捨ての排他行を1件作ってから実行する。
			var victimId = await InsertFakeLockRowAsync($"{FakeLockTablePrefix}-E11-victim", "強制クリア対象", 600, 5,
				memo: "E-11: 強制クリアがSysSeqType=1だけを消すことの確認用（直接INSERT）");
			session.ClearDialogs();
			var clearView = session.OpenView<SysExecMiscView, SysExecMiscViewModel>();
			session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);
			await clearView.RunAsync("execute:ManualLockClear(SysSeqType=0が残ることの確認)", vm => vm.ManualLockClearCommand);

			var afterClearType1 = await FetchLockRowsAsync(session);
			session.Check("E-11 強制クリアでSysSeqType=1の行は消える", !afterClearType1.Any(x => x.Id == victimId), new { afterClearType1 });
			var afterClear = await FetchRowsByTableNameAsync(session, tableName);
			session.Check("E-11 強制クリア後もSysSeqType=0の行は残る", afterClear.Any(x => x.Id == lockId), new { afterClear });
		}
		finally {
			await DeleteLockRowByIdAsync(lockId);
			var afterDelete = await FetchRowsByTableNameAsync(session, tableName);
			session.Check("E-11 テスト用行(SysSeqType=0)を削除した", !afterDelete.Any(x => x.Id == lockId), new { afterDelete });
			session.SetDialogResponder(null);
		}
	}

	// ==================================================================
	// E-03: 管理メニューからの解除 (+ E-08: 監視の2f)
	// ==================================================================

	private static async Task RunE03AndE08Async(VmSession session, AutoExecConfig autoExecConfig) {
		var beforeHist = await CountManualExecHistAsync(session, ManualLockClearTaskNameLiteral);

		// 排他行を2行、直接INSERTで作る（指示書が明示的に許可。実処理を長時間占有させるより確実）。
		// Vduを数秒前にすることで、既定の閾値(15分)より確実に短く、IsLikelyAlive=trueとなり
		// 「まだ動いている可能性があります」の警告が必ず付く状態を作る(設計書§2.5.2)。
		var lockId1 = await InsertFakeLockRowAsync($"{FakeLockTablePrefix}-E03-A", "処理中(疑似)", 600, 5,
			"E-03: 強制クリアの確認本文検証用（直接INSERT、1行目）");
		var lockId2 = await InsertFakeLockRowAsync($"{FakeLockTablePrefix}-E03-B", "処理中(疑似)", 900, 8,
			"E-03: 強制クリアの確認本文検証用（直接INSERT、2行目）");
		session.Note("E-03 テスト用排他行を2行直接INSERT", new { lockId1, lockId2 });

		var d = session.OpenView<SysExecMiscView, SysExecMiscViewModel>();
		session.ClearDialogs();
		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);
		await d.RunAsync("execute:ManualLockClear(2件)", vm => vm.ManualLockClearCommand);

		var confirmDialogs = session.Dialogs.Where(x => x.Request.Button == MessageBoxButton.YesNo).ToList();
		if (session.Check("E-03 確認ダイアログが出る", confirmDialogs.Count == 1, new { confirmDialogs.Count })) {
			var body = confirmDialogs[0].Request.Message;
			session.Check("E-03 本文に最終更新日時が含まれる", body.Contains("最終更新日時", StringComparison.Ordinal), new { body });
			session.Check("E-03 本文に最終更新からの経過時間が含まれる", body.Contains("経過時間", StringComparison.Ordinal), new { body });
			session.Check("E-03 本文に両方のTableNameが含まれる",
				body.Contains($"{FakeLockTablePrefix}-E03-A", StringComparison.Ordinal) && body.Contains($"{FakeLockTablePrefix}-E03-B", StringComparison.Ordinal),
				new { body });
			session.Check("E-03 経過時間が閾値未満のため冒頭に「まだ動いている可能性があります」の警告が付く",
				body.Contains("まだ動いている可能性があります", StringComparison.Ordinal), new { body });
		}

		var afterRows = await FetchLockRowsAsync(session);
		session.Check("E-03 強制クリアでSysSeqType=1が0件になる", afterRows.Count == 0, new { afterRows });

		var afterHist = await CountManualExecHistAsync(session, ManualLockClearTaskNameLiteral);
		session.Check("E-03 SysHistAutoexecにTaskName='マニュアル排他制御クリア'の行が増えている", afterHist > beforeHist, new { beforeHist, afterHist });

		session.SetDialogResponder(null);

		await RunE08Async(session, autoExecConfig);
	}

	// SysHistAutoexec.TaskName（CvDomainLogic/ManualLockDb.cs の ManualLockClearTaskName と同じ文字列）。
	// 製品コードのconstは別アセンブリの内部実装詳細のため、証跡照合用にリテラルで複製する。
	private const string ManualLockClearTaskNameLiteral = "マニュアル排他制御クリア";

	/// <summary>
	/// 強制クリア後に監視タスクが2f（正常終了）で記録するかを、可能なら確認する。
	/// 監視タスクが2bで検知するには、行の存在中に監視のcron(既定5分、テスト時は1分)が
	/// 最低1回tickしている必要がある。直接INSERTした行は数秒しか存在しないため、
	/// 通常は観測できない。監視が無効化されている場合も含め、確認できなければ理由を残してスキップする。
	/// </summary>
	private static async Task RunE08Async(VmSession session, AutoExecConfig autoExecConfig) {
		if (!autoExecConfig.IsEnabled) {
			session.Note("E-08 スキップ", "監視タスクの実行フラグがOFFのため(E-02/E-03用のテストスイッチ手順のとおり)、2fを観測できません。"
				+ "確認するには監視タスクを再度ONにしてから、排他行が実際に存在する状態を作る必要があります。");
			return;
		}

		// cronが毎分実行でなければ、既定5分の間に本ケースの排他行がとうに消えてしまい観測できない。
		if (!autoExecConfig.IsEveryMinute) {
			session.Note("E-08 スキップ", "S4(cron)が毎分実行へ変更されていないため、既定5分の間隔では観測できません。");
			return;
		}

		var beforeCount = (await FetchMonitorHistAsync(session)).Count;
		var found = false;
		for (var i = 0; i < 8 && !found; i++) {
			await Task.Delay(10_000);
			var hist = await FetchMonitorHistAsync(session);
			found = hist.Count > beforeCount && hist.Take(hist.Count - beforeCount).Any(x => x.Memo.StartsWith(MonitorNormalEndMarker, StringComparison.Ordinal));
		}

		if (found) {
			session.Check("E-08 強制クリア後に監視が2f(正常終了)で記録する", true);
		}
		else {
			session.Note("E-08 スキップ", "直接INSERTした排他行は数秒しか存在しないため、監視のcron(1分)が検知(2b)する前に"
				+ "強制クリアで消えてしまい、2b起点の2fが記録されませんでした。実処理を長時間占有させたうえで"
				+ "強制クリアする手順でなければ再現できない可能性があります。");
		}
	}

	// ==================================================================
	// E-14: 監視無効時は自動解放されず強制クリアが唯一の回復手段
	// ==================================================================

	/// <summary>
	/// 手動観測手順書§7のシナリオ読み替え版。E-03のコードをほぼ流用する。
	/// 監視の実行フラグは発火の都度DBから読まれるため即時反映される（S1〜S5と違い再起動不要）。
	/// </summary>
	private static async Task RunE14Async(VmSession session, AutoExecConfig autoExecConfig) {
		if (!autoExecConfig.IsEveryMinute) {
			session.Note("E-14 スキップ", "S4(cron)が毎分実行になっていないため、既定間隔(5分)では"
				+ "『自動解放されないこと』を短時間で確認できません。");
			return;
		}
		var before = await FetchLockRowsAsync(session);
		if (!session.Check("E-14 開始前に排他行が無い", before.Count == 0, new { before })) {
			return;
		}

		var tableName = $"{FakeLockTablePrefix}-E14";
		var beforeHist = await CountManualExecHistAsync(session, ManualLockClearTaskNameLiteral);

		await SetAutoExecEnabledAsync("0");
		session.Note("E-14 監視の実行フラグを0へ変更した(即時反映)", (object?)null);
		var lockId = 0L;
		try {
			// ExpectedDuration=600のときの既定閾値はmax(600*2,15分)=20分。Vduを25分前にして
			// 確実に閾値超過の状態を作る（手順書§7.3手順2と同じ）。
			lockId = await InsertFakeLockRowAsync(tableName, "観測用", expectedDurationSeconds: 600, vduAgoSeconds: 25 * 60,
				memo: "E-14: 監視無効時は自動解放されないことの確認用（直接INSERT）");
			session.Note("E-14 テスト用排他行を直接INSERT(Vduを25分前に)", new { lockId });

			// 監視無効の間は閾値を超えても解放されないはず。cronが毎分実行の前提で2〜3tick=約150秒待つ
			// （待ちは極力短く。cronが毎分でない場合はケース冒頭でスキップ済み）。
			var released = false;
			for (var i = 0; i < 15 && !released; i++) {
				await Task.Delay(10_000);
				var rows = await FetchLockRowsAsync(session);
				released = !rows.Any(x => x.Id == lockId);
			}
			session.Check("E-14 監視無効の間は約150秒待っても自動解放されない", !released, new { released });

			var d = session.OpenView<SysExecMiscView, SysExecMiscViewModel>();
			session.ClearDialogs();
			session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);
			await d.RunAsync("execute:ManualLockClear(E-14)", vm => vm.ManualLockClearCommand);

			var confirmDialogs = session.Dialogs.Where(x => x.Request.Button == MessageBoxButton.YesNo).ToList();
			session.Check("E-14 強制クリアの確認ダイアログが出る", confirmDialogs.Count == 1, new { confirmDialogs.Count });

			var afterClear = await FetchLockRowsAsync(session);
			session.Check("E-14 強制クリアで排他行が消える(監視無効時の唯一の回復手段)", !afterClear.Any(x => x.Id == lockId), new { afterClear });

			var afterHist = await CountManualExecHistAsync(session, ManualLockClearTaskNameLiteral);
			session.Check("E-14 SysHistAutoexecにTaskName='マニュアル排他制御クリア'の行が増えている", afterHist > beforeHist, new { beforeHist, afterHist });

			session.SetDialogResponder(null);
		}
		finally {
			await SetAutoExecEnabledAsync("1");
			session.Note("E-14 監視の実行フラグを1へ復元した", (object?)null);
			// 強制クリアに至らなかった場合、監視を無効にしている間に作った行が残り後続ケースの前提を壊すため必ず消す。
			if (lockId != 0 && (await FetchLockRowsAsync(session)).Any(x => x.Id == lockId)) {
				await DeleteLockRowByIdAsync(lockId);
				session.Note("E-14 後始末:残っていたテスト用行を削除した", lockId);
			}
		}
	}

	// ==================================================================
	// E-07: Vdu前進中は解放しない
	// ==================================================================

	private static async Task RunE07Async(VmSession session, Switches switches) {
		// E-07は請求計算(ExecuteCommand)を使う。単一得意先・単一月に絞っても
		// StreamStepProgressRunner経由のステップ数が2になる（実行ログ: 処理開始 ステップ数=2 →
		// Summary : CalcSummaryUriSei (16日) (1/2) → (末日) (2/2)）ため、Progressが複数回呼ばれVduが前進する。
		// 総平均原価更新を使わずに済むため実DBの原価データを書き換えず、--allow-cost-update のような
		// 明示同意は不要になった（このためAllowCostUpdateは削除した）。
		//
		// 前提: S2/S3/S5の3つすべてが設定されていること。
		// S5でExpectedDurationを小さく(例10秒)し、S3で下限を1分にしないと、閾値が
		// max(ExpectedDuration×2, 15分)のままになり処理時間が閾値に届かないため、
		// 「閾値を超えてもVdu前進なら解放されない」ことを実証できない。
		if (!switches.HasS2 || !switches.HasS3 || !switches.HasS5) {
			session.Note("E-07 スキップ", "CV10_LOCK_SLEEP_STEP_MS(S2)/CV10_LOCK_MIN_THRESHOLD_MIN(S3)/CV10_LOCK_EXPECTED_SEC(S5)の"
				+ "いずれかが未設定のため実行できません。S5でExpectedDurationを小さく(例10秒)し、S3で下限を1分にしないと"
				+ "閾値がmax(ExpectedDuration×2, 15分)のままとなり、処理時間が閾値に届かず「閾値を超えてもVdu前進なら"
				+ "解放されない」ことを実証できません。");
			return;
		}
		var before = await FetchLockRowsAsync(session);
		if (!session.Check("E-07 開始前に排他行が無い", before.Count == 0, new { before })) {
			return;
		}

		var d = session.OpenView<BillingCalculationView, BillingCalculationViewModel>();
		// 修正1と同じ理由（--hide-viewsでは自動実行されるInitCommandが走らない）でRunAsyncにより明示実行する。
		await d.RunAsync("init:締日一覧の取得", vm => vm.InitCommand);
		d.Input("対象", vm => {
			vm.BillingMonth = BillingMonth;
			vm.TorihikiCodeFrom = TokuiCode;
			vm.TorihikiCodeTo = TokuiCode;
		}, new { BillingMonth, TokuiCode });

		session.ClearDialogs();
		session.SetDialogResponder(request => request.Button == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK);
		var task = d.Vm.ExecuteCommand.ExecuteAsync(null);

		// 「経過時間が閾値(S3=1分想定)を超えた状態でもVdu前進により解放されない」ことが要点のため、
		// サンプリング期間が閾値を超えるまで観測を続ける。10秒間隔で最大20回(約200秒、1分を十分に超える)。
		// あわせて[2e:タイムアウト解放]が記録されないこと、行が途中で消えないことも見る。
		// 監視が解放(2e)すれば行そのものが消えるため、行の生存だけを見れば足りる。
		// 監視ログの2eを別途走査する必要はない。
		var vduSamples = new List<long>();
		var rowMissingWhileRunning = false;
		for (var i = 0; i < 20 && !task.IsCompleted; i++) {
			await Task.Delay(10_000);
			if (task.IsCompleted) {
				break;
			}
			var rows = await FetchLockRowsAsync(session);
			var mine = rows.FirstOrDefault(x => x.TableName == "請求計算");
			if (mine != null) {
				vduSamples.Add(mine.Vdu);
			}
			else if (vduSamples.Count > 0) {
				rowMissingWhileRunning = true;
			}
		}

		await task;
		d.Snapshot("完了後", vm => new { vm.StatusMessage, vm.ProgressValue, vm.IsProcessing });

		if (vduSamples.Count >= 2) {
			var advanced = vduSamples.Zip(vduSamples.Skip(1), (a, b) => b >= a).All(x => x);
			session.Check("E-07 占有中Vduが単調に前進し続ける", advanced, new { vduSamples });
		}
		else {
			session.Note("E-07 Vdu前進の複数回観測をスキップ", $"観測できたVduサンプルが{vduSamples.Count}件のみでした（処理が短時間で終わった可能性があります）。");
		}
		session.Check("E-07 占有中に行が消えない(Vdu前進中は監視に解放されない)", !rowMissingWhileRunning);

		var after = await FetchLockRowsAsync(session);
		session.Check("E-07 完了後に排他行が消えている(完走で解放される)", after.Count == 0, new { after });

		session.SetDialogResponder(null);
	}

	// ==================================================================
	// E-15: SeqNo=99が残った状態からの自動解放 (L-07)
	// ==================================================================

	/// <summary>
	/// 手動観測手順書§8のシナリオ読み替え版。<c>ManualLockDb.Complete</c>内部
	/// （SeqNo=99書き込み→履歴INSERT→行DELETEの3ステップ）にテスト用フックが無く、
	/// その途中でプロセスを落とす窓を作れないため、「途中で落ちた結果の状態」をSQLiteへの
	/// 直接INSERTで代替して作る（実際のクラッシュ再現ではない。手順書§8.1と同趣旨）。
	/// 前提はE-07と同じS3/S5（閾値をmax(ExpectedDuration×2, S3分)まで短縮するため）。
	/// </summary>
	private static async Task RunE15Async(VmSession session, Switches switches) {
		if (!switches.HasS3 || !switches.HasS5) {
			session.Note("E-15 スキップ", "CV10_LOCK_MIN_THRESHOLD_MIN(S3)/CV10_LOCK_EXPECTED_SEC(S5)のいずれかが"
				+ "未設定のため実行できません。閾値はmax(ExpectedDuration×2, S3分)のため、S5でExpectedDurationを"
				+ "小さくしS3で下限を1分にしないと、短時間で閾値超過を作れません。");
			return;
		}

		var tableName = $"{FakeLockTablePrefix}-E15";
		// 前回実行が残した2b/2eで誤判定しないための基準点(E-01のCheckMonitorPairAsyncと同じ手法)。
		var monitorBaselineId = (await FetchMonitorHistAsync(session)).Select(x => x.Id).DefaultIfEmpty(0).Max();

		// ExpectedDuration=10のときの閾値はmax(10*2,S3分)。Vduを2分前にしておけば
		// S3=1分の前提で確実に閾値超過となる（手順書§8.3手順1と同じ）。
		var lockId = await InsertFakeLockRowAsync(tableName, "終了処理中(疑似)", expectedDurationSeconds: 10, vduAgoSeconds: 120,
			memo: "E-15: SeqNo=99が残った状態からの自動解放の確認用（Completeの途中で落ちた状態のSQL代替、直接INSERT）",
			seqNo: 99);
		session.Note("E-15 SeqNo=99・Vduを2分前にしたテスト用行を直接INSERT", new { lockId });

		try {
			bool IsTarget(SysHistAutoexec x, string marker) =>
				x.Id > monitorBaselineId
				&& x.Memo.StartsWith(marker, StringComparison.Ordinal)
				&& x.Memo.Contains(tableName, StringComparison.Ordinal);

			var found = false;
			List<SysHistAutoexec> recent = [];
			for (var i = 0; i < 15 && !found; i++) {
				await Task.Delay(10_000);
				recent = await FetchMonitorHistAsync(session);
				found = recent.Any(x => IsTarget(x, MonitorDetectedMarker)) && recent.Any(x => IsTarget(x, MonitorTimeoutMarker));
			}

			if (session.Check("E-15 監視ログに2b(検知)→2e(タイムアウト解放)の対が記録される", found,
					new { recent = recent.Take(4).Select(x => new { x.Memo, x.ReturnCode }) })) {
				var timeoutEntry = recent.First(x => IsTarget(x, MonitorTimeoutMarker));
				session.Check("E-15 2e(タイムアウト解放)のMemoにSeqNo=99が含まれる",
					timeoutEntry.Memo.Contains("SeqNo=99", StringComparison.Ordinal), new { timeoutEntry.Memo });
			}
			else {
				session.Note("E-15 スキップ", "約150秒待っても2b→2eの対が記録されませんでした。"
					+ "S3/S5の環境変数がCvServer起動前に設定されていたか確認してください。");
			}

			var after = await FetchLockRowsAsync(session);
			session.Check("E-15 監視の解放で行が消えている", !after.Any(x => x.Id == lockId), new { after });
		}
		finally {
			var remaining = await FetchLockRowsAsync(session);
			if (remaining.Any(x => x.Id == lockId)) {
				await DeleteLockRowByIdAsync(lockId);
				session.Note("E-15 後始末:監視で解放されなかった行を直接DELETEで削除した", lockId);
			}
		}
	}
}
