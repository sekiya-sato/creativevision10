using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using Microsoft.Data.Sqlite;

// 明細別消費税（軽減税率混在）の実DB検証ハーネス。
// 詳細設計: Doc/spec/2026-08-25_明細別消費税計算_詳細設計.md の 7章「テスト観点」
//
// 使い方:  taxmix <command> [dbPath]
//   inspect   MasterSysTax の定義と対象商品の Id_Tax を表示（投入前の前提確認）
//   mixed     軽減税率(8%)と標準税率(10%)と非課税を混在させた伝票で税額計算を突合
//   all       inspect → mixed
//   dbPath    省略時 C:\gitroot\new2022\cv10\CvServer\server-user163.db
//
// 前提: dbPath は開発用DB。実運用DBには使わない。

var command = args.Length > 0 ? args[0] : "inspect";
var dbPath = args.Length > 1 ? args[1] : @"C:\gitroot\new2022\cv10\CvServer\server-user163.db";

// 軽減税率(Id_Tax=2)が設定されている検証用商品
long[] ReducedShohinIds = [37522, 37524, 37715, 37835, 37845];

var cs = new SqliteConnectionStringBuilder {
	DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
}.ToString();
using var conn = new SqliteConnection(cs);
conn.Open();
var db = new ExDatabaseSqlite(conn) { KeepConnectionAlive = true };

int failed = 0;
switch (command) {
	case "inspect": Inspect(); break;
	case "mixed": failed = Mixed(); break;
	case "all": Inspect(); Console.WriteLine(); failed = Mixed(); break;
	default:
		Console.WriteLine($"未知のコマンド: {command}");
		return 1;
}
Console.WriteLine();
Console.WriteLine(failed == 0 ? "==> 全チェックPASS" : $"==> {failed} 件FAIL");
return failed == 0 ? 0 : 1;

void Inspect() {
	Console.WriteLine($"DB: {dbPath}");
	Console.WriteLine();

	var sysman = db.Fetch<MasterSysman>("where Id = 1").FirstOrDefault() ?? new MasterSysman();
	Console.WriteLine("== MasterSysTax（MasterSysman.Jsub）==");
	Console.WriteLine($"{"Id",3} {"TaxRate",8} {"DateFrom",10} {"TaxNewRate",11}");
	foreach (var t in (sysman.Jsub ?? []).OrderBy(x => x.Id)) {
		Console.WriteLine($"{t.Id,3} {t.TaxRate,8} {t.DateFrom,10} {t.TaxNewRate,11}");
	}
	Console.WriteLine();

	// 実際に解決される税率（サーバ側ヘルパーとクライアント側 LogicGetTax の両方の式）
	var denDay = DateTime.Now.ToString("yyyyMMdd");
	Console.WriteLine($"== 税率解決 (伝票日付 {denDay}) ==");
	Console.WriteLine($"{"Id_Tax",7} {"サーバ(TaxRateResolver)",24} {"クライアント(LogicGetTax式)",28}");
	foreach (var taxId in new long[] { 0, 1, 2, 3 }) {
		var server = TaxRateResolver.ResolveTaxRatePercent(sysman, taxId, denDay);
		var client = ClientLogicGetTaxEquivalent(sysman, taxId, denDay);
		var mark = server == client ? "" : "   ← 不一致";
		Console.WriteLine($"{taxId,7} {server,20}% {client,24}%{mark}");
	}
	Console.WriteLine();

	Console.WriteLine("== 検証用商品の Id_Tax ==");
	Console.WriteLine($"{"Id",7} {"Code",14} {"Id_Tax",7}  Name");
	foreach (var id in ReducedShohinIds) {
		var s = db.Fetch<MasterShohin>("where Id = @0", id).FirstOrDefault();
		if (s == null) {
			Console.WriteLine($"{id,7} {"(該当なし)",14}");
			continue;
		}
		Console.WriteLine($"{s.Id,7} {s.Code,14} {s.Id_Tax,7}  {s.Name}");
	}
	Console.WriteLine();

	// 標準税率側の比較用に Id_Tax=1 の商品も少し見る
	Console.WriteLine("== Id_Tax の分布（MasterShohin 全体）==");
	foreach (var row in db.Fetch<TaxIdCount>(
		"SELECT Id_Tax, COUNT(*) AS Cnt FROM MasterShohin GROUP BY Id_Tax ORDER BY Id_Tax")) {
		Console.WriteLine($"Id_Tax={row.Id_Tax,-3} {row.Cnt,9:N0} 件");
	}
}

