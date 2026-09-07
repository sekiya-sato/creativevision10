using System.Data;
using System.IO;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;

namespace UatVm.Scenarios;

/// <summary>
/// テストケースE-13（`Doc/test/2026-09-07_マニュアル排他制御_テスト計画.md` §4.2）の自動化版。
/// 手順は手動観測手順書 `Doc/test/2026-09-07_マニュアル排他制御_手動観測手順.md` §6 のシナリオ読み替え。
/// </summary>
/// <remarks>
/// <para>
/// 監視タスクの「前回状態」は <c>CvServer/Services/SchedulerService.cs:129</c> の
/// <c>_manualLockMonitorState</c>（DIシングルトンのインスタンスフィールド）にしか無く、
/// CvServerを再起動すると失われる。行はDBに残るため、再起動後の最初のtickでは
/// 「前回状態なし＋行あり」となり <c>ManualLockMonitor.Evaluate</c> は2b（新規検知）を返す。
/// 設計書 `Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md` §3.7 は「ログは必ず2b→2fか
/// 2b→2eの対になる」と定めているが、再起動を跨ぐと2b→2bになり対が崩れる可能性がある。
/// </para>
/// <para>
/// これは製品コードのFAILではなく「設計の穴」として観測・記録するのが本ケースの位置づけである
/// （テスト計画書§4.2 E-13の原文どおり）。
/// </para>
/// <para>
/// <see cref="UatVm.CvServerProcess"/>の<c>--manage-server</c>はCvServerを子プロセスとして1回起動し、
/// シナリオ終了時に正規終了させる作りで、シナリオの途中で再起動する手段が無い。そこで本シナリオを
/// 2つのエントリポイント（<c>manuallockrestart1</c>・<c>manuallockrestart2</c>）に分け、
/// 外側の<c>Run-ManualLockRestart.ps1</c>が<c>UatVm.exe manuallockrestart1 --manage-server</c>→
/// <c>UatVm.exe manuallockrestart2 --manage-server</c>の順に2回起動する。CvServerの再起動そのものは
/// 各段の正規の起動・終了経路（<see cref="UatVm.CvServerProcess"/>）を2回使うだけであり、無改修である。
/// </para>
/// <para>
/// ヘルパーは<see cref="ManualLockScenario"/>のものがprivateのため流用できない。必要な最小限のみを
/// ここに自前で持つ（凝った共通化はしない）。
/// </para>
/// </remarks>
public static class ManualLockRestartScenario {
	// 実処理の一連処理名（請求計算・支払計算等）と混同しないよう分かる名前にする（ManualLockScenarioと同じ方針）。
	private const string LockTableName = "UATVM-ManualLockTest-E13";

	// 監視タスクのログ目印（CvDomainLogic/ManualLockDb.cs の private const と同じ値。証跡照合のためだけに複製する）。
	private const string MonitorDetectedMarker = "[2b:検知]";

	// 1段目が2段目へ引き継ぐ証跡（Note経由。行そのものはDBに残すのでこの値は照合・記録専用）。
	private sealed record HandoffData(long LockId, long BaselineId);

	// ==================================================================
	// manuallockrestart1: 再起動前の観測（排他行を作り、2bの検知を待つ）
	// ==================================================================

