using CvPrints;

// qfm をローカルで実 PDF 描画する検証ハーネス（DB・サーバ不要）。
// 使い方: qfmprint <formPath> <dataDir>
//   formPath : 検証する .qfm の絶対パス
//   dataDir  : data.txt（Shift_JIS/cp932, 列順 = qfm の item1..itemN）を置いたフォルダ。
//              outfile.pdf がこのフォルダに出力される。
// 事前準備: 実行フォルダ（bin/Debug/net10.0）に refer/printdll/printstream.license を置く
//           （ライセンスが未登録だと FormWriter.submit() が失敗する）。

var formPath = args.Length > 0 ? args[0] : throw new ArgumentException("formPath required");
var dataDir = args.Length > 1 ? args[1] : throw new ArgumentException("dataDir required");

var svc = new PrintAdapter();

Console.WriteLine("=== CheckLicense ===");
var lics = await svc.CheckLicenseAsync();
if (lics.Count == 0) Console.WriteLine("(no products reported)");
foreach (var l in lics) Console.WriteLine($"  product={l.Product} status={l.Status}");

// CvServer/Services/PrintPdfService.cs:93-99 と同じフィールド構成
var ctx = new PrintContext {
	BasePath = string.Empty,
	FormPath = formPath,
	DataPath = Path.Combine(dataDir, "data.txt"),
	OutputDir = dataDir,
	OutputFileName = "outfile.pdf",
};

Console.WriteLine("=== ExecutePrint ===");
Console.WriteLine($"  Form={ctx.FormPath}");
Console.WriteLine($"  Data={ctx.DataPath}");
Console.WriteLine($"  Out ={Path.Combine(ctx.OutputDir, ctx.OutputFileName)}");

var r = await svc.ExecutePrintAsync(ctx);
Console.WriteLine($"  IsSuccess={r.IsSuccess}");
Console.WriteLine($"  Message  ={r.Message}");

var outPath = Path.Combine(dataDir, "outfile.pdf");
if (File.Exists(outPath)) {
	Console.WriteLine($"  PDF exists: {outPath} ({new FileInfo(outPath).Length} bytes)");
} else {
	Console.WriteLine($"  PDF NOT created: {outPath}");
}
