
namespace Cvnet10Base;
/// <summary>
/// テーブルコメント用 (カラムコメント は変更時に問題あるので使用しない)
/// [For table comments]
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class CommentAttribute : Attribute {
	/// <summary>
	/// 日本語コメント
	/// </summary>
	public string Content { get; }
	/// <summary>
	/// 英語コメント
	/// </summary>
	public string? ContentEn { get; }
	public CommentAttribute(string content, string? contentEn = null) {
		Content = content;
		ContentEn = contentEn;
	}
}

public enum ColumnType {
	/// <summary>
	/// 文字列型
	/// [String type]
	/// </summary>
	String = 1,
	/// <summary>
	/// 数値型
	/// [Numeric type]
	/// </summary>
	//Number = 2,
	/// <summary>
	/// 日付型
	/// [Date type]
	/// </summary>
	//Date = 3,
	/// <summary>
	/// バイナリ型
	/// [Binary type]
	/// </summary>
	//Binary = 4,
	/// <summary>
	/// ブール型
	/// [Boolean type]
	/// </summary>
	//Boolean = 5,
	/// <summary>
	/// オブジェクト型
	/// [Object type]
	/// </summary>
	//Object = 6,
	/// <summary>
	/// オブジェクト型
	/// [Object type]
	/// </summary>
	//Array = 7,
	/// <summary>
	/// オブジェクト型
	/// [Object type]
	/// </summary>
	Enum = 8,
	/// <summary>
	/// JSON型
	/// [JSON type]
	/// </summary>
	Json = 10,
	/// <summary>
	/// その他型
	/// [Other type]
	/// </summary>
	Other = 99
}

/// <summary>
/// カラムのSQL-DB上のサイズ指定(9999だとJSON型LONGTEXT 65535byte)
/// [Column size specification on SQL-DB (9999 corresponds to JSON type LONGTEXT 65535 bytes)]
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class ColumnSizeDmlAttribute : Attribute {
	public int Size { get; }
	public ColumnType ColType { get; }
	public ColumnSizeDmlAttribute(int size = 40, ColumnType ctype = ColumnType.String) {
		Size = size;
		ColType = ctype;
	}
	public ColumnSizeDmlAttribute(ColumnType ctype) {
		ColType = ctype;
	}
}

/// <summary>
/// テーブルに付随する情報の定義(Create Table文など)
/// [Definition of information related to the table (e.g., Create Table statements)]
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class TableDmlAttribute : Attribute {
	/// <summary>
	/// カスタムCreateSQL文
	/// [Custom CreateSQL statement]
	/// </summary>
	public string? CreateSql = null;
	/// <summary>
	/// 追加キー定義(キー名=列名1,列名2@キー名2=列名1,列名2)
	/// [Additional key definitions (key name=column name1,column name2@key name2=column name1,column name2)]
	/// </summary>
	public string? NonUnieqeKey = null;
	/// <summary>
	/// 追加キー定義(キー名=列名1,列名2@キー名2=列名1,列名2)
	/// [Additional key definitions (key name=column name1,column name2@key name2=column name1,column name2)]
	/// </summary>
	public string? UnieqeKey = null;
}
