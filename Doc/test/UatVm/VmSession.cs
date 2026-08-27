using System.Windows;
using CvWpfclient.Helpers;

namespace UatVm;

/// <summary>
/// シナリオ1回分の実行文脈。View生成、判定、ダイアログ応答、証跡をまとめる。
/// 常にSTA（Dispatcher）スレッド上で使う。
/// </summary>
public sealed class VmSession {
	private readonly VmHost.Options _options;
	private readonly List<Window> _openedViews = [];
	private Func<MessageExTestRoute.Request, MessageBoxResult>? _dialogResponder;

	internal VmSession(VmHost.Options options, EvidenceWriter evidence) {
		_options = options;
		Evidence = evidence;
	}

	/// <summary>証跡ライター。</summary>
	public EvidenceWriter Evidence { get; }
	/// <summary>PASS件数。</summary>
	public int PassCount { get; private set; }
	/// <summary>FAIL件数。0なら全PASS。</summary>
	public int FailCount { get; private set; }

	/// <summary>
	/// ダイアログ応答を差し替える。既定はYesNo→Yes、他→OK。
	/// 応答の内容自体を検証したい場合に使う。
	/// </summary>
	public void SetDialogResponder(Func<MessageExTestRoute.Request, MessageBoxResult>? responder) {
		_dialogResponder = responder;
	}

	/// <summary>これまでに記録されたダイアログ。</summary>
	public IReadOnlyList<MessageExTestRoute.Record> Dialogs => MessageExTestRoute.Records;

	/// <summary>ダイアログ記録を消す。ケースの区切りで使う。</summary>
	public void ClearDialogs() => MessageExTestRoute.ClearRecords();

	/// <summary>
	/// 既定応答。確認（Yes/No）は安全側の No を返す。
	/// 起動時の更新確認のような想定外の問い合わせに Yes を返して副作用を起こさないため、
	/// 進めたい確認はシナリオ側が <see cref="SetDialogResponder"/> で明示する。
	/// </summary>
	public static MessageBoxResult StrictResponder(MessageExTestRoute.Request request) => request.Button switch {
		MessageBoxButton.YesNo => MessageBoxResult.No,
		MessageBoxButton.YesNoCancel => MessageBoxResult.No,
		MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
		_ => MessageBoxResult.OK,
	};

	internal MessageBoxResult OnDialog(MessageExTestRoute.Request request) {
		var result = (_dialogResponder ?? StrictResponder)(request);
		Evidence.Write("dialog", request.Kind, new {
			image = request.Image.ToString(),
			button = request.Button.ToString(),
			message = request.Message,
			appended = request.AppendedMessage,
			answered = result.ToString(),
		});
		return result;
	}

	/// <summary>
	/// Viewを実インスタンスとして生成し、そのDataContextのViewModelを取り出す。
	/// </summary>
	/// <typeparam name="TView">対象のView（Window派生）。</typeparam>
	/// <typeparam name="TViewModel">期待するViewModelの型。</typeparam>
	public ViewDriver<TViewModel> OpenView<TView, TViewModel>()
		where TView : Window, new()
		where TViewModel : class {
		var view = new TView();
		if (view.DataContext is not TViewModel vm) {
			throw new InvalidOperationException(
				$"{typeof(TView).Name} の DataContext が {typeof(TViewModel).Name} ではありません（実際: {view.DataContext?.GetType().Name ?? "null"}）。");
		}
		if (_options.ShowViews) {
			// 実描画とバインディング評価を伴わせる。ClientLib.GetActiveView が Application.Current.Windows から
			// ViewModelに対応するWindowを引けるようにするためにも表示しておく。
			view.Show();
		}
		_openedViews.Add(view);
		Evidence.Write("view", "opened", new { view = typeof(TView).FullName, vm = typeof(TViewModel).FullName, shown = _options.ShowViews });
		return new ViewDriver<TViewModel>(this, view, vm);
	}

	/// <summary>開いたViewを全て閉じる。</summary>
	public void CloseViews() {
		foreach (var view in _openedViews.AsEnumerable().Reverse()) {
			try { view.Close(); }
			catch (InvalidOperationException) { /* 既に閉じている */ }
		}
		_openedViews.Clear();
	}

	/// <summary>判定を記録する。</summary>
	public bool Check(string name, bool condition, object? detail = null) {
		if (condition) {
			PassCount++;
			Evidence.Write("check", name, new { result = "PASS", detail });
		}
		else {
			FailCount++;
			Evidence.Write("check", name, new { result = "FAIL", detail });
		}
		return condition;
	}

	/// <summary>期待値と実際値を比較して記録する。</summary>
	public bool CheckEqual<T>(string name, T expected, T actual) =>
		Check(name, EqualityComparer<T>.Default.Equals(expected, actual), new { expected, actual });

	/// <summary>失敗を記録する。</summary>
	public void Fail(string name, string detail) {
		FailCount++;
		Evidence.Write("fail", name, new { detail });
	}

	/// <summary>任意の情報を記録する。</summary>
	public void Note(string name, object? data = null) => Evidence.Write("note", name, data);

	internal int Complete() {
		CloseViews();
		Evidence.Write("result", _options.ScenarioName, new {
			pass = PassCount,
			fail = FailCount,
			verdict = FailCount == 0 ? "PASS" : "FAIL",
		});
		return FailCount;
	}
}
