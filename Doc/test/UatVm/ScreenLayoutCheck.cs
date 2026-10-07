using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UatVm;

/// <summary>
/// 実Viewの画面画像(JPG)保存と、表示崩れ（ボタン・入力欄のはみ出し、文字切れ）の自動判定。
/// <para>
/// 判定は目視の代わりではなく、目視で見落としやすい「レイアウト枠からのはみ出し(LayoutClip)」
/// 「ウィンドウ外への配置」「文字幅が表示幅を超える」を機械的に拾うためのもの。
/// DataGridのデータセルは列幅で切れることが前提なので、見出し・ボタン・ラベルと分けて報告する。
/// </para>
/// </summary>
public static class ScreenLayoutCheck {
	/// <summary>表示崩れの1件</summary>
	/// <summary>長い名称のセル文字切れ。列幅の都合で切れうるため不合格にはせず、記録だけする</summary>
	public const string LongCellKind = "セル文字切れ(長い名称)";
	const int LongTextLength = 12;

	public sealed record Issue(string Kind, string Element, string Text, double Left, double Top, double Width, double Height, double Need);

	/// <summary>Viewを描画してJPGへ保存し、保存先パスを返す。透明部分は白で塗る。</summary>
	public static string SaveJpeg(Window view, string directory, string name) {
		view.UpdateLayout();
		var dpi = VisualTreeHelper.GetDpi(view);
		int width = Math.Max(1, (int)Math.Ceiling(view.ActualWidth * dpi.DpiScaleX));
		int height = Math.Max(1, (int)Math.Ceiling(view.ActualHeight * dpi.DpiScaleY));
		var bitmap = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
		bitmap.Render(view);
		var visual = new DrawingVisual();
		using (var dc = visual.RenderOpen()) {
			var rect = new Rect(0, 0, view.ActualWidth, view.ActualHeight);
			dc.DrawRectangle(Brushes.White, null, rect);
			dc.DrawImage(bitmap, rect);
		}
		var opaque = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
		opaque.Render(visual);
		var path = Path.Combine(directory, $"{name}.jpg");
		using var stream = File.Create(path);
		var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
		encoder.Frames.Add(BitmapFrame.Create(opaque));
		encoder.Save(stream);
		return path;
	}