	public static async Task RunAsync(VmSession session) {
		session.Note("case:開始", "E-13 (1段目) CvServer再起動前の観測: 排他行を作り2b(検知)を待つ");

		var autoExecConfig = await FetchAutoExecConfigAsync(session);
		session.Note("switches:S4 監視タスクcron・実行フラグ(MasterConfig)", autoExecConfig);
		if (!autoExecConfig.IsEnabled) {
			session.Note("manuallockrestart1 スキップ", "監視タスクの実行フラグがOFFのため、2b(検知)を観測できません。"
				+ "手動観測手順書§6.2のとおりS4実行フラグ=1にしてから実行してください。");
			return;
		}
		if (!autoExecConfig.IsEveryMinute) {
			session.Note("manuallockrestart1 スキップ", "S4(cron)が毎分実行になっていないため、既定間隔(5分)では"
				+ "本シナリオの待ち時間(約150秒)内に観測できません。手動観測手順書§6.2のとおりcron=`*/1 * * * *`へ"
				+ "変更してからCvServerを起動し直してください。");
			return;
		}

		var before = await FetchLockRowsAsync(session);
		if (!session.Check("manuallockrestart1 開始前に排他行が無い", before.Count == 0, new { before })) {
			return;
		}

		// 前回実行が残した2bを今回のものと誤認しないための基準点。これより新しいIdだけを照合する
		// （ManualLockScenarioのE-01・E-15と同じ手法）。
		var baselineId = (await FetchMonitorHistAsync(session)).Select(x => x.Id).DefaultIfEmpty(0).Max();

		// ExpectedDuration=3600のときの閾値はmax(3600*2,15分)=120分。手順の途中(2段目への引き継ぎを含む)で
		// 誤って解放されないようにする（手動観測手順書§6.3手順5と同じ値）。
		var lockId = await InsertFakeLockRowAsync(LockTableName, "観測用", expectedDurationSeconds: 3600, vduAgoSeconds: 0,
			memo: "E-13(1段目): CvServer再起動前後の監視ログ対応関係の確認用（直接INSERT）");
		session.Note("manuallockrestart1 テスト用排他行を直接INSERT", new { lockId });

		bool IsTarget(SysHistAutoexec x) =>
			x.Id > baselineId
			&& x.Memo.StartsWith(MonitorDetectedMarker, StringComparison.Ordinal)
			&& x.Memo.Contains(LockTableName, StringComparison.Ordinal);

		var found = false;
		for (var i = 0; i < 15 && !found; i++) {
			await Task.Delay(10_000);
			var hist = await FetchMonitorHistAsync(session);
			found = hist.Any(IsTarget);
		}

		if (session.Check("manuallockrestart1 監視ログに2b(検知)が記録される", found)) {
			// 2段目が「このシナリオ開始時点の最大Id」を基準点にできるよう、その時点の最大Idと
			// 排他行のIdをNoteのdataへ証跡として残す（指示書のとおり）。
			var handoffBaselineId = (await FetchMonitorHistAsync(session)).Select(x => x.Id).DefaultIfEmpty(0).Max();
			session.Note("manuallockrestart1 引き継ぎ情報(2段目用)", new HandoffData(lockId, handoffBaselineId));
		}
		else {
			session.Note("manuallockrestart1 スキップ", "約150秒待っても2b(検知)が記録されませんでした。"
				+ "S4(cron・実行フラグ)がCvServer起動前に反映されていたか確認してください。");
		}

		// 排他行は削除しない（2段目で使う。指示書のとおり）。
		session.Note("case:終了", "manuallockrestart1 完了（排他行は次段のため残す）");
	}

	// ==================================================================
	// manuallockrestart2: 再起動後の観測（新たな2bが出るか＝設計の穴の確認）
	// ==================================================================

