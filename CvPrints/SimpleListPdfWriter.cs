#if !PRINT_ENABLE
using CvAsset;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace CvPrints;

/// <summary>
/// PrintStream を使わない構成（PrintEnable=false）で、data.txt をそのまま確認用の一覧 PDF（A4横）にする。
/// <para>qfm のレイアウトは解釈しない。1件を列幅に応じて最大3段へ折り返し、上部に出力日時・帳票名・ページ数を出す。</para>
/// <para>列名は data.txt と同じフォルダの columns.txt（CSV 1行、UTF-8）があれば使い、なければ item1… とする。</para>
/// </summary>
internal static class SimpleListPdfWriter {
	/// <summary>列名ファイル。CvServer の印刷前処理が SQL 経路でだけ出力する</summary>
	public const string ColumnsFileName = "columns.txt";

	private const string FontFamily = "IPAGothic";
	private const string FontResourceName = "CvPrints.Fonts.ipag.ttf";
	// A4横（pt）と余白
	private const double PageWidth = 841.89;
	private const double PageHeight = 595.28;
	private const double Margin = 28.35; // 10mm
	// 列幅（半角文字数）の下限・上限と、セル左右の余白
	private const int MinColumnUnits = 4;
	private const int MaxColumnUnits = 40;
	private const double CellPadding = 2;
	// 1件の段数の上限。フォントを下限まで縮めても入らない場合だけ超える
	private const int MaxLines = 3;
	private const double MaxFontSize = 8;
	private const double MinFontSize = 6;
	private const double TitleFontSize = 9;

	private static readonly Lock fontLock = new();
	private static bool fontInitialized;

	private sealed record Column(string Name, int Units, bool IsNumber);
	private sealed record Cell(Column Column, int Index, double X, double Width);

	public static PrintResult Write(PrintContext context) {
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		var sjis = Encoding.GetEncoding("Shift_JIS");
		var rows = CsvText.Parse(File.ReadAllText(context.DataPath, sjis)).Select(x => x.Fields).ToList();
		var columnsPath = Path.Combine(Path.GetDirectoryName(context.DataPath) ?? string.Empty, ColumnsFileName);
		var names = File.Exists(columnsPath)
			? CsvText.Parse(File.ReadAllText(columnsPath, Encoding.UTF8)).FirstOrDefault()?.Fields ?? []
			: [];
		var columns = BuildColumns(rows, names);

		EnsureFont();
		var (fontSize, lines) = FitLayout(columns);
		var lineHeight = fontSize * 1.5;
		var recordHeight = lineHeight * lines.Count;
		var titleHeight = TitleFontSize * 2;
		var bodyTop = Margin + titleHeight + recordHeight + 2;
		var perPage = Math.Max(1, (int)((PageHeight - Margin - bodyTop) / recordHeight));
		var totalPages = Math.Max(1, (rows.Count + perPage - 1) / perPage);

		var font = new XFont(FontFamily, fontSize);
		var titleFont = new XFont(FontFamily, TitleFontSize);
		var title = Path.GetFileNameWithoutExtension(context.FormPath);
		var now = DateTime.Now.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
		var headerBrush = new XSolidBrush(XColor.FromArgb(221, 221, 221));
		var stripeBrush = new XSolidBrush(XColor.FromArgb(242, 242, 242));
		var linePen = new XPen(XColor.FromArgb(160, 160, 160), 0.3);
		var headerPen = new XPen(XColors.Black, 0.6);
		var contentWidth = PageWidth - Margin * 2;

		using var document = new PdfDocument();
		document.Info.Title = title;
		for (var pageNo = 1; pageNo <= totalPages; pageNo++) {
			var page = document.AddPage();
			page.Width = XUnitPt.FromPoint(PageWidth);
			page.Height = XUnitPt.FromPoint(PageHeight);
			using var gfx = XGraphics.FromPdfPage(page);

			// 上部: 出力日時・帳票名・ページ数
			var titleRect = new XRect(Margin, Margin, contentWidth, titleHeight);
			gfx.DrawString(now, titleFont, XBrushes.Black, titleRect, XStringFormats.CenterLeft);
			gfx.DrawString(title, titleFont, XBrushes.Black, titleRect, XStringFormats.Center);
			gfx.DrawString($"{pageNo}P/{totalPages}", titleFont, XBrushes.Black, titleRect, XStringFormats.CenterRight);

			// 列名（データと同じ段構成）
			var headerTop = Margin + titleHeight;
			gfx.DrawRectangle(headerBrush, new XRect(Margin, headerTop, contentWidth, recordHeight));
			DrawRecord(gfx, font, lines, headerTop, lineHeight, cell => cell.Column.Name);
			gfx.DrawLine(headerPen, Margin, headerTop + recordHeight, Margin + contentWidth, headerTop + recordHeight);

			// 明細: 1件ごとに区切り線、1件おきに網掛け
			var start = (pageNo - 1) * perPage;
			var end = Math.Min(rows.Count, start + perPage);
			for (var i = start; i < end; i++) {
				var top = bodyTop + (i - start) * recordHeight;
				if (i % 2 == 1) {
					gfx.DrawRectangle(stripeBrush, new XRect(Margin, top, contentWidth, recordHeight));
				}
				var fields = rows[i];
				DrawRecord(gfx, font, lines, top, lineHeight, cell => cell.Index < fields.Count ? fields[cell.Index] : string.Empty);
				gfx.DrawLine(linePen, Margin, top + recordHeight, Margin + contentWidth, top + recordHeight);
			}
		}

		var outputPath = Path.Combine(context.OutputDir, context.OutputFileName);
		document.Save(outputPath);
		return new PrintResult(true, context.OutputFileName);
	}

