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
		logger.LogInformation(startMessage);
		var start = DateTime.Now;

		for (var index = 0; index < steps.Count; index++) {
			var (name, action) = steps[index];
			var startProgress = index * 100 / steps.Count;

			yield return new StreamStepProgress(name, 0, startProgress, false, false);

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

			var endProgress = (int)Math.Round((index + 1) * 100d / steps.Count, MidpointRounding.AwayFromZero);
			yield return new StreamStepProgress(name, count, endProgress, false, isError, errorMsg);
		}

		var elapsed = DateTime.Now - start;
		logger.LogInformation($"{endMessage} {elapsed.TotalSeconds:0.0}s");

		yield return new StreamStepProgress("Complete", 0, 100, true, false, $"{elapsed.TotalSeconds:0.0}s");
	}
}
