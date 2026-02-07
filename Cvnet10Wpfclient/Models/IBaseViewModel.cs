namespace Cvnet10Wpfclient.Models;

public interface IBaseViewModel {
	public string? AddInfo { get; set; }
	public int InitParam { get; set; }
}


public record class DialogCloseMessage(bool DialogResult);