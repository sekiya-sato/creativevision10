using CvBase;

namespace CvDomainLogic;

/// <summary>
/// 監視タスク（設計書§3）が前回チェック時点で保持する<see cref="SysSequence"/>（<c>SysSeqType=1</c>）の
/// スナップショット。不変（record）で、DBの現在の行そのものではなく「前回見た内容」を表す。
/// </summary>
/// <param name="Id"><see cref="SysSequence.Id"/></param>
/// <param name="TableName"><see cref="SysSequence.TableName"/>（一連処理名）</param>
/// <param name="ColumnName"><see cref="SysSequence.ColumnName"/>（現在の処理名）</param>
/// <param name="SeqNo"><see cref="SysSequence.SeqNo"/></param>
/// <param name="Vdc"><see cref="SysSequence.Vdc"/>（一連処理の開始UTC Ticks）</param>
/// <param name="Vdu"><see cref="SysSequence.Vdu"/>（最後に進捗を書いた時刻。生存判定に使う）</param>
/// <param name="ExpectedDuration"><see cref="SysSequence.ExpectedDuration"/>（予想処理時間、秒）</param>
public sealed record ManualLockMonitorState(long Id, string TableName, string ColumnName, long SeqNo, long Vdc, long Vdu, long ExpectedDuration) {
	/// <summary><see cref="SysSequence"/>の1行からスナップショットを作る。</summary>
	public static ManualLockMonitorState FromRow(SysSequence row) {
		ArgumentNullException.ThrowIfNull(row);
		return new ManualLockMonitorState(row.Id, row.TableName, row.ColumnName, row.SeqNo, row.Vdc, row.Vdu, row.ExpectedDuration);
	}
}

/// <summary>
/// 監視タスクが1回のチェックで取るべき行動（設計書§3.1〜§3.6の2a〜2f）。
/// </summary>
public enum ManualLockMonitorAction {
	/// <summary>§3.1（2a：行が無く前回状態も無い）または§3.3（2c：同じ行でVduが前進）。ログは出さない。</summary>
	None,
	/// <summary>§3.2（2b：行を新規検知）。<see cref="SysHistAutoexec"/>へログを出す。</summary>
	RecordDetected,
	/// <summary>§3.5（2e：閾値超過で異常）。行を削除し、<see cref="SysHistAutoexec"/>へログを出す。</summary>
	RecordTimeout,
	/// <summary>§3.6（2f：行が消えて正常終了とみなす）。<see cref="SysHistAutoexec"/>へログを出す。</summary>
	RecordNormalEnd,
}

/// <summary>
/// <see cref="ManualLockMonitor.Evaluate"/>の判定結果。
/// </summary>
/// <param name="NextState">次回チェックのために保持すべき状態（無ければnull）</param>
/// <param name="Action">今回取るべき行動</param>
/// <param name="Subject">
/// <see cref="Action"/>が<see cref="ManualLockMonitorAction.None"/>以外のとき、
/// ログ対象／削除対象の行の内容。<see cref="ManualLockMonitorAction.RecordNormalEnd"/>のときは
/// 既にDB上から消えている行なので、直前まで保持していた状態（<c>previous</c>）がそのまま入る。
/// </param>
public sealed record ManualLockMonitorTick(ManualLockMonitorState? NextState, ManualLockMonitorAction Action, ManualLockMonitorState? Subject);

/// <summary>
/// マニュアル排他制御の監視タスク（設計書§3）の判定を行う純関数群。
/// 正典は<c>Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md</c>§3.1〜§3.7。
/// <para>
/// <b>静的変数についての設計判断（Step 9-4）</b>: 設計書§3は「タスクは静的変数に前回チェック時点の内容を
/// 保持する」と定めるが、静的変数（static field）で実装すると単体テストの間で状態が漏れて
/// テストが互いに干渉してしまう。そのため本クラスは状態を一切保持しない静的メソッドの集まり（純関数）とし、
/// 「前回状態＋現在のDB内容（<see cref="ManualLockDb.FetchActiveLocks"/>の結果）＋現在時刻」を引数で受け取り、
/// 「次の状態＋実行すべき行動」を返す形にした。
/// </para>
/// <para>
/// 状態の実際の保持場所は<see cref="Evaluate"/>の呼び出し側に置く。Step 9-4の実装では
/// <c>CvServer.Services.SchedulerService</c>のインスタンスフィールドがこれにあたる。
/// <c>SchedulerService</c>はDIでシングルトン登録されている（<c>CvServer/Program.cs</c>）ため、
/// アプリ実行中は「1プロセスに1つだけ存在し、次回呼び出しまで値を保持し続ける」という点で
/// 静的変数と実質的に同じ役割を果たす。この構成でも設計書§3の「静的変数に保持する」という要件は
/// 満たしている（保持場所が呼び出し側になるだけである）。
/// </para>
/// </summary>
public static class ManualLockMonitor {
	/// <summary>異常判定の閾値の下限（分）。設計書§3.4。</summary>
	private const int MinThresholdMinutes = 15;

