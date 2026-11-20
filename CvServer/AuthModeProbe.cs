using CvBase;
using Microsoft.AspNetCore.Authorization;
using System.Reflection;

namespace CvServer.Handlers;

/// <summary>
/// ビルドされたサーバが業務APIの呼出しにログインを要求するかを判定する。
/// UAT用の認証解除（Doc/spec/tools/change4uat_run.ps1）は [UatChangeable] 直後の
/// [Authorize] を [AllowAnonymous] へ書き換えて再ビルドするため、実際の属性を見て判定する。
/// </summary>
public static class AuthModeProbe {
	static readonly Lazy<bool> _authRequired = new(() => IsAuthRequired(typeof(AuthModeProbe).Assembly));

	/// <summary>
	/// このサーバの判定結果（起動後最初の呼出しで一度だけ計算する）
	/// </summary>
	public static bool AuthRequired => _authRequired.Value;

	/// <summary>
	/// [UatChangeable] の付いたメソッドに1つでも [Authorize] があれば true。
	/// 切替が一部だけの場合もログイン必須側に倒す。対象メソッドが無い場合も true とする。
	/// </summary>
	public static bool IsAuthRequired(Assembly assembly) {
		ArgumentNullException.ThrowIfNull(assembly);
		return IsAuthRequired(GetLoadableTypes(assembly));
	}

	/// <summary>
	/// 指定した型群で判定する（単体テスト用）
	/// </summary>
	public static bool IsAuthRequired(IEnumerable<Type> types) {
		ArgumentNullException.ThrowIfNull(types);
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
		var methods = types
			.SelectMany(t => t.GetMethods(flags))
			.Where(m => m.IsDefined(typeof(UatChangeableAttribute), false))
			.ToList();
		if (methods.Count == 0) {
			return true;
		}
		return methods.Any(m => m.IsDefined(typeof(AuthorizeAttribute), false));
	}

	static IEnumerable<Type> GetLoadableTypes(Assembly assembly) {
		try {
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex) {
			return ex.Types.OfType<Type>();
		}
	}
}
