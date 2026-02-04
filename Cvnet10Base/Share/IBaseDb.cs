
namespace Cvnet10Base.Share;

public interface IBaseCodeName {
	public string Code { get; set; }
	public string? Name { get; set; }
	public string? Ryaku { get; set; }
	public string? Kana { get; set; }
}

public interface IBaseGetViewDefinition {
	public string GetViewDefinition();
}
public interface IBaseSerializeMeisho {
	public bool Ser { get; set; }

}

