/*
# description
BaseViewModel は初期化・終了・キャンセルコマンドの実行、および実行中の非同期コマンド管理を提供する ViewModel の共通基底クラスです。

# example
public partial class SampleViewModel : BaseViewModel { }
 */
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Reflection;
using System.Windows.Input;

namespace CvWpfclient.Helpers;

public partial class BaseViewModel : ObservableObject, IBaseViewModel, IViewModelLifecycle {
	const string InitCommandName = "InitCommand";
	const string CancelCommandSuffix = "CancelCommand";

	public int InitParam { get; set; }
	public string? AddInfo { get; set; }

	public ICommand ExitCommand { get; }
	public bool HasRunningCommand => EnumerateCommandProperties<IAsyncRelayCommand>().Any(cmd => cmd.IsRunning);

	public BaseViewModel() {
		ExitCommand = new RelayCommand(OnExit);
	}

	protected virtual void OnExit() {
		ClientLib.Exit(this);
	}

	public void ExitWithResultTrue() {
		ClientLib.ExitDialogResult(this, true);
	}

	public void ExitWithResultFalse() {
		ClientLib.Exit(this);
	}

	public bool TryExecuteExitCommand() => TryExecuteCommand(nameof(ExitCommand));

	public bool TryExecuteInitCommand() => TryExecuteCommand(InitCommandName);

	public void CancelRunningCommands() {
		foreach (var cmd in EnumerateCommandProperties<ICommand>(prop => prop.Name.EndsWith(CancelCommandSuffix))) {
			if (cmd.CanExecute(null)) {
				cmd.Execute(null);
			}
		}
	}

	bool TryExecuteCommand(string commandName) {
		try {
			var prop = GetType().GetProperty(commandName, BindingFlags.Instance | BindingFlags.Public);
			if (prop?.GetValue(this) is not ICommand cmd || !cmd.CanExecute(null)) {
				return false;
			}
			cmd.Execute(null);
			return true;
		}
		catch {
			return false;
		}
	}

	IEnumerable<TCommand> EnumerateCommandProperties<TCommand>(Func<PropertyInfo, bool>? predicate = null) {
		foreach (var prop in GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
			if (!typeof(TCommand).IsAssignableFrom(prop.PropertyType)) {
				continue;
			}
			if (predicate != null && !predicate(prop)) {
				continue;
			}
			if (prop.GetValue(this) is TCommand cmd) {
				yield return cmd;
			}
		}
	}
}

public interface IBaseViewModel {
	public int InitParam { get; set; }
	public string? AddInfo { get; set; }
}

public interface IViewModelLifecycle {
	public ICommand ExitCommand { get; }
	public bool HasRunningCommand { get; }
	public bool TryExecuteExitCommand();
	public bool TryExecuteInitCommand();
	public void CancelRunningCommands();
}