	/// <summary>
	/// 異常とみなす閾値（UTC Ticks）を計算する（設計書§3.4）。
	/// <c>閾値 = max(ExpectedDuration（秒）× 2, 15分)</c>
	/// </summary>
	/// <param name="expectedDurationSeconds">一連処理全体の予想処理秒数（<see cref="SysSequence.ExpectedDuration"/>）</param>
	public static long ComputeThresholdTicks(long expectedDurationSeconds) {
		var expectedSeconds = Math.Max(expectedDurationSeconds, 0);
		var doubledExpectedTicks = TimeSpan.FromSeconds(expectedSeconds).Ticks * 2;
		var minTicks = TimeSpan.FromMinutes(MinThresholdMinutes).Ticks;
		return Math.Max(doubledExpectedTicks, minTicks);
	}

	/// <summary>
	/// 1回のチェックを判定する（設計書§3.1〜§3.6）。現在時刻を引数で受け取るため、
	/// 単体テストは<see cref="DateTime.Now"/>等の実時間に依存せずに判定を固定できる。
	/// </summary>
	/// <param name="previous">前回チェック時点の状態（無ければnull）</param>
	/// <param name="activeLocks">
	/// 現在の<c>SysSeqType=1</c>の行（<see cref="ManualLockDb.FetchActiveLocks"/>の結果。Id昇順）
	/// </param>
	/// <param name="nowUtcTicks">現在時刻（UTC Ticks）</param>
	public static ManualLockMonitorTick Evaluate(ManualLockMonitorState? previous, IReadOnlyList<SysSequence> activeLocks, long nowUtcTicks) {
		ArgumentNullException.ThrowIfNull(activeLocks);

		// 設計書§2.1により全体で1行のはずだが、TryBeginの競合直後などで一時的に複数行になり得るため、
		// Id最小（先行＝勝者）を現在の代表行として扱う。
		var currentRow = activeLocks.Count == 0 ? null : activeLocks.OrderBy(x => x.Id).First();
		var current = currentRow == null ? null : ManualLockMonitorState.FromRow(currentRow);

		if (current == null) {
			if (previous == null) {
				// §3.1（2a）: 行が無く、前回状態も無い。何もしない。ログも出さない。
				return new ManualLockMonitorTick(null, ManualLockMonitorAction.None, null);
			}
			// §3.6（2f）: 前回保持していた行がDB上から消えた。正常終了とみなす。
			return new ManualLockMonitorTick(null, ManualLockMonitorAction.RecordNormalEnd, previous);
		}

		if (previous == null || previous.Id != current.Id) {
			// §3.2（2b）: 行を新規検知（前回状態が無い、または別の行）。
			return new ManualLockMonitorTick(current, ManualLockMonitorAction.RecordDetected, current);
		}

		if (current.Vdu > previous.Vdu) {
			// §3.3（2c）: 同じ行でVduが前進している。処理中とみなす。ログは出さない。
			return new ManualLockMonitorTick(current, ManualLockMonitorAction.None, null);
		}

		// 同じ行でVduが前進していない。§3.4の閾値で判定する。
		var elapsed = nowUtcTicks - current.Vdu;
		var threshold = ComputeThresholdTicks(current.ExpectedDuration);
		if (elapsed > threshold) {
			// §3.4/§3.5（2d/2e）: 閾値超過。異常とみなし行を削除する。
			return new ManualLockMonitorTick(null, ManualLockMonitorAction.RecordTimeout, current);
		}
		// §3.4（2d）: 閾値内。何もしない（状態は維持する）。
		return new ManualLockMonitorTick(current, ManualLockMonitorAction.None, null);
	}

