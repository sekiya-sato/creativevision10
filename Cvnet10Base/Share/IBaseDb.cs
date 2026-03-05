
namespace Cvnet10Base.Share;

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
/// テーブルに別途リスト取得用の ViewSql 文字列を定義するためのインターフェース(T.*)
/// </summary>
public interface IBaseViewDefine {

	readonly static public string ViewSql = "";
}
/// <summary>
/// シリアライズ制御用のインターフェース
/// </summary>
public interface IBaseSerializeMeisho {
	public bool Ser { get; set; }

}

