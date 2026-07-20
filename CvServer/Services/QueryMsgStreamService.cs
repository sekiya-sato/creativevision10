using CodeShare;
using CvAsset;
using CvBase;
using CvBaseOracle;
using CvDomainLogic;
using Microsoft.AspNetCore.Authorization;
using ProtoBuf.Grpc;


namespace CvServer.Services;

public partial class CoreService {
	private static StreamMsg CreateProgressStreamMsg(CvFlag flag, StreamStepProgress progress) => new() {
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

	private async IAsyncEnumerable<StreamMsg> ForwardProgressStreamAsync(
		CvFlag flag,
		IAsyncEnumerable<StreamStepProgress> stream,
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) {
		await foreach (var progress in stream.WithCancellation(ct)) {
			yield return CreateProgressStreamMsg(flag, progress);
		}
	}

	/// <summary>
	/// ストリーミングメッセージを処理する
	/// </summary>
	/// <param name="request"></param>
	/// <param name="context"></param>
	/// <returns></returns>
	// Product : テストが終わったら、[AllowAnonymous] を [Authorize] へ変更
	[AllowAnonymous]
	//[Authorize]
	public async IAsyncEnumerable<StreamMsg> QueryMsgStreamAsync(CvMsg request, CallContext context = default) {
		ArgumentNullException.ThrowIfNull(request);
		var ct = context.CancellationToken;
		_logger.LogInformation("gRPCストリーミングリクエスト QueryMsgStreamAsync Flag: {Flag}, DataType: {DataType}", request.Flag, request.DataType);
		await Task.Yield();

		// ConvertDb関連フラグの処理
		if (request.Flag is CvFlag.Msg040_ConvertDb) {
			var param = Common.DeserializeObject(request.DataMsg ?? string.Empty, request.DataType);
			if (param is ConvertDbParam convertDb) {
				await foreach (var msg in HandleConvertDbStreamAsync(convertDb.IsInit, ct, request.Flag)) {
					yield return msg;
				}
				yield break;
			}
			else if (param is ConvertSelectedDbParam convertSelected) {
				await foreach (var msg in HandleConvertSelectedStreamAsync(convertSelected.SelectedTask, convertSelected.IsInit, ct, request.Flag)) {
					yield return msg;
				}
				yield break;
			}
		}
		// 	集計処理
		else if (request.Flag is CvFlag.Msg050_Summary or CvFlag.Msg051_SummaryRealStock) {
			await foreach (var msg in HandleSummaryStreamAsync(ct, request)) {
				yield return msg;
			}
			yield break;
		}
		// テストストリーミング処理（既存）
		else if (request.Flag is CvFlag.Msg710_StreamingTest) {
			// 追加：HandleConvertTestStreamAsync を呼ぶ
			await foreach (var msg in HandleConvertTestStreamAsync(ct, request.Flag)) {
				yield return msg;
			}
			yield break;
		}
		else {
			yield return new StreamMsg {
				Flag = request.Flag,
				Code = -1,
				DataType = typeof(string),
				DataMsg = $"エラー: パラメータのデシリアライズに失敗 ----{DateTime.Now: MM/dd HH:mm:ss.fff}",
				Progress = 0,
				IsCompleted = true,
				IsError = true
			};
		}
	}

	/// <summary>
	/// ConvertDbのストリーミング処理ハンドラ
	/// </summary>
	private async IAsyncEnumerable<StreamMsg> HandleConvertDbStreamAsync(bool isInit, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, CvFlag flag) {
		var oracleConnectionString = _configuration.GetConnectionString("oracle") ?? string.Empty;
		var fromDb = ExDatabaseOracle.GetDbConn(oracleConnectionString);

		var convertDb = new ConvertDb(fromDb, _db);

		await foreach (var msg in ForwardProgressStreamAsync(flag, convertDb.ConvertAllAsyncStream(isInit), ct)) {
			yield return msg;
		}
	}
	/// <summary>
	/// ConvertDbのストリーミング処理ハンドラ
	/// </summary>
	private async IAsyncEnumerable<StreamMsg> HandleConvertSelectedStreamAsync(List<string> selectedTask, bool isInit, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, CvFlag flag) {
		var oracleConnectionString = _configuration.GetConnectionString("oracle") ?? string.Empty;
		var fromDb = ExDatabaseOracle.GetDbConn(oracleConnectionString);

		var convertDb = new ConvertDb(fromDb, _db);

		await foreach (var msg in ForwardProgressStreamAsync(flag, convertDb.ConvertSelectAsyncStream(selectedTask, isInit), ct)) {
			yield return msg;
		}
	}
	/// <summary>
	/// 集計処理のストリーミング処理ハンドラ
	/// </summary>
	private async IAsyncEnumerable<StreamMsg> HandleSummaryStreamAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, CvMsg request) {
		var summaryDb = new SummaryDb(_db);

		var param = Common.DeserializeObject(request.DataMsg, request.DataType);
		var stream = param switch {
			CalcDateTermParameter summaryParam => summaryDb.SummaryAllAsyncStream(summaryParam),
			CalcDateParameter summaryReal => summaryDb.SummaryRealAsyncStream(summaryReal),
			_ => null
		};

		if (stream is null) {
			yield return new StreamMsg {
				Flag = request.Flag,
				Code = -1,
				DataType = typeof(string),
				DataMsg = $"エラー: パラメータのデシリアライズに失敗 ----{DateTime.Now: MM/dd HH:mm:ss.fff}",
				Progress = 0,
				IsCompleted = true,
				IsError = true
			};
			yield break;
		}

		await foreach (var msg in ForwardProgressStreamAsync(request.Flag, stream, ct)) {
			yield return msg;
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
		string[] stepNames = new[] {
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