	public static async Task RunAsync2(VmSession session) {
		session.Note("case:開始", "E-13 (2段目) CvServer再起動後の観測: 新たな2b(検知)が出るかを確認する");

		var rows = await FetchLockRowsAsync(session);
		if (rows.Count != 1) {
			session.Note("manuallockrestart2 終了", "排他行が1件ではありません(1段目が残したはずの行が見つかりません)。"
				+ "1段目(manuallockrestart1)が失敗またはスキップした可能性があります。"
				+ new { rows.Count });
			return;
		}
		var lockId = rows[0].Id;
		session.Note("manuallockrestart2 1段目が残した排他行を確認", new { lockId, rows[0].TableName });

		try {
			// 基準点は「このシナリオ開始時点の最大Id」とする（1段目の基準点ではなく、指示書のとおり）。
			var baselineId = (await FetchMonitorHistAsync(session)).Select(x => x.Id).DefaultIfEmpty(0).Max();

			bool IsNewDetected(SysHistAutoexec x) =>
				x.Id > baselineId
				&& x.Memo.StartsWith(MonitorDetectedMarker, StringComparison.Ordinal)
				&& x.Memo.Contains(LockTableName, StringComparison.Ordinal);

			var found = false;
			List<SysHistAutoexec> recent = [];
			for (var i = 0; i < 15 && !found; i++) {
				await Task.Delay(10_000);
				recent = await FetchMonitorHistAsync(session);
				found = recent.Any(IsNewDetected);
			}

			if (session.Check("manuallockrestart2 CvServer再起動後に新たな2b(検知)が記録される(想定される結果)", found,
					new { recent = recent.Take(4).Select(x => new { x.Memo, x.StartTime, x.EndTime }) })) {
				session.Note("manuallockrestart2 設計の穴の確認",
					"設計書§3.7の「ログは必ず2b→2fか2b→2eの対になる」という規則が、CvServer再起動を跨ぐと"
					+ "崩れる(2b→2b)ことを確認した。前回状態がCvServer/Services/SchedulerService.cs:129の"
					+ "_manualLockMonitorState(インスタンスフィールド)にしか無く、再起動で失われるため、"
					+ "再起動後の最初のtickでは「前回状態なし＋行あり」となり2b(新規検知)を返す実装事実と一致する。"
					+ "設計の穴として設計書§3.7への追記が必要。");
			}
			else {
				session.Note("manuallockrestart2 要追加調査",
					"CvServer再起動後も新たな2b(検知)が記録されなかった。手動観測手順書§6.1の実装事実"
					+ "(前回状態はインスタンスフィールドで再起動により失われる)と食い違うため、追加調査が必要。"
					+ "(例: 別プロセスのCvServerが残っていた、cron・実行フラグが未反映だった等)");
			}
		}
		finally {
			// 必ず後始末で排他行を削除する。
			await DeleteLockRowByIdAsync(lockId);
			var after = await FetchLockRowsAsync(session);
			session.Check("manuallockrestart2 テスト用排他行を削除した", !after.Any(x => x.Id == lockId), new { after });
		}

		session.Note("case:終了", "manuallockrestart2 完了");
	}

	// ==================================================================
	// 共通ヘルパー（ManualLockScenarioのものはprivateのため流用できず、必要最小限だけ自前で持つ）
	// ==================================================================

	private sealed record AutoExecConfig(string CronVal, string EnabledVal) {
		/// <summary>手動観測手順書§6.2のとおりS4を毎分実行へ変更済みか（ManualLockScenarioと同じ判定）。</summary>
		public bool IsEveryMinute {
			get {
				var minuteField = CronVal.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
				return minuteField is "*" or "*/1";
			}
		}
		/// <summary>監視タスクが有効か。</summary>
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

	private static Task<List<SysSequence>> FetchLockRowsAsync(VmSession session) =>
		session.QueryAsync<SysSequence>($"SELECT * FROM {nameof(SysSequence)} WHERE SysSeqType=1 ORDER BY Id");

	private static Task<List<SysHistAutoexec>> FetchMonitorHistAsync(VmSession session) =>
		session.QueryAsync<SysHistAutoexec>(
			$"SELECT * FROM {nameof(SysHistAutoexec)} WHERE SysHistType=0 AND TaskName=@0 ORDER BY Id DESC LIMIT 50",
			MasterConfig.AutoExecTaskNameManualLockMonitor);

	/// <summary>
	/// 対象DB(<c>CvServer/server-user163.db</c>)のパスを、ハーネスのカレントディレクトリ
	/// （<c>CvWpfclient</c>フォルダ）から逆算する（ManualLockScenario.ResolveDbPathと同じ）。
	/// </summary>
	private static string ResolveDbPath() {
		var repoRoot = Directory.GetParent(Environment.CurrentDirectory)?.FullName
			?? throw new InvalidOperationException("リポジトリルートを解決できませんでした。");
		return Path.Combine(repoRoot, "CvServer", "server-user163.db");
	}

	private static Task<long> InsertFakeLockRowAsync(string tableName, string columnName, long expectedDurationSeconds, double vduAgoSeconds, string memo) =>
		Task.Run(() => {
			var dbPath = ResolveDbPath();
			using var db = ExDatabaseSqlite.GetDbConn(dbPath);
			var now = DateTime.UtcNow.Ticks;
			var vdu = now - (long)(vduAgoSeconds * TimeSpan.TicksPerSecond);
			var row = new SysSequence {
				SysSeqType = (int)EmSysSeqType.ManualLock,
				TableName = tableName,
				ColumnName = columnName,
				SeqNo = 1,
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
}