	/// <summary>
	/// 画面内の表示崩れを列挙する。
	/// <list type="bullet">
	/// <item>Button / TextBox / ComboBox / CheckBox / DatePicker がウィンドウ外へはみ出す、またはレイアウト枠で切られている</item>
	/// <item>折り返さない TextBlock の文字幅が表示幅を超える（ラベル・ボタン文字・列見出し・データセル）</item>
	/// <item>TextBox の入力値の文字幅が表示幅を超える</item>
	/// </list>
	/// </summary>
	public static IReadOnlyList<Issue> Inspect(Window view) {
		view.UpdateLayout();
		var issues = new List<Issue>();
		if (view.Content is not FrameworkElement root) return issues;
		var area = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
		var pixelsPerDip = VisualTreeHelper.GetDpi(view).PixelsPerDip;
		foreach (var element in Descendants(root).OfType<FrameworkElement>()) {
			if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0) continue;
			// スクロールバー部品と DataGrid の余白用見出しは表示内容ではないので対象外
			if (element is ScrollBar || FindAncestor<ScrollBar>(element) != null || element.Name == "PART_FillerColumnHeader") continue;
			Rect bounds;
			try {
				bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
			}
			catch (InvalidOperationException) {
				continue;
			}
			// 仮想化・スクロールで見えていない行は対象外
			if (!bounds.IntersectsWith(area) || IsScrolledOut(element, root)) continue;

			if (element is DataGridColumnHeader && IsOutside(bounds, area)) {
				issues.Add(Make("列が横スクロール外", element, bounds, 0));
				continue;
			}
			if (element is ButtonBase or TextBox or ComboBox or DatePicker) {
				if (element is not CheckBox && IsOutside(bounds, area)) {
					issues.Add(Make("ウィンドウ外", element, bounds, 0));
				}
				else if (IsLayoutClipped(element)) {
					issues.Add(Make("枠で切れ", element, bounds, 0));
				}
			}
			if (element is TextBlock tb && !string.IsNullOrEmpty(tb.Text) && tb.TextWrapping == TextWrapping.NoWrap) {
				var need = MeasureText(tb, tb.Text, tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch, tb.FontSize, pixelsPerDip)
					+ tb.Padding.Left + tb.Padding.Right;
				var available = Math.Min(tb.ActualWidth, VisibleWidth(tb, root, bounds));
				// セル内の TextBlock は文字幅のまま配置され、セルの枠(LayoutClip)で切られるので両方を見る
				if (need > available + 1.0 || (IsLayoutClipped(tb) && VisibleWidth(tb, root, bounds) != double.MaxValue)) {
					issues.Add(Make(Category(tb), tb, bounds, need));
				}
			}
			if (element is TextBox box && !string.IsNullOrEmpty(box.Text) && box.TextWrapping == TextWrapping.NoWrap) {
				var need = MeasureText(box, box.Text, box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch, box.FontSize, pixelsPerDip)
					+ box.Padding.Left + box.Padding.Right + box.BorderThickness.Left + box.BorderThickness.Right + 4;
				if (need > box.ActualWidth + 1.0) {
					issues.Add(Make("入力値切れ", box, bounds, need));
				}
			}
		}
		return issues;
	}

	static Issue Make(string kind, FrameworkElement element, Rect bounds, double need) => new(
		kind, Describe(element), TextOf(element), Math.Round(bounds.Left), Math.Round(bounds.Top),
		Math.Round(bounds.Width), Math.Round(bounds.Height), Math.Round(need));

	/// <summary>文字切れの種類。データセルは列幅次第なので見出し・ラベルと分ける</summary>
	static string Category(TextBlock tb) {
		if (FindAncestor<DataGridColumnHeader>(tb) != null) return "列見出し切れ";
		// 名称（コード＋名前）のような長い可変文字は列幅で切れうるので、短い固定書式（日付・区分・数量）と分ける
		if (FindAncestor<DataGridCell>(tb) != null) return tb.Text.Length > LongTextLength ? LongCellKind : "セル文字切れ";
		if (FindAncestor<ButtonBase>(tb) != null) return "ボタン文字切れ";
		return "ラベル文字切れ";
	}

	static string Describe(FrameworkElement element) {
		var name = string.IsNullOrEmpty(element.Name) ? string.Empty : $"#{element.Name}";
		var owner = FindAncestor<ButtonBase>(element) is { } button && !ReferenceEquals(button, element) ? " in Button" : string.Empty;
		return $"{element.GetType().Name}{name}{owner}";
	}

	static string TextOf(FrameworkElement element) => element switch {
		TextBlock tb => tb.Text,
		TextBox box => box.Text,
		ContentControl cc => cc.Content switch {
			string s => s,
			FrameworkElement fe => string.Join(" ", Descendants(fe).OfType<TextBlock>().Select(x => x.Text)),
			_ => cc.Content?.ToString() ?? string.Empty,
		},
		_ => string.Empty,
	};

	static bool IsOutside(Rect bounds, Rect area) =>
		bounds.Left < area.Left - 1 || bounds.Top < area.Top - 1 || bounds.Right > area.Right + 1 || bounds.Bottom > area.Bottom + 1;

	/// <summary>レイアウト枠より大きく配置されてWPFがクリップを掛けたか</summary>
	static bool IsLayoutClipped(FrameworkElement element) {
		if (LayoutInformation.GetLayoutClip(element) is not Geometry clip) return false;
		var b = clip.Bounds;
		return b.Width + 1 < element.ActualWidth || b.Height + 1 < element.ActualHeight;
	}

	/// <summary>ScrollViewer内で表示範囲外にある要素か（横スクロールで隠れたセル等）。入れ子のScrollViewerは外側まで順に見る</summary>
	static bool IsScrolledOut(FrameworkElement element, FrameworkElement root) {
		for (var presenter = FindAncestor<ScrollContentPresenter>(element); presenter != null; presenter = FindAncestor<ScrollContentPresenter>(presenter)) {
			var inPresenter = element.TransformToAncestor(presenter).TransformBounds(new Rect(element.RenderSize));
			if (!inPresenter.IntersectsWith(new Rect(presenter.RenderSize))) return true;
		}
		return false;
	}

	/// <summary>ScrollViewer内の要素は、見えている幅（表示領域との交差）で判定する</summary>
	static double VisibleWidth(FrameworkElement element, FrameworkElement root, Rect bounds) {
		var presenter = FindAncestor<ScrollContentPresenter>(element);
		if (presenter == null) return double.MaxValue;
		var inPresenter = element.TransformToAncestor(presenter).TransformBounds(new Rect(element.RenderSize));
		var visible = Rect.Intersect(inPresenter, new Rect(presenter.RenderSize));
		// 右端で一部だけ見えているセルは横スクロールで全体を見られるので切れ扱いしない
		return visible.IsEmpty || visible.Width + 1 < inPresenter.Width ? double.MaxValue : visible.Width;
	}

	/// <summary>要素と同じ文字描画方式（Display / Ideal）で文字幅を測る。方式が違うと数pxずれて誤検知になる</summary>
	static double MeasureText(DependencyObject owner, string text, FontFamily family, FontStyle style, FontWeight weight, FontStretch stretch, double size, double pixelsPerDip) {
		var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
			new Typeface(family, style, weight, stretch), size, Brushes.Black, null, TextOptions.GetTextFormattingMode(owner), pixelsPerDip);
		return formatted.WidthIncludingTrailingWhitespace;
	}

	static T? FindAncestor<T>(DependencyObject element) where T : DependencyObject {
		var current = VisualTreeHelper.GetParent(element);
		while (current != null) {
			if (current is T found) return found;
			current = VisualTreeHelper.GetParent(current);
		}
		return null;
	}

	static IEnumerable<DependencyObject> Descendants(DependencyObject parent) {
		int count = VisualTreeHelper.GetChildrenCount(parent);
		for (int i = 0; i < count; i++) {
			var child = VisualTreeHelper.GetChild(parent, i);
			yield return child;
			foreach (var nested in Descendants(child)) yield return nested;
		}
	}
}
