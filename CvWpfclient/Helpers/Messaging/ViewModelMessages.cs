/*
# description
ViewModelMessages は CommunityToolkit.Mvvm.Messaging で選択した数値または文字列を ViewModel 間で通知するメッセージ型を定義します。

# example
WeakReferenceMessenger.Default.Send(new SelectItemMessage(selectedId));
 */
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace CvWpfclient.Helpers;



public sealed class SelectItemMessage : ValueChangedMessage<long> {
	public SelectItemMessage(long value) : base(value) {
	}
}

public sealed class SelectStringMessage : ValueChangedMessage<string> {
	public SelectStringMessage(string value) : base(value) {
	}
}
