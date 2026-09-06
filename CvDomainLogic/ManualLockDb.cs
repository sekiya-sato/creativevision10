using CvAsset;
using CvBase;
using CvBase.Share;
using Microsoft.Extensions.Logging;

namespace CvDomainLogic;

/// <summary>
/// 一連処理の開始（1a）の結果。詳細は<see cref="ManualLockDb.TryBegin"/>を参照。
/// </summary>
/// <param name="IsAcquired">自分が排他を取得できたかどうか</param>
/// <param name="Handle">
/// 取得できた場合のハンドル。<see cref="ManualLockDb.Progress"/>/<see cref="ManualLockDb.Complete"/>へ渡す。
/// 取得できなかった場合はnull
/// </param>
/// <param name="Blocker">
/// 取得できなかった場合の先行処理の行（<c>SysSequence</c>をそのまま返す）。
/// 呼び出し側が利用者へ「先行処理: TableName/ColumnName/SeqNo/Vdc/Vdu/ExpectedDuration/Memo」を
/// ワーニング表示できるようにするための情報。取得できた場合はnull
/// </param>
public sealed record ManualLockResult(bool IsAcquired, ManualLockHandle? Handle, SysSequence? Blocker);

/// <summary>
/// <see cref="ManualLockDb.TryBegin"/>で排他を取得したときに返るハンドル。
/// <see cref="ManualLockDb.Progress"/>/<see cref="ManualLockDb.Complete"/>を呼ぶための最小限の情報だけを持つ。
/// <para>
/// <see cref="Dispose"/>の方針: <see cref="ManualLockDb.Complete"/>を呼ばずに破棄された（＝一連処理が
/// 異常終了した）場合、<b>行は削除せず残す</b>。理由は次の2点。
/// (1) 監視タスク（Step 9-4、設計書§3.4〜§3.5）は「<c>Vdu</c>が閾値を超えて前進していない行」を
/// 異常とみなして解放する設計であり、行が残っていることが監視の前提になっている。ここで行を消すと
/// 異常終了そのものが記録に残らず、監視タスクの2e（異常終了ログ）の経路が働かなくなる。
/// (2) 行を残すことで、運用者が強制クリア（Step 9-5、設計書§2.5）の確認画面で
/// 「いつから・どの処理が」止まっているかを確認できる。
/// 副作用として、異常終了した一連処理が次に開始されるまでのあいだ排他が掛かったままになるが、
/// これは監視タスクによる自動解放（最短でも<c>max(ExpectedDuration×2, 15分)</c>）と
/// 運用者による強制クリアのどちらかで解消される設計であり、Step 9-4/9-5の責務である。
/// </para>
/// </summary>
public sealed class ManualLockHandle : IDisposable {
	/// <summary>
	/// <see cref="SysSequence.Id"/>。<see cref="ManualLockDb"/>が内部で使う
	/// </summary>
	internal long Id { get; }
	/// <summary>
	/// 一連処理名（<see cref="SysSequence.TableName"/>）
	/// </summary>
	public string TableName { get; }
	/// <summary>
	/// 一連処理の開始UTC.Ticks（<see cref="SysSequence.Vdc"/>）
	/// </summary>
	public long Vdc { get; }
	/// <summary>
	/// 一連処理全体の予想処理秒数（<see cref="SysSequence.ExpectedDuration"/>）
	/// </summary>
	public long ExpectedDurationSeconds { get; }
	/// <summary>
	/// <see cref="ManualLockDb.Complete"/>で完了済みとしてマークされたかどうか
	/// </summary>
	public bool Completed { get; private set; }

	private readonly ILogger _logger;
	private bool _disposed;

	internal ManualLockHandle(long id, string tableName, long vdc, long expectedDurationSeconds, ILogger logger) {
		Id = id;
		TableName = tableName;
		Vdc = vdc;
		ExpectedDurationSeconds = expectedDurationSeconds;
		_logger = logger;
	}

	/// <summary>
	/// <see cref="ManualLockDb.Complete"/>から呼ばれ、正常終了したことを記録する
	/// </summary>
	internal void MarkCompleted() => Completed = true;

