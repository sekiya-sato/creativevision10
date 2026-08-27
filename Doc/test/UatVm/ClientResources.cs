using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Xml.Linq;

namespace UatVm;

/// <summary>
/// CvWpfclientのApp.xamlと同じApplicationリソースを、ハーネス側で組み立てる。
/// </summary>
/// <remarks>
/// <para>
/// なぜ `App.InitializeComponent()` を使わないか。App.xamlは`/Resources/UIColors.xaml`のように
/// **アセンブリ名なしの絶対パス形式**でリソースを参照する。この形式の解決先は
/// <see cref="Application.ResourceAssembly"/> で決まるが、これはWPF側の初期化時点で
/// エントリアセンブリ（＝このハーネス）に確定してしまい、後から変更できない
/// （`ModuleInitializer` で最初に代入しても「設定後に変更することはできません」となる）。
/// そのため App.xaml をそのまま読むと `リソース 'resources/uicolors.xaml' を検索できません` で失敗する。
/// </para>
/// <para>
/// 対策として、App.xamlを実行時に解析し、Sourceを`pack://application:,,,/CreativeVision10;component/...`
/// へアセンブリ修飾して読み込む。定義の二重管理を避けるため、リストはハードコードせず
/// 常にApp.xamlから読む。App.xamlへ辞書やコンバータを追加しても追従する。
/// </para>
/// <para>
/// 各`Resources/*.xaml`は入れ子のMergedDictionariesを持たないため、修飾は最上位だけで足りる。
/// </para>
/// </remarks>
public static class ClientResources {
	private const string ClientAssemblyName = "CreativeVision10";
	private static readonly XNamespace _presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
	private static readonly XNamespace _xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
	private const string MaterialDesignNamespace = "http://materialdesigninxaml.net/winfx/xaml/themes";

	/// <summary>組み立て結果の内訳。証跡へ残して、App.xamlとの乖離を検知できるようにする。</summary>
	public sealed record Summary(int Dictionaries, int Objects, List<string> Sources, List<string> Keys, List<string> Skipped);

	/// <summary>
	/// App.xamlを解析して <paramref name="app"/> のResourcesを構築する。
	/// </summary>
	/// <param name="app">対象Application。</param>
	/// <param name="appXamlPath">CvWpfclient/App.xaml のパス。</param>
	public static Summary Load(Application app, string appXamlPath) {
		ArgumentNullException.ThrowIfNull(app);
		if (!File.Exists(appXamlPath)) throw new FileNotFoundException("App.xaml が見つかりません。", appXamlPath);

		var root = XDocument.Load(appXamlPath).Root
			?? throw new InvalidOperationException("App.xaml のルート要素が読めません。");
		var dictionaryElement = root.Element(_presentation + "Application.Resources")?.Element(_presentation + "ResourceDictionary")
			?? throw new InvalidOperationException("App.xaml に Application.Resources/ResourceDictionary がありません。");

		var sources = new List<string>();
		var keys = new List<string>();
		var skipped = new List<string>();
		var target = app.Resources;

		foreach (var element in dictionaryElement.Elements()) {
			if (element.Name == _presentation + "ResourceDictionary.MergedDictionaries") {
				foreach (var merged in element.Elements()) {
					var dictionary = CreateMergedDictionary(merged, sources, skipped);
					if (dictionary != null) target.MergedDictionaries.Add(dictionary);
				}
				continue;
			}

			// x:Key を持つ単体リソース（コンバータ類）。
			var key = element.Attribute(_xaml + "Key")?.Value;
			if (key == null) {
				skipped.Add($"{element.Name.LocalName}(x:Key なし)");
				continue;
			}
			var type = ResolveType(element.Name);
			if (type == null) {
				skipped.Add($"{element.Name.LocalName}(型解決不可)");
				continue;
			}
			var instance = Activator.CreateInstance(type)
				?? throw new InvalidOperationException($"{type.FullName} を生成できません。");
			ApplyAttributes(element, instance);
			target[key] = instance;
			keys.Add(key);
		}

		return new Summary(target.MergedDictionaries.Count, keys.Count, sources, keys, skipped);
	}

