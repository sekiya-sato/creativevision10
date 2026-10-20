using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace CvWpfclient.Services;

/// <summary>
/// メニューから開いた画面の起動・終了を操作履歴（SysHistOperation）へ送る（Doc/spec/2026-10-20_メニュー利用履歴_最小変更設計案.md）。
/// <para>
/// 画面を1回開くごとに画面セッション(GUID)を振り、アクティブになった画面を <see cref="AppGlobal.SetCurrentScreen"/> で
/// 通信ヘッダへ反映する。送信は待たずに行い、失敗しても画面の動作は変えない（設計判断 H5）。
/// </para>
/// </summary>
public static class OperationLogClient {
	private static readonly ILogger _logger = new NLogExtender<ViewModels.MainMenuViewModel>();
	private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);
	private static bool _mainWindowHooked;

	/// <summary>
	/// 画面の起動を記録し、アクティブ化・終了を追跡する。画面を表示する直前に呼ぶ。
	/// </summary>
	/// <param name="view">開く画面</param>
	/// <param name="functionId">機能Id</param>
	/// <param name="menuPath">メニュー階層の表示</param>
	/// <param name="condition">起動パラメータのJSON（無ければ空）</param>
	public static void Track(Window view, string? functionId, string menuPath, string condition) {
		HookMainWindow();
		var screenSession = Guid.NewGuid().ToString("D");
		var fid = functionId ?? string.Empty;
		view.Activated += (_, _) => AppGlobal.SetCurrentScreen(screenSession, fid);
		AppGlobal.SetCurrentScreen(screenSession, fid);
		// ログイン画面のように開いた時点で未ログインなら、閉じた時点でログイン済みでも終了だけを送らない
		var opened = Send(new OperationLogParam(OperationKind.MenuOpen, screenSession, fid, menuPath, condition));
		view.Closed += (_, _) => {
			AppGlobal.ClearCurrentScreen(screenSession);
			if (opened) {
				Send(new OperationLogParam(OperationKind.MenuClose, screenSession, fid, string.Empty, string.Empty));
			}
		};
	}

	/// <summary>メインメニューへ戻ったら、以降の要求はメインメニューからのものとして送る</summary>
	private static void HookMainWindow() {
		if (_mainWindowHooked || Application.Current?.MainWindow is not Window main) {
			return;
		}
		main.Activated += (_, _) => AppGlobal.SetCurrentScreen(string.Empty, AppGlobal.MainMenuFunctionId);
		_mainWindowHooked = true;
	}

	/// <returns>送信した場合 true（結果は待たない）</returns>
	private static bool Send(OperationLogParam param) {
		// 未ログインでは認証が通らず記録できないので送らない
		if (string.IsNullOrEmpty(AppGlobal.LoginJwt)) {
			return false;
		}
		_ = SendAsync(param);
		return true;
	}

	private static async Task SendAsync(OperationLogParam param) {
		try {
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg093_OperationLog,
				DataType = typeof(OperationLogParam),
				DataMsg = Common.SerializeObject(param),
			};
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(CancellationToken.None, SendTimeout));
			if (reply.Code < 0) {
				_logger.LogWarning("操作履歴の送信に失敗しました Code={Code} {Option}", reply.Code, reply.Option);
			}
		}
		catch (Exception ex) {
			_logger.LogWarning(ex, "操作履歴の送信に失敗しました Kind={Kind} FunctionId={FunctionId}", param.Kind, param.FunctionId);
		}
	}
}
