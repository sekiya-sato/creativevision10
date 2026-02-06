
namespace Cvnet10Base.Share;

public interface IBaseCodeName {
	public string Code { get; set; }
	public string? Name { get; set; }
	public string? Ryaku { get; set; }
	public string? Kana { get; set; }
}

/// <summary>
/// テーブルに別途リスト取得用の ViewSql 文字列を定義するためのインターフェース(T.*)
/// </summary>
public interface IBaseViewDefine {

	readonly static public string ViewSql="";
}
public interface IBaseSerializeMeisho {
	public bool Ser { get; set; }

}