// 軽減税率・標準税率・非課税を混在させた伝票で、明細ごとの税率適用とヘッダ合計を突合する。
// 実DBの MasterSysTax と MasterShohin.Id_Tax を使い、伝票は投入せずメモリ上で検証する
// （TranTaxRebuildDb.ApplyMeisaiTax は伝票単位・四捨五入固定の税率解決ロジックを本体と共有する、
//   既存テスト向けの後方互換ラッパー）。
int Mixed() {
	var fail = 0;
	void Check(string label, object expected, object actual) {
		var ok = Equals(expected, actual);
		if (!ok) fail++;
		Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {label}: 期待={expected} 実際={actual}");
	}

	var sysman = db.Fetch<MasterSysman>("where Id = 1").FirstOrDefault() ?? new MasterSysman();
	var taxIdByShohin = new TranTaxRebuildDb(db).LoadShohinTaxIds();
	const string DenDay = "20260825";

	// 標準税率(Id_Tax=1)の商品を実DBから1件借りる
	var standard = db.Fetch<MasterShohin>("where Id_Tax = 1 order by Id limit 1").First();
	Console.WriteLine($"== 混在伝票テスト (伝票日付 {DenDay}) ==");
	Console.WriteLine($"標準税率商品: Id={standard.Id} {standard.Code} {standard.Name}");
	Console.WriteLine();

	// 明細: 軽減税率2行 + 標準税率1行 + 非課税1行(存在しない商品Id=0で既定1になる分と区別するため後で個別確認)
	var meisai = new List<Tran99Meisai> {
		new() { No = 1, Id_Shohin = 37522, Su = 10, Tanka = 1000, Kingaku = 10000 }, // 軽減 8%
		new() { No = 2, Id_Shohin = 37715, Su = 3, Tanka = 500, Kingaku = 1500 },    // 軽減 8%
		new() { No = 3, Id_Shohin = standard.Id, Su = 2, Tanka = 3000, Kingaku = 6000 }, // 標準 10%
		new() { No = 4, Id_Shohin = 0, Su = 1, Tanka = 2000, Kingaku = 2000 },       // 商品なし→標準1
	};

	// ApplyMeisaiTaxは税区分(Id_Tax 1-3)ごとの合計をタプルで返す(フェーズ6でTaxCalculator.Applyへ委譲する形に
	// 変わった際、ヘッダのTax1/2/3へそのまま代入できるようタプル化された)。本ツールはヘッダTax1/2/3の合計と
	// 明細ごとの内訳を突合するため、まず合計(headerTax)を出す。
	var headerTaxByGroup = TranTaxRebuildDb.ApplyMeisaiTax(meisai, sysman, taxIdByShohin, DenDay);
	var headerTax = (int)(headerTaxByGroup.Tax1 + headerTaxByGroup.Tax2 + headerTaxByGroup.Tax3);
	Console.WriteLine($"  Tax1(標準)={headerTaxByGroup.Tax1:N0} Tax2(軽減)={headerTaxByGroup.Tax2:N0} Tax3={headerTaxByGroup.Tax3:N0}");

	Console.WriteLine($"{"No",3} {"Id_Shohin",10} {"Kingaku",9} {"Id_Tax",7} {"TaxRate",8} {"Tax",7}");
	foreach (var m in meisai) {
		Console.WriteLine($"{m.No,3} {m.Id_Shohin,10} {m.Kingaku,9:N0} {m.Id_Tax,7} {m.TaxRate,7}% {m.Tax,7:N0}");
	}
	Console.WriteLine();

	// 明細ごとの税率が商品の税区分どおりに分かれること
	Check("明細1 税区分(軽減)", 2L, meisai[0].Id_Tax);
	Check("明細1 税率", 8, meisai[0].TaxRate);
	Check("明細1 税額 10000*8%", 800, meisai[0].Tax);
	Check("明細2 税率", 8, meisai[1].TaxRate);
	Check("明細2 税額 1500*8%", 120, meisai[1].Tax);
	Check("明細3 税区分(標準)", 1L, meisai[2].Id_Tax);
	Check("明細3 税率", 10, meisai[2].TaxRate);
	Check("明細3 税額 6000*10%", 600, meisai[2].Tax);
	Check("明細4 商品なしは標準税率を既定", 1L, meisai[3].Id_Tax);
	Check("明細4 税額 2000*10%", 200, meisai[3].Tax);

	// ヘッダは明細税額の合計
	Check("ヘッダTax = 明細合計", 800 + 120 + 600 + 200, headerTax);
	Check("ヘッダTax = Sum(明細Tax)", meisai.Sum(m => m.Tax), headerTax);

	// 単一税率で一括計算した場合との差（軽減税率が効いていることの裏付け）
	var kingakuTotal = meisai.Sum(m => m.Kingaku);
	var flatTax = (int)Math.Round(Math.Abs(kingakuTotal) * 10 / 100.0);
	Console.WriteLine();
	Console.WriteLine($"  参考: 全件10%一括なら {flatTax:N0} 円 → 明細別だと {headerTax:N0} 円（差 {headerTax - flatTax:N0} 円）");
	if (headerTax == flatTax) {
		fail++;
		Console.WriteLine("  [FAIL] 軽減税率が効いていない（一括計算と同額）");
	}

	// 非課税(Id_Tax=0)の確認。実DBに Id_Tax=0 の商品が無いためマップを差し替えて検証する
	Console.WriteLine();
	Console.WriteLine("== 非課税(Id_Tax=0)の確認 ==");
	var hikazei = new List<Tran99Meisai> { new() { No = 1, Id_Shohin = 37522, Su = 1, Tanka = 5000, Kingaku = 5000 } };
	var hikazeiMap = new Dictionary<long, long> { [37522] = 0 };
	var hikazeiTax = TranTaxRebuildDb.ApplyMeisaiTax(hikazei, sysman, hikazeiMap, DenDay);
	Check("非課税 税率", 0, hikazei[0].TaxRate);
	Check("非課税 税額", 0, hikazei[0].Tax);
	Check("非課税 ヘッダTax", 0L, hikazeiTax.Tax1 + hikazeiTax.Tax2 + hikazeiTax.Tax3);

	// 税率切替日をまたぐか（Id=1 は 20191001 から 10%、それ以前は 8%）
	Console.WriteLine();
	Console.WriteLine("== 税率切替日をまたぐ確認 (MasterSysTax Id=1: 20191001 から 10%) ==");
	foreach (var (day, expected) in new[] { ("20190930", 8), ("20191001", 10) }) {
		var line = new List<Tran99Meisai> { new() { No = 1, Id_Shohin = standard.Id, Su = 1, Tanka = 10000, Kingaku = 10000 } };
		TranTaxRebuildDb.ApplyMeisaiTax(line, sysman, taxIdByShohin, day);
		Check($"伝票日付 {day} の税率", expected, line[0].TaxRate);
	}

	// 返品相当（金額が負）でも明細税額は正値
	Console.WriteLine();
	Console.WriteLine("== 返品相当(金額が負)でも明細税額は正値 ==");
	var henpin = new List<Tran99Meisai> { new() { No = 1, Id_Shohin = 37522, Su = -5, Tanka = 1000, Kingaku = -5000 } };
	TranTaxRebuildDb.ApplyMeisaiTax(henpin, sysman, taxIdByShohin, DenDay);
	Check("返品明細の税額(5000*8%の正値)", 400, henpin[0].Tax);

	return fail;
}

// クライアント側 AppGlobal.LogicGetTax と同じ式（サーバ側ヘルパーとの差異を見るため再現）
static int ClientLogicGetTaxEquivalent(MasterSysman sysman, long no, string dateYmd) {
	var systax = sysman.Jsub?.FirstOrDefault(x => x.Id == no) ?? new MasterSysTax();
	var tax = systax.TaxRate;
	// 実物は Common.CompareYmd を無条件に呼ぶため DateFrom が空だと例外になる。
	// ここでは比較可能な場合だけ評価し、例外は -1 で表す
	if (string.IsNullOrWhiteSpace(systax.DateFrom) || systax.DateFrom.Length != 8) {
		return no <= 0 ? -1 : tax;
	}
	if (CvAsset.Common.CompareYmd(dateYmd, systax.DateFrom) >= 0) {
		tax = systax.TaxNewRate;
	}
	return tax;
}

public class TaxIdCount {
	public long Id_Tax { get; set; }
	public long Cnt { get; set; }
}
