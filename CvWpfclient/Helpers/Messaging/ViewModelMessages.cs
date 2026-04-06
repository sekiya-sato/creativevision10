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
