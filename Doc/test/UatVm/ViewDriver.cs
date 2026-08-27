using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.Input;

namespace UatVm;

/// <summary>
/// 生成済みのViewとそのViewModelを操作する。
/// 画面のマウス・キー操作は行わず、ViewModelのプロパティとコマンドを直接駆動する。
/// </summary>
/// <typeparam name="TViewModel">対象ViewModelの型。</typeparam>
public sealed class ViewDriver<TViewModel> where TViewModel : class {
	private readonly VmSession _session;

	internal ViewDriver(VmSession session, Window view, TViewModel viewModel) {
		_session = session;
		View = view;
		Vm = viewModel;
	}

	/// <summary>実生成されたView。</summary>
	public Window View { get; }
	/// <summary>ViewのDataContextであるViewModel。</summary>
	public TViewModel Vm { get; }

	/// <summary>ViewModelへ入力値を設定し、内容を証跡へ残す。</summary>
	public ViewDriver<TViewModel> Input(string name, Action<TViewModel> set, object? recorded = null) {
		set(Vm);
		_session.Evidence.Write("input", name, recorded);
		return this;
	}

	/// <summary>
	/// 非同期コマンド（<see cref="IAsyncRelayCommand"/>）を実行して完了まで待つ。
	/// </summary>
	public async Task RunAsync(string name, Func<TViewModel, IAsyncRelayCommand> selector, object? parameter = null) {
		var command = selector(Vm);
		var sw = Stopwatch.StartNew();
		_session.Evidence.Write("command", $"{name}:start", null);
		try {
			await command.ExecuteAsync(parameter);
			// ExecuteAsync が先に戻っても内部タスクが残る実装に備えて待ち合わせる。
			if (command.ExecutionTask is { IsCompleted: false } task) await task;
			_session.Evidence.Write("command", $"{name}:end", new { ms = sw.ElapsedMilliseconds });
		}
		catch (Exception ex) {
			// ViewModel側で捕捉されず抜けてきた例外はシナリオの失敗として残す。
			_session.Fail($"{name}:exception", ex.ToString());
			throw;
		}
	}

	/// <summary>同期コマンドを実行する。</summary>
	public void Run(string name, Func<TViewModel, IRelayCommand> selector, object? parameter = null) {
		_session.Evidence.Write("command", name, null);
		selector(Vm).Execute(parameter);
	}

	/// <summary>
	/// 非同期コマンドを開始し、<paramref name="cancelAfter"/>だけ待ってから
	/// `{selector名}CancelCommand`（`IncludeCancelCommand = true`が生成する取消コマンド）を実行する。
	/// 完了まで待って結果を返す（キャンセルにより例外・警告に落ちても呼び出し側で判定できるよう例外は投げない）。
	/// </summary>
	/// <param name="name">証跡上の名前。</param>
	/// <param name="selector">対象の非同期コマンド。</param>
	/// <param name="cancelCommandSelector">対応する取消コマンド（`IRelayCommand`、通常は引数なし）。</param>
	/// <param name="cancelAfter">実行開始からキャンセルまでの待ち時間。</param>
	public async Task<bool> RunAndCancelAsync(
		string name,
		Func<TViewModel, IAsyncRelayCommand> selector,
		Func<TViewModel, System.Windows.Input.ICommand> cancelCommandSelector,
		TimeSpan cancelAfter,
		object? parameter = null) {
		var command = selector(Vm);
		_session.Evidence.Write("command", $"{name}:start", new { cancelAfterMs = cancelAfter.TotalMilliseconds });
		var task = command.ExecuteAsync(parameter);

		await Task.Delay(cancelAfter);
		_session.Evidence.Write("command", $"{name}:cancel", null);
		cancelCommandSelector(Vm).Execute(null);

		try {
			await task;
		}
		catch (OperationCanceledException) {
			// ViewModel側で捕捉せずに抜けてくる実装もあるため、ここでも許容する。
		}
		_session.Evidence.Write("command", $"{name}:end", null);
		return true;
	}

	/// <summary>
	/// 条件が成立するまで待つ。成立しなければ失敗として記録する。
	/// ロード時に自動実行されるコマンドの完了待ちなどに使う。
	/// </summary>
	public async Task<bool> WaitAsync(string name, Func<TViewModel, bool> condition, int timeoutMs = 60_000, int pollMs = 50) {
		var sw = Stopwatch.StartNew();
		while (sw.ElapsedMilliseconds < timeoutMs) {
			if (condition(Vm)) {
				_session.Evidence.Write("wait", name, new { ms = sw.ElapsedMilliseconds });
				return true;
			}
			await Task.Delay(pollMs);
		}
		_session.Fail($"wait:{name}", $"{timeoutMs}ms 以内に条件が成立しませんでした。");
		return false;
	}

	/// <summary>ViewModelの状態を証跡へ残す。</summary>
	public ViewDriver<TViewModel> Snapshot(string name, Func<TViewModel, object> projection) {
		_session.Evidence.Write("state", name, projection(Vm));
		return this;
	}
}
