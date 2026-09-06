using Microsoft.Extensions.Logging;

namespace CvDomainLogic;

/// <summary>
/// ステップ実行の共通ランナー。マニュアル排他制御（正典は
/// `Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md`、以下「設計書」）§2.1〜§2.4の適用対象13処理のうち、
/// <see cref="SummaryDb"/>・<see cref="StocktakeDb"/>・<see cref="HhtProcess"/>の9処理はここを1箇所通るため、
/// 排他の取得・進捗・終了もここへ集約する（設計書§2.4「対象は`ConvertDb`を除く」）。
/// <para>
/// <paramref name="manualLockDb"/>・<paramref name="lockProcessName"/>のいずれかが<c>null</c>の場合は
/// 排他を一切取らない（既定値）。<c>ConvertDb</c>の2箇所（設計書§2.4で対象外と明記）はこの既定のまま、
/// 引数を渡さずに呼ぶことで従来どおり排他なしで動作する。
/// </para>
/// </summary>
internal static class StreamStepProgressRunner {
	/// <summary>
	/// ステップ列を順に実行しつつ進捗を通知する。
	/// </summary>
	/// <param name="steps">実行するステップ列（名前と処理本体）</param>
	/// <param name="argument">各ステップへ渡す引数</param>
	/// <param name="logger">ログ出力先</param>
	/// <param name="startMessage">開始時ログの文言</param>
	/// <param name="errorMessageTemplate">ステップ内で例外が起きたときのログテンプレート（<c>{StepName}</c>を含む）</param>
	/// <param name="endMessage">全ステップ終了時ログの文言</param>
	/// <param name="manualLockDb">
	/// マニュアル排他制御（設計書§2.1〜§2.3）を掛ける場合に渡す。<c>null</c>なら排他を取らない（既定）
	/// </param>
	/// <param name="lockProcessName">
	/// 一連処理名（<c>SysSequence.TableName</c>、設計書§2.4の表の値）。<paramref name="manualLockDb"/>と
	/// 両方指定されたときだけ排他を取る
	/// </param>
	/// <param name="lockExpectedDurationSeconds">一連処理全体の予想処理秒数（<c>ExpectedDuration</c>）</param>
	public static async IAsyncEnumerable<StreamStepProgress> Run<TArg>(
		IReadOnlyList<(string Name, Func<TArg, int> Action)> steps,
		TArg argument,
		ILogger logger,
		string startMessage,
		string errorMessageTemplate,
		string endMessage,
		ManualLockDb? manualLockDb = null,
		string? lockProcessName = null,
		long lockExpectedDurationSeconds = 0) {

		ManualLockHandle? lockHandle = null;
		if (manualLockDb != null && lockProcessName != null) {
			// 1a: ステップ実行の前に排他を取得する。取得できなければ1ステップも実行しない（設計書§2.1）
			var firstStepName = steps.Count > 0 ? steps[0].Name : lockProcessName;
			var lockResult = manualLockDb.TryBegin(lockProcessName, firstStepName, lockExpectedDurationSeconds);
			if (!lockResult.IsAcquired) {
				var message = ManualLockMessages.BuildBlockedMessage(lockProcessName, lockResult.Blocker);
				logger.LogWarning("マニュアル排他制御: 開始を中断しました。 ProcessName={ProcessName}, Message={Message}",
					lockProcessName, message);
				// 既存のエラー通知(StreamStepProgressPhase.Error)を使い、先行処理の情報を含めてストリームを終える。
				// IsCompletedはfalseのままにする(SchedulerServiceはIsCompleted優先で判定するため、
				// trueにするとIsErrorの分岐＝エラー内容の記録が読まれなくなる)。
				yield return new StreamStepProgress(lockProcessName, 0, 0, false, true, message, StreamStepProgressPhase.Error);
				yield break;
			}
			lockHandle = lockResult.Handle;
		}

		// finally で Dispose を保証する(正常終了時は下の Complete 呼び出しで Completed 済みのため実質何もしない。
		// 例外・キャンセルで打ち切られた場合は Completed=false のまま Dispose され、行を残す=異常終了とみなす方針
		// (ManualLockHandle のドキュメントコメント参照)。
		try {
			logger.LogInformation("{Message} ステップ数={StepCount}", startMessage, steps.Count);
			var start = DateTime.Now;
			var totalCount = 0;
			var hadError = false;

			for (var index = 0; index < steps.Count; index++) {
				var (name, action) = steps[index];
				var startProgress = index * 100 / steps.Count;

				if (lockHandle != null) {
					// 1b: 各ステップの開始時に進捗を書く（設計書§2.2）
					manualLockDb!.Progress(lockHandle, name, index + 1);
				}

				// ステップ開始。件数・所要時間はまだ確定していないので Phase で開始と判別させる
				// （旧実装は開始・終了とも同じ形の通知だったため、画面に「処理中 件数=0」が二重に出ていた）
				logger.LogInformation("ステップ開始: {StepName} ({StepNo}/{StepCount})", name, index + 1, steps.Count);
				yield return new StreamStepProgress(name, 0, startProgress, false, false, null, StreamStepProgressPhase.Started);

				var stepStart = DateTime.Now;
				int count = 0;
				string? errorMsg = null;
				bool isError = false;
				try {
					count = action(argument);
				}
				catch (Exception ex) {
					logger.LogError(ex, errorMessageTemplate, name);
					isError = true;
					errorMsg = ex.Message;
				}

				var stepElapsed = (DateTime.Now - stepStart).TotalSeconds;
				var endProgress = (int)Math.Round((index + 1) * 100d / steps.Count, MidpointRounding.AwayFromZero);
				if (isError) {
					hadError = true;
					yield return new StreamStepProgress(name, count, endProgress, false, true, errorMsg, StreamStepProgressPhase.Error, stepElapsed);
				}
				else {
					totalCount += count;
					logger.LogInformation("ステップ完了: {StepName} ({StepNo}/{StepCount}) 件数={Count} 所要={Elapsed:0.0}s",
						name, index + 1, steps.Count, count, stepElapsed);
					yield return new StreamStepProgress(name, count, endProgress, false, false, null, StreamStepProgressPhase.Finished, stepElapsed);
				}
			}

			var elapsed = DateTime.Now - start;
			logger.LogInformation("{Message} 所要={Elapsed:0.0}s", endMessage, elapsed.TotalSeconds);

			if (lockHandle != null) {
				// 1c: 全ステップ終了後に終了を記録する（設計書§2.3）。ここへ到達したときだけが正常終了である
				manualLockDb!.Complete(lockHandle, hadError ? 1 : 0, totalCount);
			}

			yield return new StreamStepProgress("Complete", 0, 100, true, false, $"{elapsed.TotalSeconds:0.0}s",
				StreamStepProgressPhase.Completed, elapsed.TotalSeconds);
		}
		finally {
			// Complete済みならDisposeは何もしない。例外・キャンセルで打ち切られた場合は行を残す(異常終了の方針)
			lockHandle?.Dispose();
		}
	}
}