	/// <summary>
	/// 方針は本クラスのドキュメントコメントを参照。<see cref="ManualLockDb.Complete"/>を経由せずに
	/// 破棄された場合は行をあえて残し、警告ログだけを出す
	/// </summary>
	public void Dispose() {
		if (_disposed) {
			return;
		}
		_disposed = true;
		if (!Completed) {
			_logger.LogWarning(
				"マニュアル排他制御: Complete()を呼ばずに破棄されました。異常終了とみなし、SysSequenceの行は残します（監視タスクまたは強制クリアで解放されます）。 TableName={TableName}, Id={Id}",
				TableName, Id);
		}
	}
}

/// <summary>
/// マニュアル排他制御の共通部品。
/// 正典は `Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md`（以下「設計書」）§2.1〜§2.3、§1.1。
/// <para>
/// <b>方式の要点</b>: DB的なロック（トランザクションでの行ロックや占有）は一切使わない。
/// <see cref="SysSequence"/>に<c>SysSeqType=1</c>（<see cref="EmSysSeqType.ManualLock"/>）の行が
/// 存在すること自体で「一連処理が実行中」を論理的に表現し、<c>Vdu</c>（最終更新時刻）の前進で
/// 生存を示す。開始・進捗・終了のいずれの段階も、書いたら即座に開放する（DBロックを保持したまま
/// 複数SQLを束ねない）。対象処理は数分〜数十分かかるため、その間DBロックを保持すると
/// 他の参照系まで待たされてしまう（設計書「0. 背景と決定」）。
/// </para>
/// <para>
/// <c>SysSeqType=1</c>のときだけ「全体で1行」という制約が要るが、これは列値に依存する部分ユニーク
/// 制約であり既存の<c>KeyDml</c>属性・4方言のDDL生成器では表現できない（設計書§1.1）。そのため
/// <see cref="TryBegin"/>は「先にINSERTしてから件数を再確認し、<c>Id</c>が最小でない
/// （後発の）側が自分の行を削除して降りる」という手順でTOCTOUを避けて全体1行を担保する
/// （設計書§2.1）。
/// </para>
/// </summary>
public class ManualLockDb(ExDatabase db) {
	private readonly ExDatabase _db = db;
	private readonly ILogger<ManualLockDb> _logger = new NLogExtender<ManualLockDb>();

	/// <summary>
	/// <see cref="SysSequence.Memo"/>の<c>[ColumnSizeDml(300)]</c>に合わせた上限
	/// </summary>
	private const int MemoMaxLength = 300;
	/// <summary>
	/// <see cref="SysHistAutoexec.TaskName"/>の<c>[ColumnSizeDml(100)]</c>に合わせた上限
	/// </summary>
	private const int TaskNameMaxLength = 100;
	/// <summary>
	/// 進捗追記の区切り文字列
	/// </summary>
	private const string MemoSeparator = " / ";
	/// <summary>
	/// 終了処理（1c）で書く完了済みSeqNo（設計書§2.3）
	/// </summary>
	private const long CompletedSeqNo = 99;

	// ==================================================================
	// 1. 開始（設計書§2.1、1a）
	// ==================================================================

