using CodeShare;
using CvBaseOracle;
using CvDomainLogic;
using Microsoft.AspNetCore.Authorization;
using ProtoBuf.Grpc;


namespace CvServer.Services;

public partial class CoreService {
	/// <summary>
	/// ストリーミングメッセージを処理する
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	[AllowAnonymous]
	//[Authorize]
	public async IAsyncEnumerable<StreamMsg> QueryMsgStreamAsync(CvMsg request, CallContext context = default) {
		ArgumentNullException.ThrowIfNull(request);
		var ct = context.CancellationToken;
		_logger.LogInformation("gRPCストリーミングリクエスト QueryMsgStreamAsync Flag: {Flag}, DataType: {DataType}", request.Flag, request.DataType);
		await Task.Yield();

		// ConvertDb関連フラグの処理
		if (request.Flag is CvFlag.MSg040_ConvertDb or CvFlag.MSg041_ConvertDbInit) {
			var isInit = request.Flag == CvFlag.MSg041_ConvertDbInit;

			// HandleConvertDbStreamAsyncの結果をそのまま返す
			await foreach (var msg in HandleConvertDbStreamAsync(isInit, ct, request.Flag)) {
				yield return msg;
			}
			yield break;
		}
		// 	集計処理
		else if (request.Flag is CvFlag.MSg050_Summary) {
			await foreach (var msg in HandleSummaryStreamAsync(ct, request)) {
				yield return msg;
			}
			yield break;
		}
		// テストストリーミング処理（既存）
		else if (request.Flag is CvFlag.MSg710_StreamingTest) {
			// 追加：HandleConvertTestStreamAsync を呼ぶ
			await foreach (var msg in HandleConvertTestStreamAsync(ct, request.Flag)) {
				yield return msg;
			}
			yield break;
		}
	}

	/// <summary>
	/// ConvertDbのストリーミング処理ハンドラ
	/// </summary>
	private async IAsyncEnumerable<StreamMsg> HandleConvertDbStreamAsync(bool isInit, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, CvFlag flag) {
		var oracleConnectionString = _configuration.GetConnectionString("oracle") ?? string.Empty;
		var fromDb = ExDatabaseOracle.GetDbConn(oracleConnectionString);

		var convertDb = new ConvertDb(fromDb, _db);

		// ストリーミングをメッセージに変換
		// ConvertAllAsyncStream()が既にエラーハンドリングしているため、try-catchは不要
		await foreach (var progress in convertDb.ConvertAllAsyncStream(isInit).WithCancellation(ct)) {
			yield return new StreamMsg {
				Flag = flag,
				Code = progress.IsError ? -1 : 0,
				DataType = typeof(string),
				DataMsg = progress.IsError
					? $"エラー: {progress.StepName} - {progress.ErrorMessage} ----{DateTime.Now: MM/dd HH:mm:ss.fff}"
					: $"{(progress.IsCompleted ? "完了" : "処理中")}: {progress.StepName} 件数={progress.Count} ----{DateTime.Now: MM/dd HH:mm:ss.fff}",
				Progress = progress.Progress,
				IsCompleted = progress.IsCompleted,
				IsError = progress.IsError
			};
		}
	}
	/// <summary>
	/// MSg050_Summaryのストリーミング処理ハンドラ
	/// </summary>
	private async IAsyncEnumerable<StreamMsg> HandleSummaryStreamAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, CvMsg request) {
		var summaryDb = new SummaryDb(_db);

		// ストリーミングをメッセージに変換
		// ConvertAllAsyncStream()が既にエラーハンドリングしているため、try-catchは不要
		await foreach (var progress in summaryDb.SummaryAllAsyncStream(new SummaryParameter("201905", "201906")).WithCancellation(ct)) {
			yield return new StreamMsg {
				Flag = request.Flag,
				Code = progress.IsError ? -1 : 0,
				DataType = typeof(string),
				DataMsg = progress.IsError
					? $"エラー: {progress.StepName} - {progress.ErrorMessage} ----{DateTime.Now: MM/dd HH:mm:ss.fff}"
					: $"{(progress.IsCompleted ? "完了" : "処理中")}: {progress.StepName} 件数={progress.Count} ----{DateTime.Now: MM/dd HH:mm:ss.fff}",
				Progress = progress.Progress,
				IsCompleted = progress.IsCompleted,
				IsError = progress.IsError
			};
		}
	}

	#region テストストリーミング処理
	/// <summary>
	/// ダミーのタスク(時間がかかる処理のシミュレート) — 非同期＆キャンセル対応
	/// </summary>
	/// <returns></returns>
	static async Task<int> SleepTaskAsync(int miliSeconds = 1000, CancellationToken ct = default) {
		for (int i = 0; i < 3; i++) {
			await Task.Delay(miliSeconds, ct);
		}
		return 0;
	}

	private async IAsyncEnumerable<StreamMsg> HandleConvertTestStreamAsync(
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct,
		CvFlag flag) {
		var start = DateTime.Now;
		var stepNames = new[] {
			"This is First Step",
			"This is Second Step",
			"This is Third Step",
			"This is 4th Step",
			"This is 5th Step",
			"This is 6th Step",
			"This is 7th Step",
			"This is 8th Step",
		};

		for (var index = 0; index < stepNames.Length; index++) {
			ct.ThrowIfCancellationRequested();
			var name = stepNames[index];
			var startProgress = index * 100 / stepNames.Length;
			yield return new StreamMsg {
				Flag = flag,
				Code = 0,
				DataType = typeof(string),
				DataMsg = $"開始: {name} ---- {DateTime.Now: MM/dd HH:mm:ss.fff}",
				Progress = startProgress
			};

			var count = await SleepTaskAsync(1000, ct);
			var endProgress = (int)Math.Round((index + 1) * 100d / stepNames.Length, MidpointRounding.AwayFromZero);
			yield return new StreamMsg {
				Flag = flag,
				Code = 0,
				DataType = typeof(string),
				DataMsg = $"完了: {name} 件数={count} ----{DateTime.Now: MM/dd HH:mm:ss.fff}",
				Progress = endProgress
			};
		}

		var elapsed = DateTime.Now - start;
		yield return new StreamMsg {
			Flag = flag,
			Code = 0,
			DataType = typeof(string),
			DataMsg = $"完了: {elapsed.TotalSeconds:0.0}s  ----{DateTime.Now: MM/dd HH:mm:ss.fff}",
			Progress = 100,
			IsCompleted = true
		};
	}
	#endregion

}