/// <summary>
/// マニュアル排他制御が先行処理を検知したときの利用者向けメッセージ組み立て。
/// <see cref="StreamStepProgressRunner"/>と<c>CostUpdateDb</c>の各Apply系メソッドの両方から使う共通部品。
/// </summary>
internal static class ManualLockMessages {
	/// <summary>
	/// 「どの一連処理が、どの処理を、いつから実行中か」を伝えるメッセージを組み立てる（設計書§2.4適用時の要件）。
	/// </summary>
	/// <param name="processName">開始しようとした一連処理名</param>
	/// <param name="blocker">先行処理の<c>SysSequence</c>行（<see cref="ManualLockDb.TryBegin"/>が返す）</param>
	public static string BuildBlockedMessage(string processName, CvBase.SysSequence? blocker) {
		if (blocker == null) {
			return $"他の一連処理が実行中のため、{processName}を開始できません。しばらくしてから再実行してください。";
		}
		var startedAt = new DateTime(blocker.Vdc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
		var updatedAt = new DateTime(blocker.Vdu, DateTimeKind.Utc).ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
		return $"他の一連処理（{blocker.TableName} - {blocker.ColumnName}、開始 {startedAt}、最終更新 {updatedAt}）が実行中のため、" +
			$"{processName}を開始できません。しばらくしてから再実行してください。";
	}
}