	/// <summary>
	/// 一連処理の開始を試みる（設計書§2.1）。
	/// <para>
	/// 手順は必ず「INSERT → 件数再確認」の順で行う。逆に「確認してからINSERT」にすると、
	/// 2つの処理がほぼ同時に開始したとき、両方が「行なし」を見て両方がINSERTしてしまう
	/// （TOCTOU）。監視タスク（Step 9-4）は<c>SysSeqType=1</c>の行が1行であることを前提にしているため、
	/// ここが崩れると排他として機能しない。先にINSERTしてから<c>SysSeqType=1</c>の行を数え、
	/// <c>Id</c>が最小でない（後発の）側が自分の行を削除して降りることで、DBロックを使わずに
	/// 勝者を一意に決められる。<c>Id</c>は自動採番で単調増加するため、どちらのプロセスから見ても
	/// 同じ勝者になる（設計書§2.1）。
	/// </para>
	/// </summary>
	/// <param name="processName">一連処理名（<c>TableName</c>）。排他の単位</param>
	/// <param name="stepName">最初に実行する処理名（<c>ColumnName</c>）</param>
	/// <param name="expectedDurationSeconds">一連処理全体の予想処理秒数（<c>ExpectedDuration</c>）。個々の処理ステップの見込みではない</param>
	/// <param name="memo">補足メモ</param>
	public ManualLockResult TryBegin(string processName, string stepName, long expectedDurationSeconds, string memo = "") {
		var vdate = Common.GetVdate();
		var row = new SysSequence {
			SysSeqType = (int)EmSysSeqType.ManualLock,
			TableName = processName,
			ColumnName = stepName,
			SeqNo = 1,
			Memo = memo,
			ExpectedDuration = expectedDurationSeconds,
			Vdc = vdate,
			Vdu = vdate,
		};
		// 1. まずINSERTする（確認してからINSERTしない。TOCTOU対策。上のドキュメントコメント参照）
		_db.Insert(row);

		// 2. INSERT後にSysSeqType=1の行を数え直す
		// 自分のINSERTとこの数え直しの間に強制クリア(§2.5)が走ると0件になり得るため FirstOrDefault で受ける。
		// 0件なら排他は空いており、自分の行だけが消えた状態なので取得成功として続行する。
		// Progress/Complete は対象行が無くても例外にしない実装のため、この状態でも一連処理は完走できる。
		var actives = FetchActiveLocks();
		var winner = actives.OrderBy(x => x.Id).FirstOrDefault();
		if (winner != null && winner.Id != row.Id) {
			// 3. 自分よりIdが小さい行（先行）がいるので、自分の行を削除して降りる
			_db.ExecuteDialect($"DELETE FROM {nameof(SysSequence)} WHERE Id=@0", row.Id);
			_logger.LogWarning(
				"マニュアル排他制御: 先行処理があるため開始を中断しました。 ProcessName={ProcessName}, 先行TableName={BlockerTableName}, 先行ColumnName={BlockerColumnName}, 先行SeqNo={BlockerSeqNo}",
				processName, winner.TableName, winner.ColumnName, winner.SeqNo);
			return new ManualLockResult(false, null, winner);
		}

		// 4. 自分だけ（または自分が勝者）なので続行する
		var handle = new ManualLockHandle(row.Id, processName, vdate, expectedDurationSeconds, _logger);
		return new ManualLockResult(true, handle, null);
	}

	// ==================================================================
	// 2. 進捗（設計書§2.2、1b）
	// ==================================================================

	/// <summary>
	/// 一連処理中の各処理の開始時に呼び、進捗を書き込む（設計書§2.2）。
	/// <c>(SysSeqType=1, TableName)</c>で一致する行をUPDATEし、即座に開放する
	/// （トランザクションで抱え込まない）。
	/// <para>
	/// 行が見つからない場合（強制クリアされた等）は例外にせず、更新件数0を返してログに残す。
	/// 呼び出し側の一連処理を、排他行が消えたことだけを理由に中断させないため
	/// （呼び出し側が中断するかどうかは呼び出し側の判断に委ねる）。
	/// </para>
	/// </summary>
	/// <param name="handle"><see cref="TryBegin"/>で得たハンドル</param>
	/// <param name="stepName">現在の処理名（<c>ColumnName</c>）</param>
	/// <param name="seqNo">処理順No（<c>SeqNo</c>）</param>
	/// <param name="appendMemo">追記する処理内容</param>
	/// <returns>UPDATEした件数（0または1）</returns>
	public int Progress(ManualLockHandle handle, string stepName, long seqNo, string appendMemo = "") {
		ArgumentNullException.ThrowIfNull(handle);
		var row = _db.FetchDialect<SysSequence>(
			$"SELECT * FROM {nameof(SysSequence)} WHERE SysSeqType=@0 AND TableName=@1",
			(int)EmSysSeqType.ManualLock, handle.TableName).FirstOrDefault();
		if (row == null) {
			_logger.LogWarning(
				"マニュアル排他制御: 進捗更新の対象行が見つかりません（強制クリア等で消えた可能性）。 TableName={TableName}, StepName={StepName}",
				handle.TableName, stepName);
			return 0;
		}

		var vdate = Common.GetVdate();
		var elapsedSeconds = TicksToSeconds(vdate - handle.Vdc);
		var appendText = string.IsNullOrEmpty(appendMemo)
			? $"{elapsedSeconds:0}秒経過 {stepName}"
			: $"{elapsedSeconds:0}秒経過 {stepName}: {appendMemo}";
		var newMemo = AppendTruncatedMemo(row.Memo, appendText, MemoMaxLength);

		return _db.ExecuteDialect(
			$"UPDATE {nameof(SysSequence)} SET ColumnName=@0, SeqNo=@1, Memo=@2, Vdu=@3 WHERE Id=@4",
			stepName, seqNo, newMemo, vdate, row.Id);
	}