	/// <summary>
	/// 再起動を跨いで孤立した2bへ、対を閉じる補完記録が必要かを判定する（設計書§3.7「再起動を跨いだ場合の例外」、Step T9）。
	/// 呼び出し側（<see cref="ManualLockDb.TryRecordOrphanClosed"/>）が「直近の監視ログ（本タスク名で
	/// 最新の<see cref="SysHistAutoexec"/>行）の内容」を渡し、本メソッドは判定だけを行う純関数のままにする
	/// （<see cref="Evaluate"/>と同じ配置方針。設計書§3備考「純関数の性質を壊さない配置」）。
	/// <para>
	/// <b><paramref name="previousIsNull"/>で絞る理由</b>: <see cref="Evaluate"/>でRecordDetected（2b）が
	/// 一度実行されると、次回tickの<c>previous</c>は必ず非nullになる（2c/2dでは維持され、2e/2fで初めて
	/// nullに戻るが、そのときは直近の監視ログ自体も2e/2fになっている）。したがって
	/// 「<c>previous</c>がnullなのに直近の監視ログは2bのまま」という組み合わせは、
	/// プロセス再起動でメモリ上の状態（<c>SchedulerService._manualLockMonitorState</c>）が失われた
	/// 直後の最初のtickでしか起こり得ない。§3.4のVdu前進監視（2c/2dの判定、対象は排他行そのもの）とは
	/// 判定対象が異なる別軸の閾値であり、混同しないこと。
	/// </para>
	/// <para>
	/// <b>閾値を外した理由（2026-09-07 ユーザー決定）</b>: <paramref name="previousIsNull"/>（再起動でメモリ上の
	/// 前回状態が失われた直後）かつ直近の監視ログが2bのままという組み合わせは、その2bが通常の流れでは
	/// 絶対に対にならないことの証明である（2bを書いたプロセスの状態は既に失われている）。閾値を待つと
	/// 穴が生じる。行が残っている場合、閾値未達のうちに<see cref="Evaluate"/>が新しい2bを書き、直近ログが
	/// すり替わって古い孤立2bが永久に埋もれる（実運用で観測したId 542→543、5分間隔の例）。補完記録は
	/// <see cref="SysHistAutoexec"/>へ1行書くだけで排他行の解放など破壊的な操作を一切しないため、
	/// 「早すぎる補完」を警戒する必要もない。
	/// </para>
	/// <para>
	/// <b>対象範囲（設計書の(a)行が既に消えている場合／(b)まだ残っている場合）</b>: 本メソッドは
	/// <c>SysSequence</c>の行が現存するかどうかを一切見ない。孤立2bに対応する2e/2fが欠落しているという
	/// ログ上の事実は、行の有無に関わらず同じだからである。行が残っている場合(b)は<see cref="Evaluate"/>が
	/// 別途「新しい2b」としてその行を検知し直し、そこから通常どおり2c/2d/2e/2fへ続く（既存の流れは変えない）。
	/// ただし補完記録は閾値を待たず即時に書かれるため、同一tick内では本メソッドによる補完判定が
	/// <see cref="Evaluate"/>の新規2b検知より先に行われ、両者が競合することはない。
	/// 本メソッドはそれとは独立に「直近ログが2bのまま」という記録だけを見て両ケース(a)(b)を区別せず対応する。
	/// </para>
	/// </summary>
	/// <param name="previousIsNull">今回のtickの<c>previous</c>（前回状態）がnullかどうか</param>
	/// <param name="latestHistoryIsDetectedMarker">
	/// 直近の監視ログ（本タスク名で最新の<see cref="SysHistAutoexec"/>行）のMemoが
	/// 「[2b:検知]」で始まったままかどうか
	/// </param>
	public static bool ShouldCloseOrphanedDetection(bool previousIsNull, bool latestHistoryIsDetectedMarker) {
		return previousIsNull && latestHistoryIsDetectedMarker;
	}
}
