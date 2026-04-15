using CvBase;
using NLog;

namespace CvDomainLogic;

public class SummaryDb {
	ExDatabase _db;
	Logger _logger;
	public SummaryDb(ExDatabase db) {
		_db = db;
		_logger = LogManager.GetCurrentClassLogger();
	}
	public async IAsyncEnumerable<ConvertStepProgress> SummaryAllAsyncStream(bool isInit = true) {
		_logger.Info("処理開始");
		var start = DateTime.Now;

		// ToDo: 最終的に実行させる処理を整理
		var steps = new (string Name, Func<bool, int> Action)[] {
			/*
			*/
		};

		for (var index = 0; index < steps.Length; index++) {
			var (name, action) = steps[index];
			var startProgress = index * 100 / steps.Length;

			// ステップ開始通知
			yield return new ConvertStepProgress(name, 0, startProgress, false, false);

			// 処理実行
			int count = 0;
			string? errorMsg = null;
			bool isError = false;
			try {
				count = action(isInit);
			}
			catch (Exception ex) {
				_logger.Error(ex, $"処理エラー: {name}");
				isError = true;
				errorMsg = ex.Message;
			}

			var endProgress = (int)Math.Round((index + 1) * 100d / steps.Length, MidpointRounding.AwayFromZero);

			// ステップ完了通知
			yield return new ConvertStepProgress(name, count, endProgress, false, isError, errorMsg);
		}

		var elapsed = DateTime.Now - start;
		_logger.Info($"処理終了 {elapsed.TotalSeconds:0.0}s");

		yield return new ConvertStepProgress("Complete", 0, 100, true, false, $"{elapsed.TotalSeconds:0.0}s");
	}

}
