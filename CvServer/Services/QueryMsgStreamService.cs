using CodeShare;
using CvAsset;
using CvBase;
using CvBaseOracle;
using CvDomainLogic;
using Microsoft.AspNetCore.Authorization;
using ProtoBuf.Grpc;


namespace CvServer.Services;

public partial class CoreService {
	/// <summary>
	/// 進捗をクライアント表示用の1行に整形する。
	/// ステップの開始と終了は <see cref="StreamStepProgress.Phase"/> で文言を分ける
	/// （開始時は件数が未確定なので「件数=0」を出さない）。
	/// </summary>
	private static string FormatProgressMessage(StreamStepProgress progress) {
	var time = $"----{DateTime.Now: MM/dd HH:mm:ss.fff}";
	if (progress.IsError) {
		return $"エラー: {progress.StepName} - {progress.ErrorMessage} {time}";
	}
	return progress.Phase switch {
		StreamStepProgressPhase.Started => $"開始: {progress.StepName} {time}",
		StreamStepProgressPhase.Error => $"エラー: {progress.StepName} - {progress.ErrorMessage} {time}",
		StreamStepProgressPhase.Completed => $"全処理完了: 所要={(progress.ElapsedSeconds == 0 ? progress.ErrorMessage : 
			TimeSpan.FromSeconds(progress.ElapsedSeconds).ToStrSpan())} {time}",
		_ => $"完了: {progress.StepName} 件数={progress.Count:N0} 所要={TimeSpan.FromSeconds(progress.ElapsedSeconds).ToStrSpan()}s {time}",
	};
}

	private static StreamMsg CreateProgressStreamMsg(CvFlag flag, StreamStepProgress progress) => new() {
		Flag = flag,
		Code = progress.IsError ? -1 : 0,
		DataType = typeof(string),
		DataMsg = FormatProgressMessage(progress),
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
		else if (request.Flag is CvFlag.Msg050_Summary
			or CvFlag.Msg051_SummaryRealStock
			or CvFlag.Msg052_SummaryUriKake
			or CvFlag.Msg053_SummaryKaiKake
			or CvFlag.Msg054_StocktakeStart
			or CvFlag.Msg055_StocktakeFix
			or CvFlag.Msg056_SummaryUriSei
			or CvFlag.Msg057_SummaryKaiShi
			or CvFlag.Msg058_HhtDataUpdate) {
			await foreach (var msg in HandleSummaryStreamAsync(ct, request)) {
				yield return msg;
			}
			yield break;
		}
		// 原価4処理・評価替えの更新実行(Step 9)。Apply*は内部で既にマニュアル排他制御を取得しているため
		// (設計書 CostUpdateDb.cs 参照)、StreamStepProgressRunnerの排他引数は使わない(二重取得になる)。
		else if (request.Flag is CvFlag.Msg082_CostConsumptionApply
			or CvFlag.Msg085_CostLastPurchaseApply
			or CvFlag.Msg087_CostTotalAverageApply
			or CvFlag.Msg089_CostRevaluationApply) {
			await foreach (var msg in HandleCostUpdateApplyStreamAsync(ct, request)) {
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
		var convertDb = CreateConvertDb();

		await foreach (var msg in ForwardProgressStreamAsync(flag, convertDb.ConvertAllAsyncStream(isInit), ct)) {
			yield return msg;
		}
	}
	/// <summary>
	/// ConvertDbのストリーミング処理ハンドラ
	/// </summary>
	private async IAsyncEnumerable<StreamMsg> HandleConvertSelectedStreamAsync(List<string> selectedTask, bool isInit, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, CvFlag flag) {
		var convertDb = CreateConvertDb();

		await foreach (var msg in ForwardProgressStreamAsync(flag, convertDb.ConvertSelectAsyncStream(selectedTask, isInit), ct)) {
			yield return msg;
		}
	}
	/// <summary>
	/// 集計処理とHHTデータ更新のストリーミング処理ハンドラ
	/// </summary>
	private async IAsyncEnumerable<StreamMsg> HandleSummaryStreamAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, CvMsg request) {
		var summaryDb = new SummaryDb(_db);
		var stocktakeDb = new StocktakeDb(_db);
		var hhtProcess = new HhtProcess(_db);

		var param = Common.DeserializeObject(request.DataMsg, request.DataType);
		var stream = (request.Flag, param) switch {
			(CvFlag.Msg050_Summary, CalcDateTermParameter summaryParam) => summaryDb.SummaryAllAsyncStream(summaryParam),
			(CvFlag.Msg051_SummaryRealStock, CalcDateParameter summaryReal) => summaryDb.SummaryRealAsyncStream(summaryReal),
			(CvFlag.Msg052_SummaryUriKake, CalcDateTermParameter uriKakeParam) => summaryDb.SummaryUriKakeAsyncStream(uriKakeParam),
			(CvFlag.Msg053_SummaryKaiKake, CalcDateTermParameter kaiKakeParam) => summaryDb.SummaryKaiKakeAsyncStream(kaiKakeParam),
			(CvFlag.Msg054_StocktakeStart, StocktakeParameter startParam) => stocktakeDb.StartAsyncStream(startParam),
			(CvFlag.Msg055_StocktakeFix, StocktakeParameter fixParam) => stocktakeDb.FixAsyncStream(fixParam),
			(CvFlag.Msg056_SummaryUriSei, BillingParameter uriSeiParam) => summaryDb.SummaryUriSeiAsyncStream(uriSeiParam),
			(CvFlag.Msg057_SummaryKaiShi, BillingParameter kaiShiParam) => summaryDb.SummaryKaiShiAsyncStream(kaiShiParam),
			(CvFlag.Msg058_HhtDataUpdate, HhtUpdateParameter hhtParam) => hhtProcess.UpdateVulcan2TranAsyncStream(hhtParam),
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

	#region 原価4処理・評価替えの更新実行(Step 9)
	// 正典は `Doc/spec/2026-09-05_原価4項目_詳細設計.md` §8.1・§9.3、
	// `Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md` §2.4。
	//
	// 設計上の注意: Apply*(ApplyConsumptionPurchases/ApplyLastPurchaseCost/ApplyTotalAverageCost/
	// ApplyRevaluation)はCostUpdateDb内部で既にマニュアル排他制御(ManualLockDb.TryBegin)を取得している
	// (Step 9-3)。StreamStepProgressRunner.Runの排他引数(manualLockDb/lockProcessName)を使うと、
	// 外側でTryBeginしたのと同じ一連処理名の行が既に存在する状態で内側のTryBeginが呼ばれ、
	// 必ず「先行処理あり」で失敗する(二重取得)。そのためここではRunの排他引数を使わず、
	// (b) ストリームハンドラ内で直接StreamMsgを組み立てる方式を採る。
	// Apply*は同期メソッドでCostUpdateResult(UpdatedCount/ErrorCount/BatchId/Messageを含む)を返すため、
	// Run(steps: IReadOnlyList<(string,Func<TArg,int>)>)の「ステップ名+件数(int)」だけの形にも合わない
	// (Runを使うにはCostUpdateResultの構造化情報をstring/intへ落とし込む必要があり、そのほうが情報を失う)。

	/// <summary>
	/// パラメータのデシリアライズに失敗した場合の共通エラーストリームメッセージ(既存の分岐と同じ書式)。
	/// </summary>
	private static StreamMsg CreateCostUpdateParamErrorStreamMsg(CvFlag flag) => new() {
		Flag = flag,
		Code = -1,
		DataType = typeof(string),
		DataMsg = $"エラー: パラメータのデシリアライズに失敗 ----{DateTime.Now: MM/dd HH:mm:ss.fff}",
		Progress = 0,
		IsCompleted = true,
		IsError = true
	};

	/// <summary>開始通知(件数は未確定のため出さない。既存のFormatProgressMessageの書式に合わせる)。</summary>
	private static StreamMsg CreateCostUpdateStartedStreamMsg(CvFlag flag, string stepName) => new() {
		Flag = flag,
		Code = 0,
		DataType = typeof(string),
		DataMsg = $"開始: {stepName} ----{DateTime.Now: MM/dd HH:mm:ss.fff}",
		Progress = 0,
		IsCompleted = false,
		IsError = false
	};

	/// <summary>
	/// <see cref="CostUpdateResult"/>を最終ストリームメッセージへ変換する。<c>UpdatedCount</c>・<c>ErrorCount</c>・
	/// <c>BatchId</c>・<c>Message</c>を失わないよう、<c>CostUpdateResult</c>そのものを<c>DataMsg</c>へ載せる。
	/// <c>IsSuccess=false</c>のとき(排他取得失敗・エラー行あり・原価方式不一致など)は
	/// エラーとしてストリームを終える。
	/// </summary>
	private static StreamMsg CreateCostUpdateResultStreamMsg(CvFlag flag, CostUpdateResult result) => new() {
		Flag = flag,
		Code = result.IsSuccess ? 0 : CvMsgErrorCode.InvalidParameter,
		DataType = typeof(CostUpdateResult),
		DataMsg = Common.SerializeObject(result),
		Progress = 100,
		IsCompleted = true,
		IsError = !result.IsSuccess
	};

	/// <summary>
	/// 例外で中断した場合も<see cref="CostUpdateResult"/>と同じ形へ包んで返す。
	/// <see cref="ConsumptionPurchasePaidPeriodException"/>・<see cref="CostRevaluationPaidPeriodException"/>は
	/// 「支払計算を取り消してから再実行してください」まで<c>Message</c>に含む(例外メッセージをそのまま使う)。
	/// </summary>
	private static StreamMsg CreateCostUpdateExceptionStreamMsg(CvFlag flag, string targetMonth, string batchId, string message) {
		var result = new CostUpdateResult {
			IsSuccess = false,
			BatchId = batchId,
			TargetMonth = targetMonth,
			UpdatedCount = 0,
			ErrorCount = 0,
			Message = message,
			StartedAt = 0,
			FinishedAt = Common.GetVdate(),
		};
		return CreateCostUpdateResultStreamMsg(flag, result);
	}

	/// <summary>
	/// クライアントが<c>BatchId</c>を空文字で送ってきた場合に、サーバー側でGUIDのD形式(36文字)を採番する
	/// (原価4項目 詳細設計 §2.5.2「実行IDはGUIDのD形式」)。空でなければ、確認(プレビュー)と更新で
	/// 同一値を使う運用のためクライアント指定値をそのまま使う。純関数として切り出し、単体テスト対象にする。
	/// </summary>
	public static string ResolveBatchId(string? batchId) =>
		string.IsNullOrEmpty(batchId) ? Guid.NewGuid().ToString("D") : batchId;

	/// <summary>
	/// 開始通知 → 実行 → 結果通知の1系列を組み立てる共通処理。<paramref name="apply"/>は同期処理のため
	/// <see cref="Task.Run(Func{Task},CancellationToken)"/>相当で実行し、呼び出し元スレッドを塞がない。
	/// </summary>
	private static async IAsyncEnumerable<StreamMsg> RunCostApplyStreamAsync(
		CvFlag flag,
		string stepName,
		string targetMonth,
		string batchId,
		Func<CostUpdateResult> apply,
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) {
		yield return CreateCostUpdateStartedStreamMsg(flag, stepName);

		StreamMsg finalMsg;
		try {
			var result = await Task.Run(apply, ct);
			finalMsg = CreateCostUpdateResultStreamMsg(flag, result);
		}
		catch (ConsumptionPurchasePaidPeriodException ex) {
			// 例外を握りつぶさず、利用者に「支払計算を取り消してから再実行する」旨が伝わる形で終える(設計書§4.6)。
			finalMsg = CreateCostUpdateExceptionStreamMsg(flag, targetMonth, batchId, ex.Message);
		}
		catch (CostRevaluationPaidPeriodException ex) {
			finalMsg = CreateCostUpdateExceptionStreamMsg(flag, targetMonth, batchId, ex.Message);
		}
		catch (Exception ex) {
			finalMsg = CreateCostUpdateExceptionStreamMsg(flag, targetMonth, batchId, ex.Message);
		}
		yield return finalMsg;
	}

	/// <summary>
	/// 原価4処理・評価替えの更新実行(Msg082/085/087/089)のディスパッチ。パラメータ型で処理を振り分け、
	/// <c>Id_Shain</c>をJWT解決値へ上書きし(監査値のため。TranGenka.Id_Shain/TranGenkaReval.Id_Shainへ書く値であり、利用者が任意に指定できてはならない)、<c>BatchId</c>が空文字なら
	/// サーバー側で採番してから<see cref="RunCostApplyStreamAsync"/>へ渡す。
	/// <para>
	/// <c>Confirmed</c>（<see cref="CostConfirmSnapshot"/>、設計書§2.4-4）は<c>Id_Shain</c>とは異なり
	/// 上書きしない。クライアントが「自分が見た確認結果の時点」を主張するための値であり、
	/// サーバーが上書きすると確認〜更新間の変更検知そのものが機能しなくなるため。
	/// </para>
	/// </summary>
	private IAsyncEnumerable<StreamMsg> HandleCostUpdateApplyStreamAsync(CancellationToken ct, CvMsg request) {
		var param = Common.DeserializeObject(request.DataMsg ?? string.Empty, request.DataType);
		var costDb = new CostUpdateDb(_db);
		var idShain = ResolveLoginShainId();

		switch (request.Flag, param) {
			case (CvFlag.Msg082_CostConsumptionApply, CostUpdateParameter p):
				p.Id_Shain = idShain;
				p.BatchId = ResolveBatchId(p.BatchId);
				return RunCostApplyStreamAsync(request.Flag, "消化仕入更新", p.TargetMonth, p.BatchId,
					() => costDb.ApplyConsumptionPurchases(p), ct);
			case (CvFlag.Msg085_CostLastPurchaseApply, CostUpdateParameter p):
				p.Id_Shain = idShain;
				p.BatchId = ResolveBatchId(p.BatchId);
				return RunCostApplyStreamAsync(request.Flag, "最終仕入原価更新", p.TargetMonth, p.BatchId,
					() => costDb.ApplyLastPurchaseCost(p), ct);
			case (CvFlag.Msg087_CostTotalAverageApply, CostUpdateParameter p):
				p.Id_Shain = idShain;
				p.BatchId = ResolveBatchId(p.BatchId);
				return RunCostApplyStreamAsync(request.Flag, "総平均原価更新", p.TargetMonth, p.BatchId,
					() => costDb.ApplyTotalAverageCost(p), ct);
			case (CvFlag.Msg089_CostRevaluationApply, CostRevaluationParameter rp): {
				var resolvedBatchId = ResolveBatchId(rp.BatchId);
				var overridden = rp with { Id_Shain = idShain, BatchId = resolvedBatchId };
				return RunCostApplyStreamAsync(request.Flag, "評価替え", overridden.TargetMonth, overridden.BatchId,
					() => costDb.ApplyRevaluation(overridden), ct);
			}
			default:
				return SingleMsgStream(CreateCostUpdateParamErrorStreamMsg(request.Flag));
		}
	}

	/// <summary>1件だけのStreamMsgを<see cref="IAsyncEnumerable{T}"/>へ包む(パラメータ不正時の共通処理用)。</summary>
	private static async IAsyncEnumerable<StreamMsg> SingleMsgStream(StreamMsg msg) {
		await Task.Yield();
		yield return msg;
	}
	#endregion

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
