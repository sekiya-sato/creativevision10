using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Cvnet10Wpfclient.Helpers;

public sealed record DialogCloseMessage(bool DialogResult);

public class ShortTextMessage : ValueChangedMessage<string> {
	public ShortTextMessage(string value) : base(value) {
	}
}

public sealed class ShortMsg : ShortTextMessage {
	public ShortMsg(string value) : base(value) {
	}
}

public class SelectItemMessage : ValueChangedMessage<long> {
	public SelectItemMessage(long value) : base(value) {
	}
}

public sealed class ChangeSelectMsg : SelectItemMessage {
	public ChangeSelectMsg(long value) : base(value) {
	}
}
