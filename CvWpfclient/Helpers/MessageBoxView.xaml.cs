/*
 * MessageBoxWPF
 * Alternative MessageBox for WPF.
 * https://github.com/mikihiro-t/MessageBoxWPF/
 * Licence : MIT license
 * 開発メモ: MessageEx でメッセージボックスを表示する。
 * [Development Note: Display message boxes using MessageEx]
 *	ShowInformationDialog / ShowQuestionDialog / ShowWarningDialog / ShowErrorDialog
 *	class名,Font,mergin調整、ownerの扱い等を修正
*/
/*
# description
MessageBoxView と MessageEx は、所有者 Window・ボタン種別・表示色を指定できるアプリケーション用メッセージボックスを提供します。

# example
MessageEx.ShowInformation("保存しました。", owner: this);
 */
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace CvWpfclient.Helpers;

/// <summary>
/// 拡張メッセージボックスのView (MessageExを通して表示)
/// [View for the extended message box (displayed through MessageEx)]
/// </summary>
public partial class MessageBoxView : Window {
	#region Variables
	/// <summary>
	/// Message
	/// </summary>
	public string Message { get; set; } = " ";
	/// <summary>
	/// Appended Message in the Expander.
	/// </summary>
	public string AppendedMessage { get; set; } = "";
	/// <summary>
	/// true : Appended Message Exsits.
	/// </summary>
	public bool HasAppendedMessage { get; private set; } = false;
	/// <summary>
	/// The icon to display.
	/// </summary>
	public MessageBoxImage Image { get; set; } = MessageBoxImage.Information;
	/// <summary>
	/// Button or buttons to display.
	/// </summary>
	public MessageBoxButton Button { get; set; } = MessageBoxButton.OK;
	/// <summary>
	/// The value that specifies which message box button is clicked by the user.
	/// </summary>
	public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;
	/// <summary>
	/// The default result of the message box. If you specify something other than ButtonNone, focus on the button based on that DefaultResult. 
	/// example: For YesNo Button, When DefaultResult set MessageBoxResult.Yes, YesButton wiil be forcused.
	/// </summary>
	public MessageBoxResult DefaultResult { get; set; } = MessageBoxResult.None;
	/// <summary>
	/// Shadow Effect
	/// </summary>
	public bool IsEnabledEffect { get; set; } = false;
	#endregion
	//
	#region Caption Variables
	public string OKCaption { get; set; } = "OK";
	public string YesCaption { get; set; } = "Yes";
	public string NoCaption { get; set; } = "No";
	public string CancelCaption { get; set; } = "Cancel";
	#endregion
	//
	#region Color Variables
	/// <summary>
	/// Message Foreground
	/// </summary>
	public Brush Color { get; set; } = Brushes.Black;
	/// <summary>
	/// Border Background
	/// </summary>
	public Brush BackgroundColor { get; set; } = Brushes.White;
	#endregion
	//
	#region Initializer
	public MessageBoxView() {
		InitializeComponent();
		if (string.IsNullOrWhiteSpace(AppendedMessage))
			AppendExpand.Visibility = Visibility.Hidden;
	}
	public MessageBoxView(string message, Window? owner = null) {
		InitializeComponent();
		Message = message;
		if (string.IsNullOrWhiteSpace(AppendedMessage))
			AppendExpand.Visibility = Visibility.Hidden;
		if (owner is not null && PresentationSource.FromVisual(owner) is not null) Owner = owner;
	}
	public MessageBoxView(string message, string appendedMessage, Window? owner = null) {
		InitializeComponent();
		Message = message;
		AppendedMessage = appendedMessage;
		if (string.IsNullOrWhiteSpace(AppendedMessage))
			AppendExpand.Visibility = Visibility.Hidden;
		if (owner is not null && PresentationSource.FromVisual(owner) is not null) Owner = owner;
	}
	public MessageBoxView(string message, MessageBoxButton button, MessageBoxImage image, Window? owner = null) {
		InitializeComponent();
		Message = message;
		Button = button;
		Image = image;
		if (string.IsNullOrWhiteSpace(AppendedMessage))
			AppendExpand.Visibility = Visibility.Hidden;
		if (owner is not null && PresentationSource.FromVisual(owner) is not null) Owner = owner;
	}
	public MessageBoxView(string message, string appendedMessage, MessageBoxButton button, MessageBoxImage image, Window? owner = null) {
		InitializeComponent();
		Message = message;
		AppendedMessage = appendedMessage;
		if (string.IsNullOrWhiteSpace(AppendedMessage))
			AppendExpand.Visibility = Visibility.Hidden;
		Button = button;
		Image = image;
		if (owner is not null && PresentationSource.FromVisual(owner) is not null) Owner = owner;
	}
	public MessageBoxView(string message, MessageBoxButton button, MessageBoxImage image, Brush color, Window? owner = null) {
		InitializeComponent();
		Message = message;
		Button = button;
		Image = image;
		Color = color;
		if (string.IsNullOrWhiteSpace(AppendedMessage))
			AppendExpand.Visibility = Visibility.Hidden;
		if (owner is not null && PresentationSource.FromVisual(owner) is not null) Owner = owner;
	}
	public MessageBoxView(string message, string appendedMessage, MessageBoxButton button, MessageBoxImage image, Brush color, Window? owner = null) {
		InitializeComponent();
		Message = message;
		AppendedMessage = appendedMessage;
		if (string.IsNullOrWhiteSpace(AppendedMessage))
			AppendExpand.Visibility = Visibility.Hidden;
		Button = button;
		Image = image;
		Color = color;
		if (owner is not null && PresentationSource.FromVisual(owner) is not null) Owner = owner;
	}
	#endregion
	//
	private void Window_Loaded(object sender, RoutedEventArgs e) {
		DataContext = this;
		HasAppendedMessage = !string.IsNullOrEmpty(AppendedMessage);
		if (HasAppendedMessage)
			AppendExpand.IsExpanded = true;
		//
		//RichTextBox
		FlowDocument document = MessageRichTextBox.Document;
		document.PagePadding = new Thickness(0); //Paragraph spacing
		var range = new TextRange(document.ContentStart, document.ContentEnd);
		range.Text = Message;
		//
		SetupIconVisibility();
		SetupButton();
		isWaitCursor = ClientLib.IsCursorWait();
		if (isWaitCursor) {
			ClientLib.Cursor2Normal();
		}
	}
	private bool isWaitCursor = false;
	private void CopyMenuItem_Click(object sender, RoutedEventArgs e) {
		var ctrl = (MenuItem)sender;
		if (ctrl is not null) {
			var s = Message + (string.IsNullOrEmpty(AppendedMessage) ? "" : "\r\n" + AppendedMessage);
			Clipboard.SetText(s);
		}
	}
	protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) {
		base.OnMouseLeftButtonDown(e);
		if (e.OriginalSource is DependencyObject source && FindAncestor<TextBoxBase>(source) is null && FindAncestor<ButtonBase>(source) is null)
			DragMove();
	}

	private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject {
		while (current is not null) {
			if (current is T target)
				return target;

			current = current switch {
				FrameworkContentElement frameworkContentElement => frameworkContentElement.Parent,
				_ => VisualTreeHelper.GetParent(current)
			};
		}

		return null;
	}

	private void SetupIconVisibility() {
		switch (Image) {
			case MessageBoxImage.None:
				break;
			case MessageBoxImage.Error:  //Same : MessageBoxImage.Stop, Hand
				ErrorIcon.Visibility = Visibility.Visible;
				break;
			case MessageBoxImage.Question:
				QuestionIcon.Visibility = Visibility.Visible;
				break;
			case MessageBoxImage.Warning:  //Same : Exclamation
				WarningIcon.Visibility = Visibility.Visible;
				break;
			case MessageBoxImage.Information:  //Same : Asterisk
				InformationIcon.Visibility = Visibility.Visible;
				break;
			default:
				break;
		}
	}
	private void SetupButton() {
		switch (Button) {
			case MessageBoxButton.OK:
				LeftButton.Visibility = Visibility.Collapsed;
				MiddleButton.Visibility = Visibility.Collapsed;
				RightButton.Content = OKCaption;
				RightButton.Tag = MessageBoxResult.OK; //Set MessageBoxResult to Tag
				break;
			case MessageBoxButton.OKCancel:
				LeftButton.Visibility = Visibility.Collapsed;
				MiddleButton.Content = OKCaption;
				RightButton.Content = CancelCaption;
				MiddleButton.Tag = MessageBoxResult.OK;
				RightButton.Tag = MessageBoxResult.Cancel;
				break;
			case MessageBoxButton.YesNoCancel:
				LeftButton.Content = YesCaption;
				MiddleButton.Content = NoCaption;
				RightButton.Content = CancelCaption;
				LeftButton.Tag = MessageBoxResult.Yes;
				MiddleButton.Tag = MessageBoxResult.No;
				RightButton.Tag = MessageBoxResult.Cancel;
				break;
			case MessageBoxButton.YesNo:
				LeftButton.Visibility = Visibility.Collapsed;
				MiddleButton.Content = YesCaption;
				RightButton.Content = NoCaption;
				MiddleButton.Tag = MessageBoxResult.Yes;
				RightButton.Tag = MessageBoxResult.No;
				break;
			default:
				break;
		}

		// DefaultResultが指定されていれば、そのボタンへフォーカスする。
		// 指定が無い(既定のNone)場合は従来どおり最も左側の表示されているボタンにフォーカスする
		Dispatcher.InvokeAsync(() => {
			var preferred = DefaultResult == MessageBoxResult.None
				? null
				: new[] { LeftButton, MiddleButton, RightButton }
					.FirstOrDefault(b => CanReceiveInitialFocus(b) && b.Tag is MessageBoxResult tag && tag == DefaultResult);
			if (preferred != null) {
				Keyboard.Focus(preferred);
			}
			else if (CanReceiveInitialFocus(LeftButton)) {
				Keyboard.Focus(LeftButton);
			}
			else if (CanReceiveInitialFocus(MiddleButton)) {
				Keyboard.Focus(MiddleButton);
			}
			else if (CanReceiveInitialFocus(RightButton)) {
				Keyboard.Focus(RightButton);
			}
		}, System.Windows.Threading.DispatcherPriority.Loaded);
	}

	private static bool CanReceiveInitialFocus(Button button)
		=> button.Visibility == Visibility.Visible && button.IsVisible && button.IsEnabled && button.Focusable;

	/// <summary>
	/// Left, Middle, Right Button Click
	/// </summary>
	/// <param name="sender"></param>
	/// <param name="e"></param>
	private void Button_Click(object sender, RoutedEventArgs e) {
		var button = (Button)sender;
		Result = (MessageBoxResult)button.Tag; //Get MessageBoxResult from Tag
		if (isWaitCursor) {
			ClientLib.Cursor2Wait();
		}
		Close();
		if (Owner != null)
			Owner.Activate();
	}

	private void Window_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (e.Key == Key.Escape) {
			if (isWaitCursor) {
				ClientLib.Cursor2Wait();
			}
			Close();
			if (Owner != null)
				Owner.Activate();
		}
		if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) {
			var text = Message + (HasAppendedMessage ? "\r\n" + AppendedMessage : "");
			if (!string.IsNullOrEmpty(text)) {
				_ = TrySetClipboardTextAsync(text);
				e.Handled = true;
				return;
			}
		}
	}
	private static async Task<bool> TrySetClipboardTextAsync(string text, int retryCount = 5, int delayMs = 50) {
		for (int i = 0; i < retryCount; i++) {
			try {
				Clipboard.SetText(text);
				return true;
			}
			catch (COMException ex) when ((uint)ex.HResult == 0x800401D0) {
				await Task.Delay(delayMs);
			}
		}
		return false;
	}

}

