using Cvnet10Base.Share;
using NLog.LayoutRenderers;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Cvnet10Base.Share;


public static class Resolver {
	/// <summary>
	/// 指定された型から ViewSql 文字列を取得します。
	/// 型が IBaseViewDefine を実装しており、ViewSql フィールドを持つ必要があります。
	/// </summary>
	public static string GetViewSql<T>() where T : IBaseViewDefine {
		return GetViewSql(typeof(T));
	}

	/// <summary>
	/// Type情報から ViewSql 文字列を動的に取得します。
	/// </summary>
	public static string GetViewSql(Type type) {
		// 1. インターフェースを実装しているかチェック
		if (!typeof(IBaseViewDefine).IsAssignableFrom(type)) {
			return string.Empty;
			// throw new ArgumentException($"{type.Name} は IBaseViewDefine を実装していません。");
		}
		
		// 2. staticフィールド "ViewSql" を検索
		// public かつ static なフィールドを対象とする
		var field = type.GetField("ViewSql", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

		if (field != null) {
			return field.GetValue(null)?.ToString() ?? string.Empty;
		}

		// 3. フィールドがない場合、staticプロパティも一応探す（拡張性のため）
		var prop = type.GetProperty("ViewSql", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
		if (prop != null) {
			return prop.GetValue(null)?.ToString() ?? string.Empty;
		}

		return string.Empty;
		// throw new InvalidOperationException($"{type.Name} に public static string ViewSql が定義されていません。");
	}
}