	/// <summary>
	/// <c>Memo</c>への追記を、上限文字数に収まるよう組み立てる純関数（設計書§2.2）。
	/// 上限に達する場合は<b>古い内容から切り捨て、末尾（最新）を残す</b>。
	/// 単体テスト（<c>ManualLockDbTests</c> L-05、境界値）のために独立した静的メソッドとして切り出す。
	/// </summary>
	/// <param name="currentMemo">現在のMemo（追記前）</param>
	/// <param name="appendText">追記する内容（区切り文字は本メソッドが付与する）</param>
	/// <param name="maxLength">上限文字数</param>
	public static string AppendTruncatedMemo(string? currentMemo, string appendText, int maxLength) {
		ArgumentNullException.ThrowIfNull(appendText);
		var combined = string.IsNullOrEmpty(currentMemo) ? appendText : currentMemo + MemoSeparator + appendText;
		if (combined.Length <= maxLength) {
			return combined;
		}
		// 古い内容（先頭側）から切り捨て、末尾（最新）を残す
		return combined[(combined.Length - maxLength)..];
	}

	// ==================================================================
	// 3. 終了（設計書§2.3、1c）
	// ==================================================================

	/// <summary>
	/// 一連処理の終了を記録する（設計書§2.3）。手順の順序を守ること。
	/// <list type="number">
	/// <item><description><c>SysSequence</c>に<c>SeqNo=99</c>を書いて開放する。
	/// 先に書くのは、この後の履歴書き込み中にプロセスが落ちても、
	/// 「終了処理まで到達していた」ことを監視タスクとログから読み取れるようにするため
	/// （<c>SeqNo=99</c>まで進んでいれば、単なる異常終了ではなく終了処理中の異常だったと分かる）</description></item>
	/// <item><description><c>SysHistAutoexec</c>へログを1行追加する（<c>SysHistType=1</c>、手動実行）</description></item>
	/// <item><description><c>SysSequence</c>の行を削除して開放する</description></item>
	/// </list>
	/// <para>
	/// 対象行が既に無い場合（強制クリア等で消えた後にCompleteが呼ばれた場合）は、
	/// 1.と3.を省略し、ハンドルが保持する開始時刻などの情報から履歴だけを最善努力で残す。
	/// 設計書はこのケースを明示していないための実装側の判断であり、報告事項とする。
	/// </para>
	/// </summary>
	/// <param name="handle"><see cref="TryBegin"/>で得たハンドル</param>
	/// <param name="returnCode">実行結果コード（0:成功、0以外:エラーコード）</param>
	/// <param name="count">処理件数</param>
	/// <param name="memo">終了時に追記する補足メモ</param>
	public void Complete(ManualLockHandle handle, int returnCode, int count, string memo = "") {
		ArgumentNullException.ThrowIfNull(handle);
		var vdate = Common.GetVdate();
		var row = _db.FetchDialect<SysSequence>(
			$"SELECT * FROM {nameof(SysSequence)} WHERE SysSeqType=@0 AND TableName=@1",
			(int)EmSysSeqType.ManualLock, handle.TableName).FirstOrDefault();

		if (row != null) {
			// 1. SeqNo=99を先に書いて開放する
			_db.ExecuteDialect(
				$"UPDATE {nameof(SysSequence)} SET SeqNo=@0, Vdu=@1 WHERE Id=@2",
				CompletedSeqNo, vdate, row.Id);
		}
		else {
			_logger.LogWarning(
				"マニュアル排他制御: 終了処理の対象行が見つかりません（強制クリア等で消えた可能性）。履歴のみ最善努力で記録します。 TableName={TableName}",
				handle.TableName);
		}

		// 2. SysHistAutoexecへログを追加する
		var startVdc = row?.Vdc ?? handle.Vdc;
		var history = new SysHistAutoexec {
			SysHistType = (int)EmSysHistType.ManualExec,
			TaskName = NormalizeTaskName(handle.TableName),
			StartTime = FormatHistoryDateTime(startVdc),
			EndTime = FormatHistoryDateTime(vdate),
			ElapsedTime = TicksToSeconds(vdate - startVdc),
			ReturnCode = returnCode,
			Count = count,
			Memo = BuildCompleteHistoryMemo(row, memo),
			Vdc = vdate,
			Vdu = vdate,
		};
		_db.Insert(history);

		if (row != null) {
			// 3. SysSequenceの行を削除して開放する
			_db.ExecuteDialect($"DELETE FROM {nameof(SysSequence)} WHERE Id=@0", row.Id);
		}

		handle.MarkCompleted();
	}