	/// <summary>MergedDictionaries配下の1要素を読み込む。</summary>
	private static ResourceDictionary? CreateMergedDictionary(XElement element, List<string> sources, List<string> skipped) {
		if (element.Name == _presentation + "ResourceDictionary") {
			var source = element.Attribute("Source")?.Value;
			if (string.IsNullOrEmpty(source)) {
				skipped.Add("ResourceDictionary(Source なし)");
				return null;
			}
			var uri = QualifySource(source);
			sources.Add(uri.ToString());
			return new ResourceDictionary { Source = uri };
		}

		// BundledTheme のような、辞書として振る舞う独自要素。
		var type = ResolveType(element.Name);
		if (type == null || !typeof(ResourceDictionary).IsAssignableFrom(type)) {
			skipped.Add($"{element.Name.LocalName}(辞書として生成不可)");
			return null;
		}
		var dictionary = (ResourceDictionary)(Activator.CreateInstance(type)
			?? throw new InvalidOperationException($"{type.FullName} を生成できません。"));

		// XAMLと同じ初期化順にする。BeginInit/EndInit の間に属性を設定しないと中身が構築されない。
		dictionary.BeginInit();
		ApplyAttributes(element, dictionary);
		dictionary.EndInit();
		sources.Add($"{type.FullName}({DescribeAttributes(element)})");
		return dictionary;
	}

	/// <summary>
	/// `/Resources/UIColors.xaml` のようなアセンブリ名なしのSourceを、CvWpfclient修飾の絶対URIへ変換する。
	/// </summary>
	private static Uri QualifySource(string source) {
		if (source.StartsWith("pack://", StringComparison.OrdinalIgnoreCase)) {
			return new Uri(source, UriKind.Absolute);
		}
		var path = source.StartsWith('/') ? source : "/" + source;
		return new Uri($"pack://application:,,,/{ClientAssemblyName};component{path}", UriKind.Absolute);
	}

	/// <summary>XAMLの属性をプロパティへ反映する（x:Key と Source は除く）。</summary>
	private static void ApplyAttributes(XElement element, object instance) {
		foreach (var attribute in element.Attributes()) {
			if (attribute.IsNamespaceDeclaration) continue;
			if (attribute.Name == _xaml + "Key" || attribute.Name.LocalName == "Source") continue;

			var property = instance.GetType().GetProperty(attribute.Name.LocalName,
				BindingFlags.Public | BindingFlags.Instance);
			if (property?.CanWrite != true) continue;

			var value = ConvertValue(property.PropertyType, attribute.Value);
			if (value != null) property.SetValue(instance, value);
		}
	}

	private static object? ConvertValue(Type type, string text) {
		var actual = Nullable.GetUnderlyingType(type) ?? type;
		if (actual.IsEnum) return Enum.Parse(actual, text, ignoreCase: true);
		if (actual == typeof(string)) return text;
		var converter = TypeDescriptor.GetConverter(actual);
		return converter.CanConvertFrom(typeof(string)) ? converter.ConvertFromInvariantString(text) : null;
	}

	private static string DescribeAttributes(XElement element) =>
		string.Join(",", element.Attributes()
			.Where(x => !x.IsNamespaceDeclaration)
			.Select(x => $"{x.Name.LocalName}={x.Value}"));

	/// <summary>XAMLの要素名からCLR型を解決する。</summary>
	private static Type? ResolveType(XName name) {
		var ns = name.NamespaceName;

		if (ns.StartsWith("clr-namespace:", StringComparison.Ordinal)) {
			// 例: clr-namespace:CvWpfclient.Helpers（assembly省略時はCvWpfclient）
			var body = ns["clr-namespace:".Length..];
			var parts = body.Split(';', StringSplitOptions.TrimEntries);
			var clrNamespace = parts[0];
			var assemblyName = parts.Skip(1)
				.FirstOrDefault(x => x.StartsWith("assembly=", StringComparison.Ordinal))?["assembly=".Length..]
				?? ClientAssemblyName;
			return Assembly.Load(assemblyName).GetType($"{clrNamespace}.{name.LocalName}");
		}

		if (ns == MaterialDesignNamespace) {
			return Assembly.Load("MaterialDesignThemes.Wpf").GetType($"MaterialDesignThemes.Wpf.{name.LocalName}");
		}

		if (ns == _presentation.NamespaceName) {
			// 標準の presentation 名前空間。WPFの代表的な名前空間から探す。
			var wpf = typeof(Application).Assembly;
			foreach (var candidate in new[] { "System.Windows.Controls", "System.Windows.Data", "System.Windows" }) {
				var type = wpf.GetType($"{candidate}.{name.LocalName}");
				if (type != null) return type;
			}
		}

		return null;
	}
}
