using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Windows;

namespace CvWpfclient.ViewModels._00System;

public partial class SysExecMiscViewModel : BaseViewModel {
	[ObservableProperty]
	public partial string ResultMessage { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsProcessing { get; set; }

	[RelayCommand(IncludeCancelCommand = true)]
	public async Task Test01(CancellationToken cancellationToken) {
		if (MessageEx.ShowQuestionDialog("環境変数取得？", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		try {
			IsProcessing = true;
			ClientLib.Cursor2Wait();
			cancellationToken.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg { Code = 0, Flag = CvFlag.Msg003_GetEnv };
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (reply?.DataMsg != null && reply?.DataType != null) {
				ResultMessage = reply.DataMsg;
			}
		}
		catch (OperationCanceledException) {
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			return;
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task MasterVColumnResyncAsync(CancellationToken cancellationToken) {
		if (MessageEx.ShowQuestionDialog("マスタ名称の複製列(V*列)を現在のマスタ内容で再同期しますか？", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		try {
			IsProcessing = true;
			// 数分かかることがあるため、開始時刻を先に見せておく
			ResultMessage = $"V*列の再同期を実行中です。{Environment.NewLine}開始 {DateTime.Now:yyyy/MM/dd HH:mm:ss}";
			ClientLib.Cursor2Wait();
			cancellationToken.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg047_MasterVColumnResync,
				DataType = typeof(string),
				DataMsg = string.Empty
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (reply.Code < 0) {
				var detail = !string.IsNullOrWhiteSpace(reply.Option) ? reply.Option : reply.DataMsg;
				ResultMessage = $"V*列の再同期に失敗しました。{Environment.NewLine}{detail}";
				MessageEx.ShowErrorDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
				return;
			}
			ResultMessage = $"V*列の再同期が完了しました。{Environment.NewLine}{reply.DataMsg}";
			MessageEx.ShowInformationDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			return;
		}
		catch (Exception ex) {
			ResultMessage = $"V*列の再同期中にエラーが発生しました。{Environment.NewLine}{ex.Message}";
			MessageEx.ShowErrorDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>
	/// 対象6伝票（売上/店舗売上/生地・付属仕入/商品仕入/受注/発注）の期首日以降を全件走査し、
	/// 取引先マスタ（得意先・仕入先・店舗）の現在値から <c>TaxCalcUnit</c>/<c>TaxRounding</c> を
	/// ヘッダへ再スナップショットしたうえで、<c>TaxableAmount1/2/3</c>・<c>Tax1/2/3</c>・明細Tax・
	/// <c>Total</c> を確定する一括再計算処理（<see cref="CvDomainLogic.TranTaxRebuildDb"/>）。
	/// 計算は現在のマスタ値と明細の生値から一意に決まるため、同じマスタ状態なら何度実行しても結果は変わらない（冪等）。
	/// </summary>
	[RelayCommand(IncludeCancelCommand = true)]
	private async Task TranTaxRebuildAsync(CancellationToken cancellationToken) {
		if (MessageEx.ShowQuestionDialog(
				"対象6伝票（売上/店舗売上/生地・付属仕入/商品仕入/受注/発注）の期首日以降を全件、"
				+ "取引先マスタの現在の税設定（税計算単位・端数処理）で再計算します。"
				+ $"{Environment.NewLine}ヘッダの消費税・総合計も明細合計で再計算されます。"
				+ $"{Environment.NewLine}何度実行しても結果は変わりません（冪等）が、実行後は請求計算・支払計算をやり直してください。"
				+ $"{Environment.NewLine}実行しますか？",
				owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		try {
			IsProcessing = true;
			// 全伝票の走査になり数分かかることがあるため、開始時刻を先に見せておく
			ResultMessage = $"伝票税額再更新を実行中です。{Environment.NewLine}開始 {DateTime.Now:yyyy/MM/dd HH:mm:ss}";
			ClientLib.Cursor2Wait();
			cancellationToken.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg059_TranTaxRebuild,
				DataType = typeof(string),
				DataMsg = string.Empty
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (reply.Code < 0) {
				var detail = !string.IsNullOrWhiteSpace(reply.Option) ? reply.Option : reply.DataMsg;
				ResultMessage = $"伝票税額再更新に失敗しました。{Environment.NewLine}{detail}";
				MessageEx.ShowErrorDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
				return;
			}
			ResultMessage = $"伝票税額再更新が完了しました。{Environment.NewLine}{reply.DataMsg}";
			MessageEx.ShowInformationDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			return;
		}
		catch (Exception ex) {
			ResultMessage = $"伝票税額再更新中にエラーが発生しました。{Environment.NewLine}{ex.Message}";
			MessageEx.ShowErrorDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task MasterShohinMeishoRebuildAsync(CancellationToken cancellationToken) {
		if (MessageEx.ShowQuestionDialog("MasterShohinのId_Col/Id_Sizが0のデータから名称マスタを再構築しますか？", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		try {
			IsProcessing = true;
			ResultMessage = "商品名称マスタ再構築を実行中です。";
			ClientLib.Cursor2Wait();
			cancellationToken.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg046_MasterShohinMeishoRebuild,
				DataType = typeof(string),
				DataMsg = string.Empty
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (reply.Code < 0) {
				var detail = !string.IsNullOrWhiteSpace(reply.Option) ? reply.Option : reply.DataMsg;
				ResultMessage = $"商品名称マスタ再構築に失敗しました。{Environment.NewLine}{detail}";
				MessageEx.ShowErrorDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
				return;
			}
			ResultMessage = "商品名称マスタ再構築が完了しました。";
			MessageEx.ShowInformationDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			return;
		}
		catch (Exception ex) {
			ResultMessage = $"商品名称マスタ再構築中にエラーが発生しました。{Environment.NewLine}{ex.Message}";
			MessageEx.ShowErrorDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>
	/// マニュアル排他制御の強制クリア（詳細設計 §2.5）。既存4処理と異なり、まず状態照会
	/// （<see cref="CvFlag.Msg061_ManualLockStatus"/>）を行い、結果に応じて確認ダイアログの
	/// 要否・内容を変える2往復のフローになる（§2.5.1）。
	/// </summary>
	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ManualLockClearAsync(CancellationToken cancellationToken) {
		try {
			IsProcessing = true;
			ClientLib.Cursor2Wait();
			cancellationToken.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();

			// 1. まず状態照会する（§2.5.1-1）。DBは変更しないため確認ダイアログを出す前に呼んでよい
			var statusMsg = new CvMsg { Code = 0, Flag = CvFlag.Msg061_ManualLockStatus, DataType = typeof(string), DataMsg = string.Empty };
			var statusReply = await coreService.QueryMsgAsync(statusMsg, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (statusReply.Code < 0) {
				var detail = !string.IsNullOrWhiteSpace(statusReply.Option) ? statusReply.Option : statusReply.DataMsg;
				ResultMessage = $"マニュアル排他制御の状態照会に失敗しました。{Environment.NewLine}{detail}";
				MessageEx.ShowErrorDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
				return;
			}
			if (Common.DeserializeObject(statusReply.DataMsg ?? string.Empty, typeof(ManualLockStatus)) is not ManualLockStatus status) {
				ResultMessage = "マニュアル排他制御の状態照会結果を解釈できませんでした。";
				MessageEx.ShowErrorDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
				return;
			}

			// 2. 0件なら確認ダイアログを出さず情報表示だけで終わる（§2.5.1-2）
			if (status.Rows.Count == 0) {
				ResultMessage = "マニュアル排他制御は掛かっていません。";
				MessageEx.ShowInformationDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
				return;
			}

			// 3. 排他行の内容を本文へ埋め込んだYes/No確認ダイアログを出す（§2.5.2）。既定はNo
			var confirmBody = BuildConfirmBody(status);
			if (MessageEx.ShowQuestionDialog(confirmBody, owner: ClientLib.GetActiveView(this), defaultResult: MessageBoxResult.No) != MessageBoxResult.Yes) {
				return;
			}

			// 4. 強制クリアを実行する（Msg062）。
			// 実行社員はサーバーがJWTから解決する（§2.5.3）。「誰が強制解放したか」は後の原因追跡の
			// 要になる監査値であり、利用者が任意に選べる申告値にしてはならないため、ここでは送らない。
			var clearMsg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg062_ManualLockClear,
				DataType = typeof(string),
				DataMsg = string.Empty
			};
			var clearReply = await coreService.QueryMsgAsync(clearMsg, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (clearReply.Code < 0) {
				var detail = !string.IsNullOrWhiteSpace(clearReply.Option) ? clearReply.Option : clearReply.DataMsg;
				ResultMessage = $"マニュアル排他制御クリアに失敗しました。{Environment.NewLine}{detail}";
				MessageEx.ShowErrorDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
				return;
			}
			var deletedCount = Common.DeserializeObject(clearReply.DataMsg ?? string.Empty, typeof(int)) is int count ? count : 0;
			ResultMessage =
				$"マニュアル排他制御クリアが完了しました。{Environment.NewLine}"
				+ $"削除件数={deletedCount}{Environment.NewLine}"
				+ $"実行社員は自動実行履歴（SysHistAutoexec）へ記録しました。{Environment.NewLine}{Environment.NewLine}"
				+ confirmBody;
			MessageEx.ShowInformationDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			return;
		}
		catch (Exception ex) {
			ResultMessage = $"マニュアル排他制御クリア中にエラーが発生しました。{Environment.NewLine}{ex.Message}";
			MessageEx.ShowErrorDialog(ResultMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>
	/// 確認ダイアログ本文を組み立てる（詳細設計 §2.5.2）。行ごとに一連処理名・現在の処理名・処理順No・
	/// 開始日時・最終更新日時・最終更新からの経過時間・予想処理時間・メモを出す。
	/// <c>Vdc</c>/<c>Vdu</c>はUTC Ticksのため、ローカル時刻へ変換して表示する。
	/// いずれかの行が<see cref="ManualLockRow.IsLikelyAlive"/>なら本文冒頭に警告を付ける。
	/// </summary>
	private static string BuildConfirmBody(ManualLockStatus status) {
		var lines = new List<string>();
		if (status.HasLikelyAlive) {
			lines.Add(
				"この処理はまだ動いている可能性があります。実行中に強制クリアすると、"
				+ "別の処理が同時に走り、集計結果が壊れることがあります。");
		}
		else {
			lines.Add("監視タスクが次回起動時に自動解放する見込みです。");
		}
		lines.Add(string.Empty);
		lines.Add("次の排他行を強制クリアします。よろしいですか？");

		foreach (var row in status.Rows) {
			lines.Add(string.Empty);
			lines.Add($"一連処理名: {row.TableName}");
			lines.Add($"現在の処理名: {row.ColumnName}");
			lines.Add($"処理順No: {row.SeqNo}");
			lines.Add($"開始日時: {ToLocalDateTimeText(row.Vdc)}");
			lines.Add($"最終更新日時: {ToLocalDateTimeText(row.Vdu)}");
			lines.Add($"最終更新からの経過時間: {FormatElapsed(row.ElapsedSecondsSinceVdu)}（最終更新から{row.ElapsedSecondsSinceVdu / 60}分）");
			lines.Add($"予想処理時間: {row.ExpectedDuration}秒");
			if (!string.IsNullOrEmpty(row.Memo)) {
				lines.Add($"メモ: {row.Memo}");
			}
		}
		return string.Join(Environment.NewLine, lines);
	}

	private static string ToLocalDateTimeText(long utcTicks) =>
		new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");

	private static string FormatElapsed(long elapsedSeconds) {
		var minutes = elapsedSeconds / 60;
		var seconds = elapsedSeconds % 60;
		return $"{minutes}分{seconds}秒";
	}
}
