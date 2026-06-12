
namespace CvBase.Share;

/// <summary>
/// コード、名称、略称、カナを持つテーブル
/// </summary>
public interface IBaseCodeName {
	public string Code { get; set; }
	public string Name { get; set; }
	public string Ryaku { get; set; }
	public string Kana { get; set; }
}

/// <summary>
/// 派生テーブルの元テーブルを示すインターフェース
/// </summary>
public interface IDerivedOrigin {
	public Type DerivedClass { get; }
}