	/// <summary>
	/// 終了履歴の<c>Memo</c>を、<c>SysSequence.Memo</c>（1bで積み上げた進捗）と
	/// 終了時の補足メモから組み立てる（設計書§2.3-2「<c>SysSequence.Memo</c>の内容を参照して組み立てる」）
	/// </summary>
	private static string BuildCompleteHistoryMemo(SysSequence? row, string completeMemo) {
		var progressMemo = row?.Memo ?? string.Empty;
		if (string.IsNullOrEmpty(completeMemo)) {
			return progressMemo;
		}
		return string.IsNullOrEmpty(progressMemo) ? completeMemo : progressMemo + MemoSeparator + completeMemo;
	}

	// ==================================================================
	// 4. 状態照会
	// ==================================================================

	/// <summary>
	/// 現在排他が掛かっている行を<c>Id</c>昇順で返す（Step 9-4監視タスク、Step 9-5強制クリアの両方が使う）。
	/// <c>SysSeqType=0</c>（テーブル連番）の行は絶対に含めない
	/// </summary>
	public IReadOnlyList<SysSequence> FetchActiveLocks() =>
		_db.FetchDialect<SysSequence>(
			$"SELECT * FROM {nameof(SysSequence)} WHERE SysSeqType=@0 ORDER BY Id",
			(int)EmSysSeqType.ManualLock);

	// ==================================================================
	// 5. 監視タスク（設計書§3、Step 9-4）
	// ==================================================================
	// 判定そのもの（純関数）は<see cref="ManualLockMonitor.Evaluate"/>が行う。ここは判定結果に応じた
	// DB書き込み（SysSequenceの削除、SysHistAutoexecへのログ）だけを担う。書式組み立ては
	// NormalizeTaskName/FormatHistoryDateTime/TicksToSeconds/AppendTruncatedMemoを処理側（Complete）と
	// 共通利用し、重複実装を避ける（設計書§3.7、報告事項の指示に対応）。

	/// <summary>
	/// 監視タスクが§3.5（2e）で異常とみなしたときの<see cref="SysHistAutoexec.ReturnCode"/>。
	/// 値そのものに意味は無く、0以外であることだけが規約（設計書§3.5）
	/// </summary>
	public const int MonitorTimeoutReturnCode = 9;

	/// <summary>監視ログの目印（設計書§3.7の2b）。<see cref="SysHistAutoexec.Memo"/>の先頭に付ける</summary>
	private const string MonitorDetectedMarker = "[2b:検知]";
	/// <summary>監視ログの目印（設計書§3.7の2e）</summary>
	private const string MonitorTimeoutMarker = "[2e:タイムアウト解放]";
	/// <summary>監視ログの目印（設計書§3.7の2f）</summary>
	private const string MonitorNormalEndMarker = "[2f:正常終了]";

	/// <summary>
	/// 監視タスクが§3.2（2b）で行を新規検知したときの履歴を書く（設計書§3.2、§3.7）。
	/// <see cref="EmSysHistType.AutoExec"/>（0）で記録する。監視タスク自身が自動実行のため、
	/// 処理側が§2.3-2で書く<see cref="EmSysHistType.ManualExec"/>の履歴とは別行になる（設計書§3.7）。
	/// </summary>
	/// <param name="subject">検知した行のスナップショット</param>
	/// <param name="monitorTaskName">監視タスクの表示名（<see cref="SysHistAutoexec.TaskName"/>）</param>
	public void RecordMonitorDetected(ManualLockMonitorState subject, string monitorTaskName) =>
		InsertMonitorHistory(monitorTaskName, 0, MonitorDetectedMarker, subject);

