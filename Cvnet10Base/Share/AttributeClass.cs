
namespace Cvnet10Base;
/// <summary>
/// テーブルコメント用 (カラムコメント は変更時に問題あるので使用しない)
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class CommentAttribute : Attribute {
	public string Content { get; } =string.Empty;
	public CommentAttribute(string content) {
		Content = content;
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
	const int DefaultSize = 40;
	public int Size { get; }
	public ColumnType ColType { get; }
	public ColumnSizeDmlAttribute(int size = DefaultSize, ColumnType ctype = ColumnType.String) {
		Size = size;
		ColType = ctype;
	}
	public ColumnSizeDmlAttribute(ColumnType ctype) {
		ColType = ctype;
	}
}
/// <summary>
/// 実テーブル不要
/// </summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = false)] 
public sealed class NoCreateAttribute : Attribute {
}

/// <summary>
/// データベースキー定義
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)] // Multipleをtrueに
public sealed class KeyDmlAttribute : Attribute {
	/// <summary>
	/// キー名
	/// </summary>
	public string KeyName { get; }
	/// <summary>
	/// カラムリスト
	/// </summary>
	public string[] ColNames { get; }
	/// <summary>
	/// ユニークキー?
	/// </summary>
	public bool IsUnique { get; }

	public KeyDmlAttribute(string keyName, bool isUnique, params string[] colNames) {
		KeyName = keyName;
		IsUnique = isUnique;
		ColNames = colNames;
	}
}

