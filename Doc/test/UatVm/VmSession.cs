using System.Collections;
using System.Windows;
using CodeShare;
using CvAsset;
using CvBase;
using CvWpfclient;
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

	/// <summary>
	/// DBの値を読み戻す。ViewModelと同じgRPC経路（<c>Msg101_Op_Query</c>）を使う。
	/// </summary>
	/// <remarks>
	/// <typeparamref name="T"/> はサーバー側でも解決できる共有型（`CvBase`のエンティティ等）でなければならない。
	/// ハーネス内で定義した型は使えない。
	/// パラメータは文字列で渡る。SQLiteは動的型のため、整数列と文字列を直接比較すると一致しないことがある。
	/// コード等の文字列列で絞るか、JOINで解決すること。
	/// </remarks>
	public async Task<List<T>> QueryAsync<T>(string sql, params string[] parameters) {
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var message = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListSqlParam),
			DataMsg = Common.SerializeObject(new QueryListSqlParam(typeof(T), sql, [.. parameters])),
		};
		var reply = await coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext());
		if (reply.Code < 0 && reply.Code != -1) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "サーバQueryでエラーが発生しました");
		}
		return Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list
			? list.Cast<T>().ToList()
			: [];
	}

	/// <summary>
	/// ドメインオブジェクトを更新する。ViewModelと同じgRPC経路（<c>Msg201_Op_Execute</c>／`UpdateParam`）を使う。
	/// </summary>
	/// <remarks>
	/// 任意SQLでUPDATEを送るAPIは存在しない（`Msg101_Op_Query`はSELECT/クエリ専用）。
	/// サーバーは楽観排他（`Vdu`一致）で更新するため、<see cref="QueryAsync{T}"/>で取得した
	/// 直近の行をそのまま渡すこと。
	/// </remarks>
	public async Task<T> UpdateAsync<T>(T item) where T : BaseDbClass {
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var message = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg201_Op_Execute,
			DataType = typeof(UpdateParam),
			DataMsg = Common.SerializeObject(new UpdateParam(typeof(T), Common.SerializeObject(item))),
		};
		var reply = await coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext());
		if (reply.Code < 0) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "サーバUpdateでエラーが発生しました");
		}
		return (T)(Common.DeserializeObject(reply.DataMsg ?? "null", reply.DataType)
			?? throw new InvalidOperationException("更新結果を読み取れませんでした。"));
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