	/// <summary>
	/// 監視タスクが§3.5（2e）で閾値超過（異常）と判定したときに、対象行を削除して履歴を書く（設計書§3.5、§3.7）。
	/// <c>ReturnCode</c>は<see cref="MonitorTimeoutReturnCode"/>（非0）を使う。
	/// </summary>
	public void RecordMonitorTimeout(ManualLockMonitorState subject, string monitorTaskName) {
		ArgumentNullException.ThrowIfNull(subject);
		_db.ExecuteDialect($"DELETE FROM {nameof(SysSequence)} WHERE Id=@0", subject.Id);
		InsertMonitorHistory(monitorTaskName, MonitorTimeoutReturnCode, MonitorTimeoutMarker, subject);
	}

	/// <summary>
	/// 監視タスクが§3.6（2f）で正常終了を検知したときの履歴を書く。対象行は既にDB上から消えているため
	/// （処理側が§2.3-3で自分の行を削除済み）、ここでは削除は行わない。
	/// </summary>
	public void RecordMonitorNormalEnd(ManualLockMonitorState subject, string monitorTaskName) =>
		InsertMonitorHistory(monitorTaskName, 0, MonitorNormalEndMarker, subject);

	/// <summary>
	/// 監視タスクの<see cref="SysHistAutoexec"/>ログを組み立てて書き込む共通処理（設計書§3.7）。
	/// </summary>
	private void InsertMonitorHistory(string monitorTaskName, int returnCode, string marker, ManualLockMonitorState subject) {
		ArgumentNullException.ThrowIfNull(subject);
		var vdate = Common.GetVdate();
		var detail =
			$"TableName={subject.TableName}, ColumnName={subject.ColumnName}, SeqNo={subject.SeqNo}, " +
			$"Vdc={FormatHistoryDateTime(subject.Vdc)}, Vdu={FormatHistoryDateTime(subject.Vdu)}, ExpectedDuration={subject.ExpectedDuration}秒";
		var memo = AppendTruncatedMemo(marker, detail, MemoMaxLength);

		var history = new SysHistAutoexec {
			SysHistType = (int)EmSysHistType.AutoExec,
			TaskName = NormalizeTaskName(monitorTaskName),
			StartTime = FormatHistoryDateTime(subject.Vdc),
			EndTime = FormatHistoryDateTime(vdate),
			ElapsedTime = TicksToSeconds(vdate - subject.Vdc),
			ReturnCode = returnCode,
			Count = 0,
			Memo = memo,
			Vdc = vdate,
			Vdu = vdate,
		};
		_db.Insert(history);
	}

	// ==================================================================
	// 内部ヘルパー
	// ==================================================================

	/// <summary>
	/// UTC Ticksの差分を秒数に変換する。負値（時計のずれ等）は0に丸める
	/// </summary>
	private static double TicksToSeconds(long deltaTicks) =>
		deltaTicks <= 0 ? 0 : (double)deltaTicks / TimeSpan.TicksPerSecond;

	/// <summary>
	/// UTC Ticksを<c>SysHistAutoexec.StartTime</c>/<c>EndTime</c>と同じ書式（<c>yyyyMMddHHmmss</c>、ローカル時刻）に変換する。
	/// 既存<c>SchedulerService</c>の<c>Helpers.ToAutoexecDateTimeString</c>と同じ書式に合わせる
	/// （`CvServer/Services/SchedulerService.cs:1351`付近の<c>InsertAutoexecHistory</c>を参照）
	/// </summary>
	private static string FormatHistoryDateTime(long utcTicks) =>
		new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyyMMddHHmmss");

	/// <summary>
	/// <see cref="SysHistAutoexec.TaskName"/>の<c>[ColumnSizeDml(100)]</c>に収める。
	/// <c>CvServer</c>は参照できないため（Step 9-3/9-4/9-5の層であり本Stepの対象外）、
	/// <c>SchedulerService.Helpers.NormalizeAutoexecText</c>と同じ考え方
	/// （改行を空白に置換してトリムし、上限を超えたら末尾3文字を<c>...</c>に置き換える）を
	/// 本クラス側に用意する
	/// </summary>
	private static string NormalizeTaskName(string value) {
		var text = string.IsNullOrWhiteSpace(value) ? "未設定" : value.Replace("\r", " ").Replace("\n", " ").Trim();
		if (text.Length <= TaskNameMaxLength) {
			return text;
		}
		return text[..(TaskNameMaxLength - 3)] + "...";
	}
}
