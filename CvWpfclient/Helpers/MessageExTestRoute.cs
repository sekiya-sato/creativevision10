/*
# description
MessageExTestRoute は MessageEx のテスト専用ルートです。
有効化している間、MessageEx は実際のモーダル画面（MessageBoxView）を生成せず、
ここへ問い合わせて応答を得ます。同時に、出たダイアログを記録します。

UATハーネス（Doc/test/UatVm）から ViewModel のコマンドを直接駆動する際、
確認ダイアログで停止させないために使います。無効（既定）のときは何もしません。

# example
MessageExTestRoute.Enable();                                  // 既定応答（YesNo→Yes、他→OK）
MessageExTestRoute.Enable(req => req.Message.Contains("実行しますか")
    ? MessageBoxResult.Yes : MessageBoxResult.OK);             // 個別応答
await vm.ExecuteCommand.ExecuteAsync(null);
var warnings = MessageExTestRoute.Records.Where(x => x.Request.Image == MessageBoxImage.Warning);
MessageExTestRoute.Disable();
 */
using System.Windows;

namespace CvWpfclient.Helpers;

/// <summary>
/// MessageEx のテスト専用ルート。実ダイアログを抑止し、応答をプログラムで与えて記録する。
/// [Test-only route for MessageEx: suppresses real dialogs, supplies responses programmatically and records them]
/// </summary>
/// <remarks>
/// 本番実行では常に無効であり、<see cref="MessageEx"/> の挙動は従来と同一である。
/// 有効化はUATハーネスからの明示的な <see cref="Enable"/> 呼び出しだけで起こる。
/// </remarks>
public static class MessageExTestRoute {
	/// <summary>ダイアログ要求。<paramref name="Kind"/> は MessageEx のメソッド名。</summary>
	public sealed record Request(
		string Kind,
		MessageBoxButton Button,
		MessageBoxImage Image,
		string Message,
		string AppendedMessage,
		bool IsModal);

	/// <summary>ダイアログ要求と、テストルートが返した応答。</summary>
	public sealed record Record(Request Request, MessageBoxResult Result);

	private static readonly object _sync = new();
	private static readonly List<Record> _records = [];
	private static Func<Request, MessageBoxResult>? _responder;

	/// <summary>テストルートが有効か。<see cref="MessageEx"/> はこの値で分岐する。</summary>
	public static bool IsActive {
		get { lock (_sync) return _responder != null; }
	}

	/// <summary>
	/// テストルートを有効にする。
	/// </summary>
	/// <param name="responder">
	/// 応答を決める関数。<see langword="null"/> のときは <see cref="DefaultResponder"/>（YesNoはYes、他はOK）を使う。
	/// </param>
	/// <param name="clearRecords">これまでの記録を消すか。既定は消す。</param>
	public static void Enable(Func<Request, MessageBoxResult>? responder = null, bool clearRecords = true) {
		lock (_sync) {
			_responder = responder ?? DefaultResponder;
			if (clearRecords) _records.Clear();
		}
	}

	/// <summary>テストルートを無効にして通常のダイアログ表示へ戻す。記録は残す。</summary>
	public static void Disable() {
		lock (_sync) _responder = null;
	}

	/// <summary>既定応答。YesNo系はYes、それ以外はOKを返す。</summary>
	public static MessageBoxResult DefaultResponder(Request request) => request.Button switch {
		MessageBoxButton.YesNo => MessageBoxResult.Yes,
		MessageBoxButton.YesNoCancel => MessageBoxResult.Yes,
		MessageBoxButton.OKCancel => MessageBoxResult.OK,
		_ => MessageBoxResult.OK,
	};

	/// <summary>記録されたダイアログの一覧。呼び出し順である。</summary>
	public static IReadOnlyList<Record> Records {
		get { lock (_sync) return [.. _records]; }
	}

	/// <summary>記録だけを消す。有効・無効の状態は変えない。</summary>
	public static void ClearRecords() {
		lock (_sync) _records.Clear();
	}

	/// <summary>
	/// <see cref="MessageEx"/> から呼ばれ、応答を決めて記録する。
	/// </summary>
	/// <remarks>
	/// <see cref="IsActive"/> が偽のあいだに呼ばれた場合も既定応答を返す（呼び出し側の分岐漏れで
	/// 実ダイアログが出るより、応答して記録するほうが安全なため）。
	/// </remarks>
	internal static MessageBoxResult Respond(
		string kind,
		MessageBoxButton button,
		MessageBoxImage image,
		string message,
		string appendedMessage,
		bool isModal) {
		var request = new Request(kind, button, image, message ?? string.Empty, appendedMessage ?? string.Empty, isModal);
		Func<Request, MessageBoxResult> responder;
		lock (_sync) {
			responder = _responder ?? DefaultResponder;
		}
		var result = responder(request);
		lock (_sync) {
			_records.Add(new Record(request, result));
		}
		return result;
	}
}