/// <summary>
/// 拡張メッセージボックス ShowInformationDialog / ShowQuestionDialog / ShowWarningDialog / ShowErrorDialog
/// [Extended message box]
/// </summary>
public static class MessageEx {
	#region Information Dialog
	public static MessageBoxResult ShowInformationDialog(string message, string appendedMessage = "", Window? owner = null) {
		if (MessageExTestRoute.IsActive)
			return MessageExTestRoute.Respond(nameof(ShowInformationDialog), MessageBoxButton.OK, MessageBoxImage.Information, message, appendedMessage, isModal: true);
		if (owner != null)
			owner.Opacity = 0.7;
		var cls = new MessageBoxView(message, appendedMessage, MessageBoxButton.OK, MessageBoxImage.Information, owner);
		cls.ShowDialog();
		if (owner != null)
			owner.Opacity = 1;
		return cls.Result;
	}
	/// <summary>
	/// Information Show with th Appended Message
	/// </summary>
	/// <param name="message"></param>
	/// <param name="appendedMessage"></param>
	/// <returns></returns>
	public static MessageBoxResult ShowInformation(string message, string appendedMessage = "", Window? owner = null) {
		if (MessageExTestRoute.IsActive)
			return MessageExTestRoute.Respond(nameof(ShowInformation), MessageBoxButton.OK, MessageBoxImage.Information, message, appendedMessage, isModal: false);
		var cls = new MessageBoxView(message, appendedMessage, MessageBoxButton.OK, MessageBoxImage.Information, owner);
		cls.Show();
		return cls.Result;
	}
	#endregion
	//
	#region Question Dialog
	public static MessageBoxResult ShowQuestionDialog(string message, string appendedMessage = "", Window? owner = null, MessageBoxResult defaultResult = MessageBoxResult.None) {
		if (MessageExTestRoute.IsActive)
			return MessageExTestRoute.Respond(nameof(ShowQuestionDialog), MessageBoxButton.YesNo, MessageBoxImage.Question, message, appendedMessage, isModal: true);
		if (owner != null)
			owner.Opacity = 0.7;
		var cls = new MessageBoxView(message, appendedMessage, MessageBoxButton.YesNo, MessageBoxImage.Question, owner) {
			DefaultResult = defaultResult
		};
		cls.ShowDialog();
		if (owner != null)
			owner.Opacity = 1;
		return cls.Result;
	}
	#endregion
	//
	#region Warning Dialog
	public static MessageBoxResult ShowWarningDialog(string message, string appendedMessage = "", Window? owner = null) {
		if (MessageExTestRoute.IsActive)
			return MessageExTestRoute.Respond(nameof(ShowWarningDialog), MessageBoxButton.OK, MessageBoxImage.Warning, message, appendedMessage, isModal: true);
		if (owner != null)
			owner.Opacity = 0.7;
		var cls = new MessageBoxView(message, appendedMessage, MessageBoxButton.OK, MessageBoxImage.Warning, owner);
		cls.ShowDialog();
		if (owner != null)
			owner.Opacity = 1;
		return cls.Result;
	}
	#endregion
	//
	#region Error Dialog
	public static MessageBoxResult ShowErrorDialog(string message, string appendedMessage = "", Window? owner = null) {
		if (MessageExTestRoute.IsActive)
			return MessageExTestRoute.Respond(nameof(ShowErrorDialog), MessageBoxButton.OK, MessageBoxImage.Error, message, appendedMessage, isModal: true);
		if (owner != null)
			owner.Opacity = 0.7;
		var cls = new MessageBoxView(message, appendedMessage, MessageBoxButton.OK, MessageBoxImage.Error, owner);
		cls.ShowDialog();
		if (owner != null)
			owner.Opacity = 1;
		return cls.Result;
	}
	#endregion
	//
	#region General Dialog
	/// <summary>
	/// General Show
	/// </summary>
	/// <param name="message"></param>
	/// <param name="appendedMessage"></param>
	/// <param name="messageBoxButton"></param>
	/// <param name="messageBoxImage"></param>
	/// <param name="color"></param>
	/// <returns></returns>
	public static MessageBoxResult Show(string message, string appendedMessage, MessageBoxButton messageBoxButton, MessageBoxImage messageBoxImage, Brush color, Window? owner = null) {
		if (MessageExTestRoute.IsActive)
			return MessageExTestRoute.Respond(nameof(Show), messageBoxButton, messageBoxImage, message, appendedMessage, isModal: false);
		var cls = new MessageBoxView(message, appendedMessage, messageBoxButton, messageBoxImage, color);
		cls.Show();
		return cls.Result;
	}
	/// <summary>
	/// General Show Dialog
	/// </summary>
	/// <param name="message"></param>
	/// <param name="appendedMessage"></param>
	/// <param name="messageBoxButton"></param>
	/// <param name="messageBoxImage"></param>
	/// <param name="color"></param>
	/// <returns></returns>
	public static MessageBoxResult ShowDialog(string message, string appendedMessage, MessageBoxButton messageBoxButton, MessageBoxImage messageBoxImage, Brush color, Window? owner = null) {
		if (MessageExTestRoute.IsActive)
			return MessageExTestRoute.Respond(nameof(ShowDialog), messageBoxButton, messageBoxImage, message, appendedMessage, isModal: true);
		var cls = new MessageBoxView(message, appendedMessage, messageBoxButton, messageBoxImage, color, owner);
		cls.ShowDialog();
		return cls.Result;
	}
	#endregion
}
