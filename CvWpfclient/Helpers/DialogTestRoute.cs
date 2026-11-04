/*
# description
DialogTestRoute は、ClientLib.ShowDialogView で開くモーダル画面（一覧条件の選択Winなど）のテスト専用ルートです。

UATハーネス（Doc/test/UatVm）から ViewModel のコマンドを直接駆動すると、一覧取得の前に開く条件指定ダイアログ
（RangeParamView / RangeInputParamView / PointMasterSearchParamView など）がモーダルのまま止まり、
一覧のSQLが実行されない。有効化している間は、ダイアログを実際に表示したうえで（初期化・入力検証・
バインディングは本番と同じに通す）、描画と初期化コマンドの完了を待ってから応答関数を呼び、閉じる。
無効（既定）のときは何もしない。

応答関数は Window を受け取り、条件を入れたり撮影したりしてから DialogResult を返す。
既定の応答は「既定条件のまま OK」で、ViewModel の OkCommand を実行する（OkCommand が自分で閉じる）。
OkCommand が無い・実行できない画面はキャンセル(false)で閉じる。

# example
DialogTestRoute.Enable();                                        // 既定: 既定条件のまま OK
DialogTestRoute.Enable(win => win is RangeParamView ? true : false);
await vm.DoListOnListTabCommand.ExecuteAsync(null);
var opened = DialogTestRoute.Records;                            // 開いたダイアログと応答
DialogTestRoute.Disable();
 */
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;

namespace CvWpfclient.Helpers;

/// <summary>
/// ShowDialogView で開くモーダル画面のテスト専用ルート。応答をプログラムで与えて記録する。
/// </summary>
/// <remarks>本番実行では常に無効で、<see cref="ClientLib.ShowDialogView"/> の挙動は従来と同一。</remarks>
public static class DialogTestRoute {
	/// <summary>開いたダイアログと、閉じたときの結果</summary>
	public sealed record Record(string ViewType, string Title, bool? Result, string Note);

	static readonly object _sync = new();
	static readonly List<Record> _records = [];
	static Func<Window, bool?>? _responder;

	/// <summary>初期化コマンドの完了を待つ上限</summary>
	const int InitTimeoutMs = 30_000;

	/// <summary>テストルートが有効か</summary>
	public static bool IsActive {
		get { lock (_sync) return _responder != null; }
	}

	/// <summary>
	/// テストルートを有効にする。
	/// </summary>
	/// <param name="responder">応答関数。null のときは <see cref="DefaultResponder"/>（既定条件のまま OK）。
	/// 戻り値 null は「応答関数の中で ViewModel が自分で閉じた（または閉じる）」の意味。</param>
	/// <param name="clearRecords">これまでの記録を消すか</param>
	public static void Enable(Func<Window, bool?>? responder = null, bool clearRecords = true) {
		lock (_sync) {
			_responder = responder ?? DefaultResponder;
			if (clearRecords) _records.Clear();
		}
	}

	/// <summary>テストルートを無効にする。記録は残す。</summary>
	public static void Disable() {
		lock (_sync) _responder = null;
	}

	/// <summary>記録の一覧（開いた順）</summary>
	public static IReadOnlyList<Record> Records {
		get { lock (_sync) return [.. _records]; }
	}

	/// <summary>記録を消す</summary>
	public static void ClearRecords() {
		lock (_sync) _records.Clear();
	}

	/// <summary>
	/// 既定応答: ViewModel の OkCommand を実行する（既定条件のまま確定）。OkCommand が自分で閉じるので null を返す。
	/// OkCommand が無い・実行できないときは false（キャンセル）。
	/// </summary>
	public static bool? DefaultResponder(Window window) {
		var command = window.DataContext?.GetType().GetProperty("OkCommand", BindingFlags.Instance | BindingFlags.Public)?.GetValue(window.DataContext) as ICommand;
		if (command == null || !command.CanExecute(null))
			return false;
		command.Execute(null);
		return null;
	}

	/// <summary>
	/// <see cref="ClientLib.ShowDialogView"/> が ShowDialog の直前に呼ぶ。描画後に応答関数を呼んで閉じる仕掛けを付ける。
	/// </summary>
	internal static void Attach(Window window) {
		Func<Window, bool?>? responder;
		lock (_sync) responder = _responder;
		if (responder == null)
			return;
		window.ContentRendered += async (_, _) => {
			var note = string.Empty;
			bool? result = null;
			try {
				await WaitIdleAsync(window.DataContext);
				result = responder(window);
				// 非同期の確定処理（ViewModel が閉じる）を待つ
				await WaitIdleAsync(window.DataContext);
				if (window.IsVisible) {
					window.DialogResult = result ?? false;
					note = result == null ? "応答後も開いていたためキャンセルで閉じた" : string.Empty;
				}
			}
			catch (Exception ex) {
				note = $"例外: {ex.Message}";
				if (window.IsVisible) {
					try { window.DialogResult = false; } catch (InvalidOperationException) { window.Close(); }
				}
			}
			lock (_sync) _records.Add(new Record(window.GetType().Name, window.Title, window.DialogResult ?? result, note));
		};
	}

	/// <summary>ViewModel の非同期コマンドが走っている間待つ（初期化・確定処理）</summary>
	static async Task WaitIdleAsync(object? dataContext) {
		var deadline = DateTime.UtcNow.AddMilliseconds(InitTimeoutMs);
		await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
		while (IsRunning(dataContext) && DateTime.UtcNow < deadline)
			await Task.Delay(50);
	}

	static bool IsRunning(object? dataContext) {
		if (dataContext == null) return false;
		foreach (var prop in dataContext.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
			if (typeof(IAsyncRelayCommand).IsAssignableFrom(prop.PropertyType)
				&& prop.GetValue(dataContext) is IAsyncRelayCommand { IsRunning: true })
				return true;
		}
		return false;
	}
}
