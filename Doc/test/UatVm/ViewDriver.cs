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
