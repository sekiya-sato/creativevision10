using Microsoft.Extensions.Logging;

namespace CvDomainLogic;

internal static class StreamStepProgressRunner {
	public static async IAsyncEnumerable<StreamStepProgress> Run<TArg>(
		IReadOnlyList<(string Name, Func<TArg, int> Action)> steps,
		TArg argument,
		ILogger logger,
		string startMessage,
		string errorMessageTemplate,
		string endMessage) {
		logger.LogInformation("{Message} ステップ数={StepCount}", startMessage, steps.Count);
		var start = DateTime.Now;

		for (var index = 0; index < steps.Count; index++) {
			var (name, action) = steps[index];
			var startProgress = index * 100 / steps.Count;

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
				yield return new StreamStepProgress(name, count, endProgress, false, true, errorMsg, StreamStepProgressPhase.Error, stepElapsed);
			}
			else {
				logger.LogInformation("ステップ完了: {StepName} ({StepNo}/{StepCount}) 件数={Count} 所要={Elapsed:0.0}s",
					name, index + 1, steps.Count, count, stepElapsed);
				yield return new StreamStepProgress(name, count, endProgress, false, false, null, StreamStepProgressPhase.Finished, stepElapsed);
			}
		}

		var elapsed = DateTime.Now - start;
		logger.LogInformation("{Message} 所要={Elapsed:0.0}s", endMessage, elapsed.TotalSeconds);

		yield return new StreamStepProgress("Complete", 0, 100, true, false, $"{elapsed.TotalSeconds:0.0}s",
			StreamStepProgressPhase.Completed, elapsed.TotalSeconds);
	}
}