	private static List<Column> BuildColumns(List<List<string>> rows, List<string> names) {
		var count = Math.Max(names.Count, rows.Count == 0 ? 0 : rows.Max(x => x.Count));
		List<Column> columns = [];
		for (var i = 0; i < count; i++) {
			var name = i < names.Count && !string.IsNullOrWhiteSpace(names[i]) ? names[i] : $"item{i + 1}";
			var values = rows.Select(x => i < x.Count ? Normalize(x[i]) : string.Empty).ToList();
			var units = Math.Max(DisplayUnits(name), values.Count == 0 ? 0 : values.Max(DisplayUnits));
			var nonEmpty = values.Where(x => x.Length > 0).ToList();
			var isNumber = nonEmpty.Count > 0
				&& nonEmpty.All(x => decimal.TryParse(x, NumberStyles.Number, CultureInfo.InvariantCulture, out _));
			columns.Add(new Column(name, Math.Clamp(units, MinColumnUnits, MaxColumnUnits), isNumber));
		}
		return columns;
	}

	/// <summary>
	/// 列を左から1段の幅まで詰め、入りきらない列を次の段へ送る。
	/// 最大段数に収まるまでフォントを縮め、下限でも収まらなければ段数の上限を超えて配置する。
	/// </summary>
	private static (double FontSize, List<List<Cell>> Lines) FitLayout(List<Column> columns) {
		var width = PageWidth - Margin * 2;
		List<List<Cell>> lines = [];
		for (var size = MaxFontSize; size >= MinFontSize; size -= 0.5) {
			lines = Pack(columns, size, width);
			if (lines.Count <= MaxLines) {
				return (size, lines);
			}
		}
		return (MinFontSize, lines);
	}

	private static List<List<Cell>> Pack(List<Column> columns, double fontSize, double width) {
		// IPAゴシックは等幅のため、半角1文字 = フォントサイズの半分
		var unitWidth = fontSize / 2;
		List<List<Cell>> lines = [[]];
		var x = 0.0;
		for (var i = 0; i < columns.Count; i++) {
			var cellWidth = Math.Min(columns[i].Units * unitWidth + CellPadding * 2, width);
			if (x > 0 && x + cellWidth > width) {
				lines.Add([]);
				x = 0;
			}
			lines[^1].Add(new Cell(columns[i], i, x, cellWidth));
			x += cellWidth;
		}
		return lines;
	}

	private static void DrawRecord(XGraphics gfx, XFont font, List<List<Cell>> lines, double top, double lineHeight,
		Func<Cell, string> textOf) {
		var unitWidth = font.Size / 2;
		for (var line = 0; line < lines.Count; line++) {
			foreach (var cell in lines[line]) {
				var maxUnits = (int)((cell.Width - CellPadding * 2) / unitWidth);
				var text = Truncate(Normalize(textOf(cell)), maxUnits);
				var rect = new XRect(Margin + cell.X + CellPadding, top + line * lineHeight, cell.Width - CellPadding * 2, lineHeight);
				// 列名も値と同じ寄せにして、列の対応を見やすくする
				var format = cell.Column.IsNumber ? XStringFormats.CenterRight : XStringFormats.CenterLeft;
				gfx.DrawString(text, font, XBrushes.Black, rect, format);
			}
		}
	}

	private static string Normalize(string value) =>
		value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

	/// <summary>表示幅（半角=1、全角=2）が上限を超える場合は末尾を「…」にする</summary>
	private static string Truncate(string value, int maxUnits) {
		if (DisplayUnits(value) <= maxUnits) {
			return value;
		}
		var sb = new StringBuilder();
		var units = 0;
		foreach (var rune in value.EnumerateRunes()) {
			var w = RuneUnits(rune);
			if (units + w > maxUnits - 2) {
				break;
			}
			sb.Append(rune.ToString());
			units += w;
		}
		return sb.Append('…').ToString();
	}

	private static int DisplayUnits(string value) {
		var units = 0;
		foreach (var rune in value.EnumerateRunes()) {
			units += RuneUnits(rune);
		}
		return units;
	}

	// ASCII と半角カナだけを半角扱いにする
	private static int RuneUnits(Rune rune) =>
		rune.Value < 0x80 || (rune.Value >= 0xFF61 && rune.Value <= 0xFF9F) ? 1 : 2;

	/// <summary>同梱の IPAゴシックをフォント解決に登録する（PDFsharp の設定はプロセスで1回だけ）</summary>
	private static void EnsureFont() {
		lock (fontLock) {
			if (fontInitialized) {
				return;
			}
			if (GlobalFontSettings.FontResolver is null) {
				GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
			}
			fontInitialized = true;
		}
	}

	private sealed class EmbeddedFontResolver : IFontResolver {
		private static readonly Lazy<byte[]> fontData = new(() => {
			using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FontResourceName)
				?? throw new InvalidOperationException($"フォントリソースがありません: {FontResourceName}");
			using var ms = new MemoryStream();
			stream.CopyTo(ms);
			return ms.ToArray();
		});

		// 書体名に関係なく同梱フォントを返す（太字・斜体は使わない）
		public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) => new(FontFamily);

		public byte[]? GetFont(string faceName) => fontData.Value;
	}
}
#endif
