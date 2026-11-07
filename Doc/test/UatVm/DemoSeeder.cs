using System.Data;
using System.Globalization;
using System.IO;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using Microsoft.Data.Sqlite;

namespace UatVm.Seed;

/// <summary>
/// デモ動画撮影用の架空会社DB（CV10デモアパレル株式会社）を空のDBファイルから作成する。
/// <para>
/// 製品と同じ初期化（<see cref="DefineDataTable.InitializeAsync"/>）のあと、初期化が作るサンプル
/// （NewBrand・サンプル商品・サンプル得意先・サンプル店舗A等）を削除し、架空のマスタと
/// 2026-08-01〜2026-11-06 の伝票（発注→仕入、移動、店舗売上、受注→卸売上、入金・支払）と予算を投入する。
/// 日付は固定、乱数は固定シードのため、同じコードなら何度作っても同じ内容になる。
/// </para>
/// <para>
/// 集計表（在庫・売掛・買掛・請求残・支払残）は作らない。投入後に UatVm の resummaryall で全件再集計する。
/// 伝票の値は入力画面（各 InputViewModel）と同じ規則で埋める：明細金額=数量×単価、上代・下代は商品マスタ、
/// 消費税は <see cref="TaxCalculator.Apply"/>（伝票単位、税区分ごとに1回丸め）、返品は正値で持ち符号はヘッダ CalcFlag。
/// 在庫は投入中に倉庫・店舗×SKUで追跡し、マイナスになる出庫は作らない。
/// </para>
/// </summary>
public static class DemoSeeder {
	/// <summary>自社名</summary>
	public const string CompanyName = "CV10デモアパレル株式会社";
	/// <summary>期首年月日</summary>
	public const string FiscalStartDate = "20260801";
	/// <summary>伝票の開始日</summary>
	public static readonly DateOnly FirstDay = new(2026, 8, 1);
	/// <summary>伝票の終了日（撮影前日）</summary>
	public static readonly DateOnly LastDay = new(2026, 11, 6);

	/// <summary>
	/// デモDBを作成する。
	/// </summary>
	/// <param name="dbPath">作成先。ファイル名が demo- で始まり .db で終わり、CvServerフォルダ外であること</param>
	/// <param name="recreate">既存の同名DB（-wal/-shm を含む）を削除して作り直す</param>
	/// <param name="trace">進捗・結果の出力先</param>
	public static void Run(string dbPath, bool recreate, Action<string> trace) {
		ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
		ArgumentNullException.ThrowIfNull(trace);
		var path = PreparePath(dbPath, recreate, trace);
		using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
		connection.Open();
		using var db = new ExDatabaseSqlite(connection) { KeepConnectionAlive = true };
		if (!new DefineDataTable().InitializeAsync(db, false).GetAwaiter().GetResult()) {
			throw new InvalidOperationException("デモDBの初期化に失敗しました。");
		}
		trace($"初期化: {path}");
		var builder = new Builder(db, trace);
		try {
			db.BeginTransaction(IsolationLevel.Serializable);
			builder.Build();
			db.CompleteTransaction();
		}
		catch {
			db.AbortTransaction();
			throw;
		}
		builder.Report();
	}

	/// <summary>
	/// 作成先を検査する。共有開発DB等を誤って更新しないよう、CvServerフォルダ外の demo-*.db だけを許可する。
	/// 作り直し指定時に削除するのは、そのDBと -wal/-shm だけ。
	/// </summary>
	static string PreparePath(string dbPath, bool recreate, Action<string> trace) {
		var path = Path.GetFullPath(dbPath);
		if (!Path.GetFileName(path).StartsWith("demo-", StringComparison.OrdinalIgnoreCase)
			|| !path.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
			|| path.Split(Path.DirectorySeparatorChar).Any(x => x.Equals("CvServer", StringComparison.OrdinalIgnoreCase))) {
			throw new InvalidOperationException($"CvServerフォルダ外の demo-*.db だけ使用できます: {path}");
		}
		if (recreate) {
			foreach (var file in new[] { path, path + "-wal", path + "-shm" }) {
				if (!File.Exists(file)) continue;
				File.Delete(file);
				trace($"削除: {file}");
			}
		}
		if (File.Exists(path) && new FileInfo(path).Length > 0) {
			throw new InvalidOperationException($"既存のDBがあります。作り直す場合は --recreate を付けてください: {path}");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		return path;
	}

	// ───────── 架空マスタの定義 ─────────

	/// <summary>商品の定義。Season: S=春夏 A=秋口 W=冬 N=定番。Sizes: S/M/L/X(XL)/F(FREE)。Colors: カラー略号</summary>
	sealed record ProductDef(string Code, string Brand, string Item, string Name, int Jodai, char Season, string Sizes, string Colors);

	static readonly ProductDef[] ProductDefs = [
		// VELNOA（レディース）
		new("VL-S001", "01", "06", "リネンブレンド スキッパーシャツ", 12900, 'S', "SML", "WBN"),
		new("VL-S002", "01", "01", "フレンチスリーブ コットンブラウス", 9900, 'S', "SML", "WP"),
		new("VL-S003", "01", "02", "ティアード マキシワンピース", 18900, 'S', "SM", "KB"),
		new("VL-S004", "01", "07", "ドライタッチ ボートネックTシャツ", 4900, 'S', "SML", "WNG"),
		new("VL-S005", "01", "04", "リネン ワイドパンツ", 13900, 'S', "SML", "BH"),
		new("VL-S006", "01", "09", "シアー ロングカーディガン", 11900, 'S', "F", "WK"),
		new("VL-S007", "01", "02", "フレアスリーブ プリントワンピース", 16900, 'S', "SM", "NP"),
		new("VL-A001", "01", "01", "ボウタイ サテンブラウス", 12900, 'A', "SML", "BNK"),
		new("VL-A002", "01", "06", "ペプラム コットンシャツ", 10900, 'A', "SML", "WG"),
		new("VL-A003", "01", "04", "タックテーパード スラックス", 14900, 'A', "SML", "KGB"),
		new("VL-A004", "01", "02", "ニットドッキング ワンピース", 19900, 'A', "SM", "KR"),
		new("VL-A005", "01", "05", "ノーカラー ツイードジャケット", 26900, 'A', "SML", "BK"),
		new("VL-A006", "01", "09", "クルーネック ショートカーディガン", 9900, 'A', "F", "BPG"),
		new("VL-A007", "01", "08", "プリーツ ロングスカート", 12900, 'A', "F", "KHB"),
		new("VL-W001", "01", "03", "ウールブレンド ケーブルニット", 15900, 'W', "F", "WRG"),
		new("VL-W002", "01", "03", "カシミヤタッチ タートルニット", 12900, 'W', "F", "KBP"),
		new("VL-W003", "01", "10", "ダブルフェイス ロングコート", 29000, 'W', "SM", "BN"),
		new("VL-W004", "01", "05", "ボアフリース ショートブルゾン", 19900, 'W', "F", "WR"),
		new("VL-W005", "01", "02", "ハイネック リブワンピース", 16900, 'W', "F", "KG"),
		new("VL-N001", "01", "04", "センタープレス ストレートパンツ", 13900, 'N', "SML", "KN"),
		new("VL-N002", "01", "06", "とろみ素材 レギュラーシャツ", 9900, 'N', "SML", "WK"),
		new("VL-N003", "01", "04", "ウエストリボン イージーパンツ", 8900, 'N', "SML", "NG"),
		// MIRAVE（メンズ）
		new("MR-S001", "02", "06", "オープンカラー リネンシャツ", 11900, 'S', "MLX", "WNB"),
		new("MR-S002", "02", "07", "ヘビーウェイト ポケットTシャツ", 5900, 'S', "MLX", "WKH"),
		new("MR-S003", "02", "04", "シアサッカー イージーショーツ", 7900, 'S', "MLX", "NB"),
		new("MR-S004", "02", "03", "サマーニット ポロ", 9900, 'S', "MLX", "NG"),
		new("MR-A001", "02", "06", "オックスフォード ボタンダウンシャツ", 9900, 'A', "MLX", "WN"),
		new("MR-A002", "02", "04", "ストレッチ チノパンツ", 10900, 'A', "MLX", "BHN"),
		new("MR-A003", "02", "05", "コーデュロイ ワークジャケット", 22900, 'A', "MLX", "RH"),
		new("MR-A004", "02", "05", "ウールライク テーラードジャケット", 24900, 'A', "MLX", "NG"),
		new("MR-A005", "02", "07", "ワッフル ロングスリーブTシャツ", 5900, 'A', "MLX", "WGK"),
		new("MR-A006", "02", "04", "ジャージー イージースラックス", 11900, 'A', "MLX", "KG"),
		new("MR-W001", "02", "03", "ミドルゲージ クルーネックニット", 13900, 'W', "MLX", "NGB"),
		new("MR-W002", "02", "10", "ステンカラー ウールコート", 29000, 'W', "MLX", "KN"),
		new("MR-W003", "02", "05", "中綿 キルティングジャケット", 15900, 'W', "MLX", "KH"),
		new("MR-W004", "02", "06", "フランネル チェックシャツ", 10900, 'W', "MLX", "NR"),
		new("MR-N001", "02", "04", "テーパード デニムパンツ", 12900, 'N', "MLX", "NK"),
		new("MR-N002", "02", "14", "裏毛 スウェットパーカー", 9900, 'N', "MLX", "GN"),
		// kotoni（雑貨）
		new("KT-S001", "03", "13", "ペーパーブレード ハット", 5900, 'S', "F", "BK"),
		new("KT-S002", "03", "11", "UVカット リネンストール", 4900, 'S', "F", "WB"),
		new("KT-A001", "03", "13", "ウールフェルト ハット", 6900, 'A', "F", "KR"),
		new("KT-W001", "03", "11", "ウールブレンド 大判ストール", 6900, 'W', "F", "GRP"),
		new("KT-W002", "03", "11", "カシミヤタッチ マフラー", 5900, 'W', "F", "NGB"),
		new("KT-W003", "03", "13", "ニット ベレー帽", 3900, 'W', "F", "KB"),
		new("KT-N001", "03", "12", "コットンリネン トートバッグ", 6900, 'N', "F", "BN"),
		new("KT-N002", "03", "12", "レザーライク ショルダーバッグ", 12900, 'N', "F", "KR"),
		new("KT-N003", "03", "12", "ナイロン 2WAYリュック", 9900, 'N', "F", "KH"),
		new("KT-N004", "03", "12", "キャンバス ミニショルダー", 4900, 'N', "F", "WK"),
	];

	/// <summary>カラー略号 → (名称コード, 名称)</summary>
	static readonly Dictionary<char, (string Code, string Name)> ColorDefs = new() {
		['W'] = ("01", "ホワイト"), ['K'] = ("02", "ブラック"), ['N'] = ("03", "ネイビー"), ['B'] = ("04", "ベージュ"),
		['G'] = ("05", "グレー"), ['H'] = ("06", "カーキ"), ['P'] = ("07", "ピンク"), ['R'] = ("08", "ブラウン"),
	};
	/// <summary>サイズ略号 → (名称コード, 名称)</summary>
	static readonly Dictionary<char, (string Code, string Name)> SizeDefs = new() {
		['S'] = ("01", "S"), ['M'] = ("02", "M"), ['L'] = ("03", "L"), ['X'] = ("04", "XL"), ['F'] = ("05", "FREE"),
	};

	sealed class Shop {
		public required MasterTokui M { get; init; }
		public required MasterTokui Source { get; init; }
		public required MasterShain Manager { get; init; }
		public required MasterShain Clerk { get; init; }
		public required double Base { get; init; }
		public required Dictionary<string, double> Affinity { get; init; }
		public List<MasterEndCustomer> Members { get; } = [];
		public List<(Tran01Tenuri Den, Tran99Meisai Line, DateOnly Day)> Recent { get; } = [];
	}

	sealed class Wholesale {
		public required MasterTokui M { get; init; }
		public required MasterShain Sales { get; init; }
		public required int Fee { get; init; }
		public required string[] Brands { get; init; }
		public required double Scale { get; init; }
		public DateOnly NextOrder { get; set; }
		public List<(Sku Sku, int Tanka, int Su)> Shipped { get; } = [];
	}

	sealed class Supplier {
		public required MasterShiire M { get; init; }
		public required string KinCode { get; init; }
	}

	sealed class Product {
		public required int Index { get; init; }
		public required MasterShohin M { get; init; }
		public required char Season { get; init; }
		public required string Brand { get; init; }
		public required Supplier Supplier { get; init; }
		public required double Popularity { get; init; }
		public List<Sku> Skus { get; } = [];
	}

	sealed class Sku {
		public required int Index { get; init; }
		public required Product P { get; init; }
		public required MasterMeisho Col { get; init; }
		public required MasterMeisho Siz { get; init; }
		public required string Jan { get; init; }
		public required double Share { get; init; }
	}

	sealed class OpenHachu {
		public required Tran13Hachu Den { get; init; }
		public required Supplier Supplier { get; init; }
		public required Dictionary<int, int> Remaining { get; init; }
		public List<(DateOnly Day, Dictionary<int, int> Qty)> Deliveries { get; } = [];
	}

	sealed class OpenJuchu {
		public required Tran12Jyuchu Den { get; init; }
		public required Wholesale Customer { get; init; }
		public required DateOnly Nouhin { get; init; }
		public required Dictionary<int, int> Remaining { get; init; }
		public required Dictionary<int, int> Tanka { get; init; }
		public DateOnly LastShip { get; set; } = DateOnly.MinValue;
	}

	/// <summary>投入処理の本体。マスタ・伝票の状態（在庫・発注残・受注残・掛の発生）を日付順に追跡する。</summary>
	sealed class Builder(ExDatabaseSqlite db, Action<string> trace) {
		const int MaxLines = 25;
		const double UnitsPerSlip = 1.6;
		/// <summary>卸の都度注文の見込み（全卸先合計の1日あたり数量）。発注量の計算だけに使う</summary>
		const double WholesaleDailyUnits = 30.0;
		static readonly DateOnly Exhibition = new(2026, 8, 18);
		static readonly DateOnly SaleFrom = new(2026, 8, 22);
		static readonly DateOnly SaleTo = new(2026, 9, 30);

		readonly Random rng = new(20261107);
		MasterSysman sysman = null!;
		MasterShain admin = null!;
		MasterShain buyer = null!;
		MasterTokui honsha = null!;
		MasterTokui kansai = null!;
		readonly Dictionary<string, MasterMeisho> brands = [];
		readonly Dictionary<string, MasterMeisho> items = [];
		readonly Dictionary<string, MasterMeisho> colors = [];
		readonly Dictionary<string, MasterMeisho> sizes = [];
		readonly Dictionary<string, MasterMeisho> seasons = [];
		readonly Dictionary<string, MasterMeisho> bumons = [];
		readonly Dictionary<string, MasterMeisho> kins = [];
		readonly Dictionary<string, MasterShain> staff = [];
		readonly List<Shop> shops = [];
		readonly List<Wholesale> wholesale = [];
		readonly List<Supplier> suppliers = [];
		readonly List<Product> products = [];
		readonly List<Sku> skus = [];
		readonly Dictionary<(long Soko, int Sku), int> stock = [];
		readonly List<OpenHachu> openHachu = [];
		readonly List<OpenJuchu> openJuchu = [];
		readonly List<(long Id, DateOnly Day, long Amount)> salesLedger = [];
		readonly List<(long Id, DateOnly Day, long Amount)> purchaseLedger = [];
		readonly Dictionary<(long Shop, string Brand, string Ym), (long Sales, long Gross)> shopBrandActual = [];
		readonly Dictionary<(long Shain, string Ym), (long Sales, long Gross)> staffActual = [];
		readonly Dictionary<(long Shop, int Day), double[]> shareCache = [];
		readonly Dictionary<int, double[]> genericShareCache = [];
		int manualSeq;

		public void Build() {
			RemoveSampleData();
			CreateMeisho();
			CreateStaff();
			CreateTokui();
			CreateSuppliers();
			UpdateSysman();
			CreateProducts();
			CreateMembers();
			for (var d = FirstDay; d <= LastDay; d = d.AddDays(1)) {
				RunDay(d);
			}
			CreatePayments();
			CreateBudgets();
		}

		// ───────── マスタ ─────────

		/// <summary>初期化が作るサンプル（CreateSampleData）を削除する。管理者・ログイン・自社設定・標準区分は残す。</summary>
		void RemoveSampleData() {
			foreach (var shohin in db.Fetch<MasterShohin>("where Code=@0", "0001")) {
				db.Execute(DerivedShohinColSiz.DeleteSql, shohin.Id);
				db.Execute("DELETE FROM MasterShohin WHERE Id=@0", shohin.Id);
			}
			db.Execute("DELETE FROM MasterMeisho WHERE Kubun IN (@0,@1,@2,@3) AND Name IN ('NewBrand','NewItem','NewColor','NewSize')",
				MasterMeisho.KubunBrand, MasterMeisho.KubunItem, MasterMeisho.KubunColor, MasterMeisho.KubunSize);
			db.Execute("DELETE FROM MasterEndCustomer WHERE Name=@0", "サンプル顧客");
			db.Execute("DELETE FROM MasterShiire WHERE Name=@0", "サンプル仕入先");
			db.Execute("DELETE FROM MasterTokui WHERE Name IN (@0,@1)", "サンプル得意先", "サンプル店舗A");
			admin = db.Fetch<MasterShain>("order by Id").First();
		}

		void CreateMeisho() {
			var existingIndex = db.Fetch<MasterMeisho>("where Kubun=@0", MasterMeisho.KubunIndex).Select(x => x.Code).ToHashSet();
			foreach (var (code, name) in new[] { (MasterMeisho.KubunSeason, "シーズン"), (MasterMeisho.KubunBumon, "部門"), (MasterMeisho.KubunKin, "入金・支払方法") }) {
				if (!existingIndex.Contains(code)) Meisho(MasterMeisho.KubunIndex, "名称区分", code, name, "", 0);
			}
			foreach (var (code, name, ryaku, kana) in new[] { ("01", "VELNOA", "VELNOA", "ヴェルノア"), ("02", "MIRAVE", "MIRAVE", "ミラーヴェ"), ("03", "kotoni", "kotoni", "コトニ") }) {
				brands[code] = Meisho(MasterMeisho.KubunBrand, "ブランド", code, name, ryaku, int.Parse(code, CultureInfo.InvariantCulture), kana);
			}
			string[] itemNames = ["ブラウス", "ワンピース", "ニット", "パンツ", "ジャケット", "シャツ", "Tシャツ", "スカート", "カーディガン", "コート", "ストール", "バッグ", "帽子", "スウェット"];
			for (var i = 0; i < itemNames.Length; i++) {
				var code = (i + 1).ToString("00", CultureInfo.InvariantCulture);
				items[code] = Meisho(MasterMeisho.KubunItem, "アイテム", code, itemNames[i], itemNames[i], i + 1);
			}
			foreach (var (_, (code, name)) in ColorDefs) {
				colors[code] = Meisho(MasterMeisho.KubunColor, "カラー", code, name, name, int.Parse(code, CultureInfo.InvariantCulture));
			}
			foreach (var (_, (code, name)) in SizeDefs) {
				sizes[code] = Meisho(MasterMeisho.KubunSize, "サイズ", code, name, name, int.Parse(code, CultureInfo.InvariantCulture));
			}
			foreach (var (code, name) in new[] { ("01", "2026春夏"), ("02", "2026秋冬"), ("03", "定番") }) {
				seasons[code] = Meisho(MasterMeisho.KubunSeason, "シーズン", code, name, name, int.Parse(code, CultureInfo.InvariantCulture));
			}
			foreach (var (code, name) in new[] { ("01", "店舗営業部"), ("02", "卸営業部"), ("03", "商品部"), ("04", "管理部") }) {
				bumons[code] = Meisho(MasterMeisho.KubunBumon, "部門", code, name, name, int.Parse(code, CultureInfo.InvariantCulture));
			}
			// 入金・支払の内訳は SummaryDb が名称コードで振り分ける（01現金 / 02振込手数料 / 03手形 / 04相殺 / それ以外その他）
			foreach (var (code, name) in new[] { ("01", "現金・振込"), ("02", "振込手数料"), ("03", "手形・電子記録債権"), ("04", "相殺"), ("05", "その他") }) {
				kins[code] = Meisho(MasterMeisho.KubunKin, "入金・支払方法", code, name, name, int.Parse(code, CultureInfo.InvariantCulture));
			}
		}

		MasterMeisho Meisho(string kubun, string kubunName, string code, string name, string ryaku, int odr, string kana = "") {
			var row = new MasterMeisho { Kubun = kubun, KubunName = kubunName, Code = code, Name = name, Ryaku = ryaku, Kana = kana, Odr = odr };
			StampMaster(row);
			db.Insert(row);
			return row;
		}

		void CreateStaff() {
			var defs = new[] {
				("0101", "佐藤 美咲", "サトウ ミサキ", "01"), ("0102", "高橋 結衣", "タカハシ ユイ", "01"),
				("0201", "田中 健太", "タナカ ケンタ", "01"), ("0202", "伊藤 彩花", "イトウ アヤカ", "01"),
				("0301", "山本 拓也", "ヤマモト タクヤ", "01"), ("0302", "中村 さくら", "ナカムラ サクラ", "01"),
				("0401", "小林 大輔", "コバヤシ ダイスケ", "01"), ("0402", "加藤 葵", "カトウ アオイ", "01"),
				("0501", "吉田 翔", "ヨシダ ショウ", "02"), ("0502", "山田 真由美", "ヤマダ マユミ", "02"),
				("0601", "松本 浩二", "マツモト コウジ", "03"),
			};
			foreach (var (code, name, kana, bumon) in defs) {
				var row = new MasterShain { Code = code, Name = name, Ryaku = name.Split(' ')[0], Kana = kana,
					Id_Bumon = bumons[bumon].Id, VBumon = V(bumons[bumon]) };
				StampMaster(row);
				db.Insert(row);
				staff[code] = row;
			}
			admin.Id_Bumon = bumons["04"].Id;
			admin.VBumon = V(bumons["04"]);
			db.Update(admin);
			buyer = staff["0601"];
		}

		void CreateTokui() {
			honsha = Tokui("000001", "本社倉庫", "本社倉庫", EnumTokui._0_Soko, buyer, "東京都", "江東区デモ町3-1-1", "CV10デモ物流センター", "03-0000-1001");
			kansai = Tokui("000002", "関西倉庫", "関西倉庫", EnumTokui._0_Soko, buyer, "大阪府", "大阪市住之江区デモ町5-2-8", "", "06-0000-1002");
			var shopDefs = new[] {
				("000101", "表参道店", "表参道", "東京都", "渋谷区デモ町1-4-7", "デモ表参道ビル1F", "03-0000-2101", "0101", "0102", 21.0, honsha, (1.30, 0.80, 1.05)),
				("000102", "横浜店", "横浜", "神奈川県", "横浜市西区デモ町2-2-2", "デモスクエア2F", "045-000-2102", "0201", "0202", 17.0, honsha, (1.00, 1.15, 1.00)),
				("000103", "梅田店", "梅田", "大阪府", "大阪市北区デモ町8-1-3", "デモ梅田モール3F", "06-0000-2103", "0301", "0302", 19.0, kansai, (1.10, 1.00, 1.10)),
				("000104", "福岡店", "福岡", "福岡県", "福岡市中央区デモ町4-6-9", "", "092-000-2104", "0401", "0402", 14.0, kansai, (1.00, 1.00, 0.90)),
			};
			foreach (var (code, name, ryaku, pref, addr2, addr3, tel, manager, clerk, baseSlips, source, (vl, mr, kt)) in shopDefs) {
				var m = Tokui(code, name, ryaku, EnumTokui._6_Tenpo, staff[manager], pref, addr2, addr3, tel);
				shops.Add(new Shop { M = m, Source = source, Manager = staff[manager], Clerk = staff[clerk], Base = baseSlips,
					Affinity = new() { ["01"] = vl, ["02"] = mr, ["03"] = kt } });
				foreach (var shainCode in new[] { manager, clerk }) {
					var shain = staff[shainCode];
					shain.Id_Tenpo = m.Id;
					shain.VTenpo = V(m);
					db.Update(shain);
				}
			}
			// 卸先: 締日20日/月末、回収は翌月10日・翌月末など。税は伝票単位で計算する
			var wholesaleDefs = new[] {
				("100001", "まるみ百貨店 本店", "まるみ本店", "東京都", "中央区デモ町1-1-1", "03-0000-3001", 20, 1, 10, 62, 660, "0501", new[] { "01", "02", "03" }, 1.4, EnumRounding.Floor),
				("100002", "ほしぞら百貨店", "ほしぞら", "愛知県", "名古屋市中区デモ町2-5-1", "052-000-3002", 99, 1, 99, 60, 0, "0502", new[] { "01", "02", "03" }, 1.3, EnumRounding.Round),
				("100003", "セレクトショップ ルミエラ", "ルミエラ", "東京都", "目黒区デモ町3-8-2", "03-0000-3003", 99, 1, 99, 55, 440, "0501", new[] { "01", "03" }, 0.8, EnumRounding.Round),
				("100004", "アトリエ ノクト", "ノクト", "京都府", "京都市中京区デモ町6-3", "075-000-3004", 20, 1, 99, 58, 440, "0502", new[] { "02", "03" }, 0.7, EnumRounding.Round),
				("100005", "ブティック ソラリエ", "ソラリエ", "兵庫県", "神戸市中央区デモ町4-4-4", "078-000-3005", 99, 1, 25, 55, 0, "0501", new[] { "01" }, 0.7, EnumRounding.Round),
				("100006", "ひだまり洋品店", "ひだまり", "北海道", "札幌市中央区デモ町7-1", "011-000-3006", 99, 2, 10, 60, 330, "0502", new[] { "01", "02", "03" }, 0.9, EnumRounding.Round),
				("100007", "モード カナリエ", "カナリエ", "宮城県", "仙台市青葉区デモ町9-2-5", "022-000-3007", 20, 1, 25, 57, 440, "0501", new[] { "01", "02" }, 0.8, EnumRounding.Round),
			};
			foreach (var (code, name, ryaku, pref, addr2, tel, shime, payMonth, payDay, rate, fee, sales, brandCodes, scale, rounding) in wholesaleDefs) {
				var m = Tokui(code, name, ryaku, EnumTokui._1_Oroshi, staff[sales], pref, addr2, "", tel, t => {
					t.Shime1 = shime;
					t.PayMonth = payMonth;
					t.PayDay = payDay;
					t.RateProper = rate;
					t.RateSale = rate - 10;
					t.IsPay = 1;
					t.Id_PayMethod = kins["01"].Id;
					t.VPayMethod = V(kins["01"]);
					t.TaxCalcUnit = (int)EnumTaxCalcUnit.Slip;
					t.TaxRounding = (int)rounding;
					t.LeadTimeDays = 3;
				});
				wholesale.Add(new Wholesale { M = m, Sales = staff[sales], Fee = fee, Brands = brandCodes, Scale = scale,
					NextOrder = FirstDay.AddDays(rng.Next(2, 10)) });
			}
		}

		MasterTokui Tokui(string code, string name, string ryaku, EnumTokui tenType, MasterShain shain, string pref, string addr2, string addr3, string tel, Action<MasterTokui>? setup = null) {
			var row = new MasterTokui { Code = code, Name = name, Ryaku = ryaku, TenType = (int)tenType, IsZaiko = tenType is EnumTokui._0_Soko or EnumTokui._6_Tenpo ? 1 : 0,
				Id_Shain = shain.Id, VShain = V(shain), PostalCode = "000-0000", Address1 = pref, Address2 = addr2, Address3 = addr3, Tel = tel, Shime1 = 99 };
			setup?.Invoke(row);
			StampMaster(row);
			db.Insert(row);
			return row;
		}

		void CreateSuppliers() {
			var defs = new[] {
				("1001", "あおば縫製株式会社", "あおば縫製", "岐阜県", "岐阜市デモ町1-2-3", "058-000-4001", 99, 1, 99, 40, "01"),
				("1002", "ミナモテキスタイル株式会社", "ミナモ", "大阪府", "大阪市中央区デモ町2-7-1", "06-0000-4002", 20, 1, 99, 42, "03"),
				("1003", "コモレビニット株式会社", "コモレビ", "新潟県", "五泉市デモ町3-3-3", "0250-00-4003", 99, 2, 10, 38, "01"),
				("1004", "株式会社ソラノ縫製", "ソラノ", "岡山県", "倉敷市デモ町4-1-9", "086-000-4004", 99, 1, 99, 40, "01"),
				("1005", "ハルカ雑貨工房株式会社", "ハルカ", "東京都", "台東区デモ町5-5-5", "03-0000-4005", 20, 1, 10, 45, "01"),
				("1006", "ツムギ商事株式会社", "ツムギ", "愛知県", "一宮市デモ町6-2-4", "0586-00-4006", 99, 1, 25, 42, "01"),
			};
			foreach (var (code, name, ryaku, pref, addr2, tel, shime, payMonth, payDay, rate, kin) in defs) {
				var row = new MasterShiire { Code = code, Name = name, Ryaku = ryaku, Id_Shain = buyer.Id, VShain = V(buyer),
					PostalCode = "000-0000", Address1 = pref, Address2 = addr2, Tel = tel, Shime1 = shime, PayMonth = payMonth, PayDay = payDay,
					RateProper = rate, IsPay = 1, Id_PayMethod = kins[kin].Id, VPayMethod = V(kins[kin]),
					TaxCalcUnit = (int)EnumTaxCalcUnit.Slip, TaxRounding = (int)EnumRounding.Round, LeadTimeDays = 12 };
				StampMaster(row);
				db.Insert(row);
				suppliers.Add(new Supplier { M = row, KinCode = kin });
			}
		}

		void UpdateSysman() {
			sysman = db.Fetch<MasterSysman>("order by Id").First();
			sysman.Name = CompanyName;
			sysman.PostalCode = "000-0000";
			sysman.Address1 = "東京都";
			sysman.Address2 = "千代田区デモ町1-2-3";
			sysman.Address3 = "CV10デモビル5F";
			sysman.Tel = "03-0000-1000";
			sysman.Mail = "info@demo.invalid";
			sysman.Hp = "https://demo.invalid/";
			sysman.ShimeBi = 99;
			sysman.FiscalStartDate = FiscalStartDate;
			sysman.BankAccount1 = "デモ銀行 本店営業部 普通 0000000 シーブイテンデモアパレル（カ";
			sysman.Id_Soko = honsha.Id;
			sysman.VSoko = V(honsha);
			sysman.TaxRounding = (int)EnumRounding.Round;
			// 税率表は初期化の既定（Id1=10%、Id2=8%、Id3=0%）のまま使う
			db.Update(sysman);
		}

		void CreateProducts() {
			foreach (var def in ProductDefs) {
				var supplier = SupplierOf(def);
				var genka = (int)(Math.Round(def.Jodai * (0.35 + rng.NextDouble() * 0.10) / 10.0) * 10);
				var seasonCode = def.Season switch { 'S' => "01", 'N' => "03", _ => "02" };
				var tento = def.Season switch { 'S' => "20260301", 'A' => "20260801", 'W' => "20260901", _ => "20260301" };
				var m = new MasterShohin {
					Code = def.Code, Name = def.Name, Ryaku = def.Name.Length > 12 ? def.Name[..12] : def.Name,
					Id_Brand = brands[def.Brand].Id, VBrand = V(brands[def.Brand]),
					Id_Item = items[def.Item].Id, VItem = V(items[def.Item]),
					Id_Season = seasons[seasonCode].Id, VSeason = V(seasons[seasonCode]),
					TankaJodaiOrg = def.Jodai, TankaJodai = def.Jodai, TankaGenka = genka, TankaShiire = genka,
					DayShukka = tento, DayNohin = tento, DayTento = tento,
					Id_Tax = TaxCalculator.StandardTaxId, IsZaiko = 1, MakerHin = $"{supplier.M.Code}-{products.Count + 1:000}",
					Id_Soko = honsha.Id, VSoko = V(honsha), Jcolsiz = [],
				};
				var colorWeights = ShareWeights(def.Colors.Length, 0.45);
				var sizeWeights = def.Sizes switch { "SML" => new[] { 0.30, 0.45, 0.25 }, "SM" => new[] { 0.45, 0.55 }, "MLX" => new[] { 0.35, 0.40, 0.25 }, _ => new[] { 1.0 } };
				var product = new Product { Index = products.Count, M = m, Season = def.Season, Brand = def.Brand, Supplier = supplier, Popularity = 0.6 + rng.NextDouble() * 0.9 };
				var skuDefs = new List<(MasterMeisho Col, MasterMeisho Siz, string Jan, double Share)>();
				for (var c = 0; c < def.Colors.Length; c++) {
					for (var s = 0; s < def.Sizes.Length; s++) {
						var col = colors[ColorDefs[def.Colors[c]].Code];
						var siz = sizes[SizeDefs[def.Sizes[s]].Code];
						var jan = Jan($"2026{product.Index + 1:000}{col.Code}{siz.Code}0");
						var colsiz = new MasterShohinColSiz { Id_Col = col.Id, Code_Col = col.Code, Mei_Col = col.Name, Id_Siz = siz.Id, Code_Siz = siz.Code, Mei_Siz = siz.Name, Jan1 = jan };
						StampMaster(colsiz);
						m.Jcolsiz.Add(colsiz);
						skuDefs.Add((col, siz, jan, colorWeights[c] * sizeWeights[s]));
					}
				}
				StampMaster(m);
				db.Insert(m);
				db.ExecuteDialect(DerivedShohinColSiz.InsertSql, m.Id);
				foreach (var (col, siz, jan, share) in skuDefs) {
					var sku = new Sku { Index = skus.Count, P = product, Col = col, Siz = siz, Jan = jan, Share = share };
					product.Skus.Add(sku);
					skus.Add(sku);
				}
				products.Add(product);
			}
		}

		/// <summary>アイテムとブランドで仕入先を決める（縫製・生地・ニット・雑貨の専業）</summary>
		Supplier SupplierOf(ProductDef def) => def.Item switch {
			"01" or "02" or "08" => suppliers[0],
			"06" => def.Brand == "01" ? suppliers[0] : suppliers[3],
			"07" => suppliers[3],
			"04" or "05" or "10" => suppliers[1],
			"03" or "09" or "14" => suppliers[2],
			"11" => suppliers[5],
			_ => suppliers[4],
		};

		static double[] ShareWeights(int count, double first) => count switch {
			1 => [1.0],
			2 => [0.55, 0.45],
			_ => [first, .. Enumerable.Repeat((1.0 - first) / (count - 1), count - 1)],
		};

		/// <summary>12桁の本体にチェックデジットを付けたJAN（先頭20はインストアコード）</summary>
		static string Jan(string body12) {
			var sum = 0;
			for (var i = 0; i < 12; i++) sum += (body12[i] - '0') * (i % 2 == 0 ? 1 : 3);
			return body12 + ((10 - sum % 10) % 10).ToString(CultureInfo.InvariantCulture);
		}

		void CreateMembers() {
			string[] family = ["佐々木", "井上", "木村", "林", "清水", "山口", "森", "池田", "橋本", "阿部", "石川", "前田", "藤田", "小川", "岡田", "後藤", "長谷川", "村上", "近藤", "石井"];
			(string Name, string Kana, int Gender)[] given = [("真理子", "マリコ", 2), ("陽子", "ヨウコ", 2), ("恵", "メグミ", 2), ("直樹", "ナオキ", 1), ("裕子", "ユウコ", 2),
				("和也", "カズヤ", 1), ("美穂", "ミホ", 2), ("聡", "サトシ", 1), ("由美", "ユミ", 2), ("亮", "リョウ", 1),
				("麻衣", "マイ", 2), ("剛", "ツヨシ", 1), ("香織", "カオリ", 2), ("誠", "マコト", 1), ("千尋", "チヒロ", 2), ("瞳", "ヒトミ", 2)];
			string[] familyKana = ["ササキ", "イノウエ", "キムラ", "ハヤシ", "シミズ", "ヤマグチ", "モリ", "イケダ", "ハシモト", "アベ", "イシカワ", "マエダ", "フジタ", "オガワ", "オカダ", "ゴトウ", "ハセガワ", "ムラカミ", "コンドウ", "イシイ"];
			var no = 0;
			foreach (var shop in shops) {
				for (var i = 0; i < 10; i++, no++) {
					var f = (no * 7 + 3) % family.Length;
					var g = given[(no * 5 + 1) % given.Length];
					var birthday = new DateOnly(1965 + rng.Next(0, 38), rng.Next(1, 13), rng.Next(1, 29));
					var row = new MasterEndCustomer { Code = $"C{no + 1:00000}", Name = $"{family[f]} {g.Name}", Ryaku = family[f], Kana = $"{familyKana[f]} {g.Kana}",
						Id_Tenpo = shop.M.Id, VTenpo = V(shop.M), Gender = g.Gender, Birthday = Ymd(birthday), BirthNoyear = birthday.ToString("MMdd", CultureInfo.InvariantCulture),
						Rank = i < 3 ? "A" : "B", Tel = $"090-0000-{no + 1:0000}", Mail = $"member{no + 1:000}@demo.invalid" };
					StampMaster(row);
					db.Insert(row);
					shop.Members.Add(row);
				}
			}
		}

		// ───────── 日次の伝票生成 ─────────

		void RunDay(DateOnly d) {
			var weekday = d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
			if (d == FirstDay) PlaceOrders(d, initial: true);
			ReceiveDeliveries(d);
			if (d.DayOfWeek == DayOfWeek.Monday) PlaceOrders(d, initial: false);
			if (d >= Exhibition && d < Exhibition.AddDays(4)) TakeJuchu(d);
			if (weekday) {
				ShipJuchu(d);
				WholesaleOrders(d);
			}
			if (d == new DateOnly(2026, 9, 8) || d == new DateOnly(2026, 9, 24)) WholesaleReturn(d);
			if (d == new DateOnly(2026, 9, 11)) SupplierReturn(d);
			if (d == new DateOnly(2026, 10, 15)) SeasonEndReturn(d);
			if (d == FirstDay || d.DayOfWeek == DayOfWeek.Monday) TransferToKansai(d);
			if (d == FirstDay || d.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Thursday) TransferToShops(d);
			ShopSales(d);
		}

		/// <summary>
		/// 発注。初日は期首在庫として当日入荷、以降は毎週月曜に仕入先別で、入荷予定（リードタイム10〜14日）から3週間分の
		/// 見込み販売＋受注残−（全拠点在庫＋発注残）を発注する。25%は2回に分けて入荷する。
		/// </summary>
		void PlaceOrders(DateOnly d, bool initial) {
			var leads = suppliers.ToDictionary(s => s, _ => initial ? 0 : rng.Next(10, 15));
			var plan = new List<(Sku Sku, int Su)>();
			foreach (var k in skus) {
				var lead = leads[k.P.Supplier];
				var end = d.AddDays(initial ? 35 : lead + 21);
				var demand = 0.0;
				for (var x = d; x <= end; x = x.AddDays(1)) demand += DailyDemand(k, x);
				if (SeasonWeight(k.P.Season, d.AddDays(lead)) >= 0.1) demand += shops.Count;
				demand += openJuchu.Sum(j => j.Remaining.GetValueOrDefault(k.Index));
				var need = demand * 1.1 - TotalStock(k) - openHachu.Sum(h => h.Remaining.GetValueOrDefault(k.Index));
				if (need >= 2) plan.Add((k, (int)Math.Ceiling(need)));
			}
			foreach (var group in plan.GroupBy(x => x.Sku.P.Supplier)) {
				foreach (var chunk in Chunk(group)) {
					CreateHachu(d, group.Key, chunk, leads[group.Key], initial);
				}
			}
		}

		void CreateHachu(DateOnly d, Supplier sup, List<(Sku Sku, int Su)> lines, int lead, bool initial) {
			var den = new Tran13Hachu {
				DenDay = Ymd(d), NouhinDay = Ymd(d.AddDays(lead)), Kubun = (int)EnumHachu.Hachu,
				Id_Shiire = sup.M.Id, VShiire = V(sup.M), Id_Soko = honsha.Id, VSoko = V(honsha), Id_Shain = buyer.Id, VShain = V(buyer),
				Rate = sup.M.RateProper, TaxRounding = sup.M.TaxRounding, Memo = initial ? "期首在庫 初回発注" : "",
			};
			var meisai = lines.Select((x, i) => Line(i + 1, x.Sku, x.Su, x.Sku.P.M.TankaGenka, taxable: true)).ToList();
			Finish(den, meisai, EnumTaxCalcUnit.Slip, (EnumRounding)sup.M.TaxRounding, d, 17 * 60);
			var order = new OpenHachu { Den = den, Supplier = sup, Remaining = lines.ToDictionary(x => x.Sku.Index, x => x.Su) };
			if (!initial && rng.NextDouble() < 0.25) {
				order.Deliveries.Add((d.AddDays(lead), lines.ToDictionary(x => x.Sku.Index, x => (int)Math.Ceiling(x.Su * 0.7))));
				var rest = lines.Where(x => x.Su - (int)Math.Ceiling(x.Su * 0.7) > 0).ToDictionary(x => x.Sku.Index, x => x.Su - (int)Math.Ceiling(x.Su * 0.7));
				if (rest.Count > 0) order.Deliveries.Add((d.AddDays(lead + 7), rest));
			}
			else {
				order.Deliveries.Add((d.AddDays(lead), lines.ToDictionary(x => x.Sku.Index, x => x.Su)));
			}
			openHachu.Add(order);
		}

		/// <summary>入荷予定日の発注を本社倉庫へ仕入計上する（RelateNo1=発注Id）。全SKU入荷した発注は完了にする。</summary>
		void ReceiveDeliveries(DateOnly d) {
			foreach (var order in openHachu.ToList()) {
				foreach (var (_, qty) in order.Deliveries.Where(x => x.Day == d)) {
					var lines = qty.Select(x => (Sku: skus[x.Key], Su: x.Value)).OrderBy(x => x.Sku.Index).ToList();
					CreateShiire(d, order.Supplier, lines, order.Den.Id, EnumShiire.Shiire, "");
					foreach (var (sku, su) in lines) order.Remaining[sku.Index] -= su;
				}
				if (order.Remaining.Values.All(x => x <= 0)) {
					order.Den.EndFlag = 1;
					db.Update(order.Den);
					openHachu.Remove(order);
				}
			}
		}

		void CreateShiire(DateOnly d, Supplier sup, List<(Sku Sku, int Su)> lines, long relate, EnumShiire kubun, string memo) {
			var den = new Tran03Shiire {
				DenDay = Ymd(d), KakeDay = Ymd(d), Kubun = (int)kubun, IsPay = 1, IsStock = 1, RelateNo1 = relate,
				Id_Shiire = sup.M.Id, VShiire = V(sup.M), Id_Soko = honsha.Id, VSoko = V(honsha), Id_Shain = buyer.Id, VShain = V(buyer),
				Rate = sup.M.RateProper, TaxCalcUnit = sup.M.TaxCalcUnit, TaxRounding = sup.M.TaxRounding,
				ManualNo = $"{sup.M.Code}-{++manualSeq:00000}", Memo = memo,
			};
			var meisai = lines.Select((x, i) => Line(i + 1, x.Sku, x.Su, x.Sku.P.M.TankaGenka, taxable: true)).ToList();
			foreach (var (sku, su) in lines) Move(honsha, sku, den.CalcFlag * su);
			Finish(den, meisai, (EnumTaxCalcUnit)den.TaxCalcUnit, (EnumRounding)den.TaxRounding, d, 16 * 60);
			purchaseLedger.Add((sup.M.Id, d, den.CalcFlag * den.Total));
		}

		/// <summary>展示会受注（8/18〜8/21）。秋冬・定番の品番をSKU展開で受け、納品予定日ごとに伝票を分ける。</summary>
		void TakeJuchu(DateOnly d) {
			var dayIndex = d.DayNumber - Exhibition.DayNumber;
			for (var i = 0; i < wholesale.Count; i++) {
				if (i % 4 != dayIndex) continue;
				var c = wholesale[i];
				var candidates = products.Where(p => p.Season != 'S' && c.Brands.Contains(p.Brand)).ToList();
				var picks = PickDistinct(candidates, p => p.Popularity, rng.Next(6, 10));
				var lines = new List<(Sku Sku, int Su, DateOnly Nouhin)>();
				foreach (var p in picks) {
					DateOnly[] options = p.Season switch {
						'W' => [new(2026, 10, 10), new(2026, 10, 25), new(2026, 11, 10), new(2026, 11, 20)],
						_ => [new(2026, 9, 10), new(2026, 9, 25), new(2026, 10, 10)],
					};
					var nouhin = options[rng.Next(options.Length)];
					var basis = rng.Next(16, 37) * c.Scale;
					foreach (var k in p.Skus) lines.Add((k, Math.Max(1, (int)Math.Round(basis * k.Share)), nouhin));
				}
				foreach (var group in lines.GroupBy(x => x.Nouhin).OrderBy(x => x.Key)) {
					foreach (var chunk in Chunk(group.Select(x => (x.Sku, x.Su)))) CreateJuchu(d, c, chunk, group.Key);
				}
			}
		}

		void CreateJuchu(DateOnly d, Wholesale c, List<(Sku Sku, int Su)> lines, DateOnly nouhin) {
			var den = new Tran12Jyuchu {
				DenDay = Ymd(d), NouhinDay = Ymd(nouhin), Kubun = (int)EnumJuchu.Juchu,
				Id_Tokui = c.M.Id, VTokui = V(c.M), Id_Soko = honsha.Id, VSoko = V(honsha), Id_Shain = c.Sales.Id, VShain = V(c.Sales),
				Rate = c.M.RateProper, TaxRounding = c.M.TaxRounding, Memo = "2026秋冬 展示会受注",
			};
			var meisai = lines.Select((x, i) => Line(i + 1, x.Sku, x.Su, WholesalePrice(x.Sku, c), taxable: true)).ToList();
			Finish(den, meisai, EnumTaxCalcUnit.Slip, (EnumRounding)c.M.TaxRounding, d, 15 * 60);
			openJuchu.Add(new OpenJuchu { Den = den, Customer = c, Nouhin = nouhin,
				Remaining = lines.ToDictionary(x => x.Sku.Index, x => x.Su), Tanka = lines.ToDictionary(x => x.Sku.Index, x => WholesalePrice(x.Sku, c)) });
		}

		/// <summary>納品予定日の3日前から、本社倉庫の在庫の範囲で受注を出荷する（RelateNo1=受注Id）。全SKU出荷で受注完了。</summary>
		void ShipJuchu(DateOnly d) {
			foreach (var j in openJuchu.ToList()) {
				if (d < j.Nouhin.AddDays(-3) || d < j.LastShip.AddDays(5)) continue;
				var lines = j.Remaining.Where(x => x.Value > 0)
					.Select(x => (Sku: skus[x.Key], Su: Math.Min(x.Value, Stock(honsha, skus[x.Key]))))
					.Where(x => x.Su > 0).OrderBy(x => x.Sku.Index).ToList();
				var remaining = j.Remaining.Values.Sum();
				var shippable = lines.Sum(x => x.Su);
				if (shippable == 0 || (shippable < remaining * 0.6 && d < j.Nouhin.AddDays(3))) continue;
				CreateWholesaleSale(d, j.Customer, [.. lines.Select(x => (x.Sku, x.Su, j.Tanka[x.Sku.Index]))], j.Den.Id, EnumUri00.Uriage, "受注分出荷");
				foreach (var (sku, su) in lines) j.Remaining[sku.Index] -= su;
				j.LastShip = d;
				if (j.Remaining.Values.All(x => x <= 0)) {
					j.Den.EndFlag = 1;
					db.Update(j.Den);
					openJuchu.Remove(j);
				}
			}
		}

		/// <summary>卸先の都度注文（受注を経ない出荷売上）。8〜14日おきに、旬の品番を本社倉庫の在庫の範囲で出荷する。</summary>
		void WholesaleOrders(DateOnly d) {
			foreach (var c in wholesale.Where(x => x.NextOrder <= d)) {
				c.NextOrder = d.AddDays(rng.Next(8, 15));
				var candidates = products.Where(p => c.Brands.Contains(p.Brand) && SeasonWeight(p.Season, d) >= 0.3).ToList();
				var lines = new List<(Sku Sku, int Su, int Tanka)>();
				foreach (var p in PickDistinct(candidates, p => p.Popularity * SeasonWeight(p.Season, d), rng.Next(3, 7))) {
					foreach (var k in PickDistinct(p.Skus, k => k.Share, rng.Next(2, 5))) {
						var su = rng.Next(2, 7);
						if (Stock(honsha, k) >= su) lines.Add((k, su, WholesalePrice(k, c)));
					}
				}
				if (lines.Count == 0) continue;
				foreach (var chunk in Chunk(lines.Select(x => (x.Sku, x.Su)))) {
					CreateWholesaleSale(d, c, [.. chunk.Select(x => (x.Sku, x.Su, WholesalePrice(x.Sku, c)))], 0, EnumUri00.Uriage, "追加注文");
				}
			}
		}

		/// <summary>春夏品の卸返品（区分20、正値で保持し CalcFlag=-1）</summary>
		void WholesaleReturn(DateOnly d) {
			var c = wholesale.Where(x => x.Shipped.Any(s => s.Sku.P.Season == 'S' && s.Su >= 2)).OrderBy(_ => rng.Next()).FirstOrDefault();
			if (c == null) return;
			var lines = c.Shipped.Where(s => s.Sku.P.Season == 'S' && s.Su >= 2).Take(2).Select(s => (s.Sku, Math.Min(2, s.Su), s.Tanka)).ToList();
			CreateWholesaleSale(d, c, lines, 0, EnumUri00.Henpin, "春夏品 シーズン終了による返品");
		}

		void CreateWholesaleSale(DateOnly d, Wholesale c, List<(Sku Sku, int Su, int Tanka)> lines, long relate, EnumUri00 kubun, string memo) {
			var den = new Tran00Uriage {
				DenDay = Ymd(d), KakeDay = Ymd(d), Kubun = (int)kubun, IsPay = 1, RelateNo1 = relate,
				Id_Tokui = c.M.Id, VTokui = V(c.M), Id_Soko = honsha.Id, VSoko = V(honsha), Id_Shain = c.Sales.Id, VShain = V(c.Sales),
				Rate = c.M.RateProper, TaxCalcUnit = c.M.TaxCalcUnit, TaxRounding = c.M.TaxRounding, Memo = memo,
			};
			var meisai = lines.Select((x, i) => Line(i + 1, x.Sku, x.Su, x.Tanka, taxable: true)).ToList();
			foreach (var (sku, su, _) in lines) Move(honsha, sku, -den.CalcFlag * su);
			Finish(den, meisai, (EnumTaxCalcUnit)den.TaxCalcUnit, (EnumRounding)den.TaxRounding, d, 14 * 60);
			salesLedger.Add((c.M.Id, d, den.CalcFlag * den.Total));
			if (den.CalcFlag > 0) {
				foreach (var (sku, su, tanka) in lines) c.Shipped.Add((sku, tanka, su));
			}
		}

		/// <summary>縫製不良の仕入返品（区分20）。本社倉庫に在庫がある春夏品から2SKUを返す。</summary>
		void SupplierReturn(DateOnly d) {
			var lines = skus.Where(k => k.P.Season == 'S' && k.P.Supplier == suppliers[0] && Stock(honsha, k) >= 2).Take(2).Select(k => (k, 2)).ToList();
			if (lines.Count == 0) return;
			CreateShiire(d, suppliers[0], lines, 0, EnumShiire.Henpin, "縫製不良のため返品");
		}

		/// <summary>関西倉庫の補充（毎週月曜、本社倉庫→関西倉庫）。関西2店の10日分の見込み＋陳列分を確保する。</summary>
		void TransferToKansai(DateOnly d) {
			var kansaiShops = shops.Where(s => s.Source == kansai).ToList();
			var lines = new List<(Sku Sku, int Su)>();
			foreach (var k in skus) {
				if (SeasonWeight(k.P.Season, d) < 0.1) continue;
				var demand = 0.0;
				for (var x = d; x < d.AddDays(10); x = x.AddDays(1)) demand += kansaiShops.Sum(s => ShopDemand(s, k, x));
				var need = (int)Math.Ceiling(demand) + kansaiShops.Count - Stock(kansai, k) - kansaiShops.Sum(s => Stock(s.M, k));
				var su = Math.Min(need, Stock(honsha, k));
				if (su > 0) lines.Add((k, su));
			}
			foreach (var chunk in Chunk(lines)) CreateIdo(d, honsha, kansai, chunk, "関西倉庫 補充");
		}

		/// <summary>店舗補充（月曜・木曜、倉庫→店舗）。5日分の見込み×1.3、旬の品番は各SKU1点以上を陳列する。</summary>
		void TransferToShops(DateOnly d) {
			foreach (var shop in shops) {
				var lines = new List<(Sku Sku, int Su)>();
				foreach (var k in skus) {
					if (SeasonWeight(k.P.Season, d) < 0.1) continue;
					var demand = 0.0;
					for (var x = d; x < d.AddDays(5); x = x.AddDays(1)) demand += ShopDemand(shop, k, x);
					var target = Math.Max(1, (int)Math.Ceiling(demand * 1.3));
					var su = Math.Min(target - Stock(shop.M, k), Stock(shop.Source, k));
					if (su > 0) lines.Add((k, su));
				}
				foreach (var chunk in Chunk(lines)) CreateIdo(d, shop.Source, shop.M, chunk, "店舗補充");
			}
		}

		/// <summary>春夏品の店舗在庫を倉庫へ返送する（10/15）。</summary>
		void SeasonEndReturn(DateOnly d) {
			foreach (var shop in shops) {
				var lines = skus.Where(k => k.P.Season == 'S' && Stock(shop.M, k) > 0).Select(k => (k, Stock(shop.M, k))).ToList();
				foreach (var chunk in Chunk(lines)) CreateIdo(d, shop.M, shop.Source, chunk, "春夏品 倉庫返送");
			}
		}

		void CreateIdo(DateOnly d, MasterTokui from, MasterTokui to, List<(Sku Sku, int Su)> lines, string memo) {
			var shain = shops.FirstOrDefault(s => s.M == from || s.M == to)?.Manager ?? buyer;
			var den = new Tran05Ido {
				DenDay = Ymd(d), Id_Soko = from.Id, VSoko = V(from), Id_Ido = to.Id, VIdo = V(to), Id_Shain = shain.Id, VShain = V(shain), Memo = memo,
			};
			// 移動は非課税（明細 Id_Tax=0）。金額は上代で持つ
			var meisai = lines.Select((x, i) => Line(i + 1, x.Sku, x.Su, x.Sku.P.M.TankaJodai, taxable: false)).ToList();
			foreach (var (sku, su) in lines) {
				Move(from, sku, -su);
				Move(to, sku, su);
			}
			Finish(den, meisai, null, EnumRounding.Round, d, 9 * 60);
		}

		/// <summary>店舗売上。各店1日10〜30伝票（週末多め）、季節の売れ筋に沿って店舗在庫の範囲で販売する。返品は約1.5%。</summary>
		void ShopSales(DateOnly d) {
			foreach (var shop in shops) {
				var count = Math.Clamp((int)Math.Round(shop.Base * Dow(d) * (0.85 + rng.NextDouble() * 0.3)), 10, 30);
				for (var i = 0; i < count; i++) {
					var minutes = 10 * 60 + 30 + i * (600 / count) + rng.Next(0, 10);
					if (rng.NextDouble() < 0.015 && TryShopReturn(shop, d, minutes)) continue;
					ShopSale(shop, d, minutes);
				}
				shop.Recent.RemoveAll(x => x.Day < d.AddDays(-14));
			}
		}

		void ShopSale(Shop shop, DateOnly d, int minutes) {
			var lineCount = rng.NextDouble() switch { < 0.6 => 1, < 0.9 => 2, _ => 3 };
			var shain = rng.NextDouble() < 0.4 ? shop.Manager : shop.Clerk;
			var picked = new List<(Sku Sku, int Su, bool Sale)>();
			for (var n = 0; n < lineCount; n++) {
				var k = PickSku(shop, d);
				if (k == null || picked.Any(x => x.Sku == k)) continue;
				var su = rng.NextDouble() < 0.06 && Stock(shop.M, k) >= 2 ? 2 : 1;
				var sale = k.P.Season == 'S' && d >= SaleFrom && d <= SaleTo;
				picked.Add((k, su, sale));
			}
			if (picked.Count == 0) return;
			var meisai = picked.Select((x, i) => Line(i + 1, x.Sku, x.Su, x.Sale ? Floor10(x.Sku.P.M.TankaJodai * 0.7) : x.Sku.P.M.TankaJodai,
				taxable: true, kubun: x.Sale ? 1 : 0, shain: shain)).ToList();
			var member = rng.NextDouble() < 0.35 ? shop.Members[rng.Next(shop.Members.Count)] : null;
			var den = NewTenuri(shop, d, picked.Any(x => x.Sale) ? EnumUri01.UriSale : EnumUri01.Uriage, shain, member, "");
			foreach (var (sku, su, _) in picked) Move(shop.M, sku, -su);
			Finish(den, meisai, EnumTaxCalcUnit.Slip, (EnumRounding)shop.M.TaxRounding, d, minutes);
			foreach (var line in meisai) shop.Recent.Add((den, line, d));
			AddActual(shop, den, d);
		}

		/// <summary>14日以内の販売から1点を返品する（区分20、セール品は21）。数量・金額は正値で持つ。</summary>
		bool TryShopReturn(Shop shop, DateOnly d, int minutes) {
			if (shop.Recent.Count == 0) return false;
			var index = rng.Next(shop.Recent.Count);
			var (original, line, _) = shop.Recent[index];
			shop.Recent.RemoveAt(index);
			var k = skus.First(x => x.P.M.Id == line.Id_Shohin && x.Col.Id == line.Id_Col && x.Siz.Id == line.Id_Siz);
			var shain = shop.Manager;
			var member = shop.Members.FirstOrDefault(x => x.Id == original.Id_Customer);
			var den = NewTenuri(shop, d, line.Kubun == 1 ? EnumUri01.HenSale : EnumUri01.Henpin, shain, member,
				rng.NextDouble() < 0.5 ? "サイズ違いのため返品" : "イメージ違いのため返品");
			den.RelateNo1 = original.Id;
			var meisai = new List<Tran99Meisai> { Line(1, k, 1, line.Tanka, taxable: true, kubun: line.Kubun, shain: shain) };
			Move(shop.M, k, 1);
			Finish(den, meisai, EnumTaxCalcUnit.Slip, (EnumRounding)shop.M.TaxRounding, d, minutes);
			AddActual(shop, den, d);
			return true;
		}

		Tran01Tenuri NewTenuri(Shop shop, DateOnly d, EnumUri01 kubun, MasterShain shain, MasterEndCustomer? member, string memo) => new() {
			DenDay = Ymd(d), Kubun = (int)kubun, Id_Tenpo = shop.M.Id, VTenpo = V(shop.M), Id_Soko = shop.M.Id, VSoko = V(shop.M),
			Id_Shain = shain.Id, VShain = V(shain), Id_Customer = member?.Id ?? 0, VCustomer = member == null ? new() : V(member),
			Code_Customer = member?.Code ?? "", TaxRounding = shop.M.TaxRounding, Memo = memo,
		};

		void AddActual(Shop shop, Tran01Tenuri den, DateOnly d) {
			var ym = d.ToString("yyyyMM", CultureInfo.InvariantCulture);
			foreach (var line in den.Jmeisai!) {
				var brand = products.First(p => p.M.Id == line.Id_Shohin).Brand;
				var key = (shop.M.Id, brand, ym);
				var (sales, gross) = shopBrandActual.GetValueOrDefault(key);
				shopBrandActual[key] = (sales + den.CalcFlag * line.Kingaku, gross + den.CalcFlag * (line.Kingaku - (long)line.Su * line.Gedai));
			}
			var staffKey = (den.Id_Shain, ym);
			var (s, g) = staffActual.GetValueOrDefault(staffKey);
			staffActual[staffKey] = (s + den.CalcFlag * den.KingakuTotal, g + den.CalcFlag * (den.KingakuTotal - den.GedaiTotal));
		}

		Sku? PickSku(Shop shop, DateOnly d) {
			var shares = Shares(shop, d);
			for (var attempt = 0; attempt < 4; attempt++) {
				var p = products[WeightedIndex(shares)];
				var k = p.Skus[WeightedIndex([.. p.Skus.Select(x => x.Share)])];
				if (Stock(shop.M, k) > 0) return k;
				var alternatives = p.Skus.Where(x => Stock(shop.M, x) > 0).ToList();
				if (alternatives.Count > 0 && rng.NextDouble() < 0.5) return alternatives[WeightedIndex([.. alternatives.Select(x => x.Share)])];
			}
			return null;
		}

		// ───────── 入金・支払・予算 ─────────

		/// <summary>締日・回収条件どおりに、入金予定日が11/6以前の請求期間を全額入金する（振込手数料は先方負担として内訳02）。</summary>
		void CreatePayments() {
			var months = new[] { new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1) };
			foreach (var c in wholesale) {
				foreach (var m in months) {
					var (amount, payDay, label) = Due(salesLedger, c.M.Id, c.M.Shime1, c.M.PayMonth, c.M.PayDay, m);
					if (amount <= 0 || payDay > LastDay) continue;
					var fee = Math.Min(c.Fee, (int)amount);
					var den = new Tran06Nyukin { KakeDay = Ymd(payDay), Id_Torisaki = c.M.Id, VTori = V(c.M), Id_Shain = c.Sales.Id, VShain = V(c.Sales),
						KingakuTotal = amount, Memo = $"{label} 請求分", Jmeisai = KinLines(("01", amount - fee), ("02", fee)) };
					StampTran(den, payDay, 11 * 60);
					db.Insert(den);
				}
			}
			foreach (var s in suppliers) {
				foreach (var m in months) {
					var (amount, payDay, label) = Due(purchaseLedger, s.M.Id, s.M.Shime1, s.M.PayMonth, s.M.PayDay, m);
					if (amount <= 0 || payDay > LastDay) continue;
					var den = new Tran07Shiharai { KakeDay = Ymd(payDay), Id_Torisaki = s.M.Id, VTori = V(s.M), Id_Shain = buyer.Id, VShain = V(buyer),
						KingakuTotal = amount, Memo = $"{label} 支払分", Jmeisai = KinLines((s.KinCode, amount)) };
					StampTran(den, payDay, 11 * 60);
					db.Insert(den);
				}
			}
		}

		static (long Amount, DateOnly PayDay, string Label) Due(List<(long Id, DateOnly Day, long Amount)> ledger, long id, int shime, int payMonth, int payDay, DateOnly month) {
			var to = shime == 99 ? month.AddMonths(1).AddDays(-1) : new DateOnly(month.Year, month.Month, shime);
			var from = shime == 99 ? month : new DateOnly(month.Year, month.Month, shime).AddMonths(-1).AddDays(1);
			if (from < FirstDay) from = FirstDay;
			var amount = ledger.Where(x => x.Id == id && x.Day >= from && x.Day <= to).Sum(x => x.Amount);
			var pm = month.AddMonths(payMonth);
			var last = DateTime.DaysInMonth(pm.Year, pm.Month);
			var pay = new DateOnly(pm.Year, pm.Month, payDay == 99 || payDay <= 0 ? last : Math.Min(payDay, last));
			return (amount, pay, $"{to.Month}月{(shime == 99 ? "末" : to.Day + "日")}締");
		}

		List<TranKinMeisai> KinLines(params (string Code, long Amount)[] lines) => [.. lines.Where(x => x.Amount != 0).Select((x, i) =>
			new TranKinMeisai { No = i + 1, Id_Kin = kins[x.Code].Id, Code_Kin = x.Code, Mei_Kin = kins[x.Code].Name, Kingaku = x.Amount })];

		/// <summary>
		/// 店ブランド予算・販売員予算（日別、2026-08〜2027-01）。8〜10月は実績の0.85〜1.15倍、11月以降は10月実績から季節係数で見込む。
		/// 月額を曜日の重みで日へ配分する（月次予算画面は日別行を合計して表示する）。
		/// </summary>
		void CreateBudgets() {
			var months = Enumerable.Range(0, 6).Select(i => new DateOnly(2026, 8, 1).AddMonths(i)).ToArray();
			var factors = new Dictionary<int, double> { [11] = 1.05, [12] = 1.35, [1] = 0.85 };
			var brandRows = 0;
			foreach (var shop in shops) {
				foreach (var brandCode in brands.Keys) {
					var history = months.Take(3).Select(m => shopBrandActual.GetValueOrDefault((shop.M.Id, brandCode, m.ToString("yyyyMM", CultureInfo.InvariantCulture)))).ToList();
					var grossRate = history.Sum(x => x.Sales) > 0 ? (double)history.Sum(x => x.Gross) / history.Sum(x => x.Sales) : 0.58;
					foreach (var m in months) {
						var actual = m.Month is 8 or 9 or 10 ? history[m.Month - 8].Sales : history[2].Sales * factors[m.Month];
						var uri = RoundTo(actual * (0.85 + rng.NextDouble() * 0.30), 10_000);
						var arari = RoundTo(uri * grossRate * (0.97 + rng.NextDouble() * 0.06), 1_000);
						foreach (var (day, u, a) in SplitDaily(m, uri, arari)) {
							var row = new MasterYosanBrand { Id_Tenpo = shop.M.Id, VTenpo = V(shop.M), Id_Brand = brands[brandCode].Id, VBrand = V(brands[brandCode]),
								DenDay = Ymd(day), UriYosan = u, ArariYosan = a };
							StampMaster(row);
							db.Insert(row);
							brandRows++;
						}
					}
				}
			}
			var staffRows = 0;
			foreach (var shain in shops.SelectMany(s => new[] { s.Manager, s.Clerk })) {
				var history = months.Take(3).Select(m => staffActual.GetValueOrDefault((shain.Id, m.ToString("yyyyMM", CultureInfo.InvariantCulture)))).ToList();
				var grossRate = history.Sum(x => x.Sales) > 0 ? (double)history.Sum(x => x.Gross) / history.Sum(x => x.Sales) : 0.58;
				foreach (var m in months) {
					var actual = m.Month is 8 or 9 or 10 ? history[m.Month - 8].Sales : history[2].Sales * factors[m.Month];
					var uri = RoundTo(actual * (0.85 + rng.NextDouble() * 0.30), 10_000);
					var arari = RoundTo(uri * grossRate * (0.97 + rng.NextDouble() * 0.06), 1_000);
					foreach (var (day, u, a) in SplitDaily(m, uri, arari)) {
						var row = new MasterYosanHanbai { Id_Shain = shain.Id, VShain = V(shain), DenDay = Ymd(day), UriYosan = u, ArariYosan = a };
						StampMaster(row);
						db.Insert(row);
						staffRows++;
					}
				}
			}
			trace($"予算: 店ブランド {brandRows} 行 / 販売員 {staffRows} 行（日別、2026-08〜2027-01）");
		}

		static IEnumerable<(DateOnly Day, long Uri, long Arari)> SplitDaily(DateOnly month, long uri, long arari) {
			var days = Enumerable.Range(0, DateTime.DaysInMonth(month.Year, month.Month)).Select(month.AddDays).ToList();
			var total = days.Sum(Dow);
			long uriSum = 0, arariSum = 0;
			for (var i = 0; i < days.Count; i++) {
				var last = i == days.Count - 1;
				var u = last ? uri - uriSum : RoundTo(uri * Dow(days[i]) / total, 100);
				var a = last ? arari - arariSum : RoundTo(arari * Dow(days[i]) / total, 100);
				uriSum += u;
				arariSum += a;
				yield return (days[i], Math.Max(0, u), Math.Max(0, a));
			}
		}

		// ───────── 需要モデル ─────────

		/// <summary>シーズン別の売れ方（8/1を0とした月数で線形補間）。春夏は8月がピーク、秋冬は10〜11月に立ち上がる。</summary>
		static double SeasonWeight(char season, DateOnly d) {
			double[] anchors = season switch {
				'S' => [1.0, 0.75, 0.15, 0.04, 0.02],
				'A' => [0.25, 0.8, 1.0, 0.7, 0.4],
				'W' => [0.03, 0.2, 0.7, 1.0, 1.1],
				_ => [0.8, 0.8, 0.8, 0.8, 0.8],
			};
			var x = Math.Clamp((d.DayNumber - FirstDay.DayNumber) / 30.5, 0, anchors.Length - 1);
			var i = Math.Min((int)x, anchors.Length - 2);
			return anchors[i] + (anchors[i + 1] - anchors[i]) * (x - i);
		}

		static double Dow(DateOnly d) => d.DayOfWeek switch {
			DayOfWeek.Saturday => 1.45, DayOfWeek.Sunday => 1.35, DayOfWeek.Friday => 1.05,
			DayOfWeek.Monday => 0.8, DayOfWeek.Tuesday => 0.8, _ => 0.85,
		};

		double[] Shares(Shop shop, DateOnly d) {
			if (shareCache.TryGetValue((shop.M.Id, d.DayNumber), out var cached)) return cached;
			var raw = products.Select(p => SeasonWeight(p.Season, d) * p.Popularity * shop.Affinity[p.Brand]).ToArray();
			var sum = raw.Sum();
			var shares = raw.Select(x => x / sum).ToArray();
			shareCache[(shop.M.Id, d.DayNumber)] = shares;
			return shares;
		}

		double[] GenericShares(DateOnly d) {
			if (genericShareCache.TryGetValue(d.DayNumber, out var cached)) return cached;
			var raw = products.Select(p => SeasonWeight(p.Season, d) * p.Popularity).ToArray();
			var sum = raw.Sum();
			var shares = raw.Select(x => x / sum).ToArray();
			genericShareCache[d.DayNumber] = shares;
			return shares;
		}

		double ShopDemand(Shop shop, Sku k, DateOnly d) =>
			Math.Clamp(shop.Base * Dow(d), 10, 30) * UnitsPerSlip * Shares(shop, d)[k.P.Index] * k.Share;

		double DailyDemand(Sku k, DateOnly d) =>
			shops.Sum(s => ShopDemand(s, k, d)) + WholesaleDailyUnits * GenericShares(d)[k.P.Index] * k.Share;

		// ───────── 共通 ─────────

		int Stock(MasterTokui soko, Sku k) => stock.GetValueOrDefault((soko.Id, k.Index));

		int TotalStock(Sku k) => stock.Where(x => x.Key.Sku == k.Index).Sum(x => x.Value);

		/// <summary>在庫を増減する。マイナスになる出庫は作らない（作ろうとしたら投入を中止する）。</summary>
		void Move(MasterTokui soko, Sku k, int delta) {
			var value = Stock(soko, k) + delta;
			if (value < 0) throw new InvalidOperationException($"在庫がマイナスになります: {soko.Name} {k.P.M.Code} {k.Col.Name} {k.Siz.Name} {value}");
			stock[(soko.Id, k.Index)] = value;
		}

		static Tran99Meisai Line(int no, Sku k, int su, int tanka, bool taxable, int kubun = 0, MasterShain? shain = null) => new() {
			No = no, Kubun = kubun, Id_Shohin = k.P.M.Id, Code_Shohin = k.P.M.Code, Mei_Shohin = k.P.M.Name, JanCode = k.Jan,
			Id_Col = k.Col.Id, Code_Col = k.Col.Code, Mei_Col = k.Col.Name, Id_Siz = k.Siz.Id, Code_Siz = k.Siz.Code, Mei_Siz = k.Siz.Name,
			Su = su, Tanka = tanka, Kingaku = (long)su * tanka, Jodai = k.P.M.TankaJodai, Gedai = k.P.M.TankaGenka,
			Id_Tax = taxable ? k.P.M.Id_Tax : 0,
			Id_Shain = shain?.Id ?? 0, Code_Shain = shain?.Code ?? "", Mei_Shain = shain?.Name ?? "",
		};

		/// <summary>明細から合計・消費税（入力画面の UpdateHeaderTotals と同じ式）を確定して登録する。</summary>
		void Finish(TranAllHeader den, List<Tran99Meisai> meisai, EnumTaxCalcUnit? unit, EnumRounding rounding, DateOnly d, int minutes) {
			den.Jmeisai = meisai;
			den.SuTotal = meisai.Sum(x => x.Su);
			den.KingakuTotal = meisai.Sum(x => x.Kingaku);
			den.JodaiTotal = meisai.Sum(x => (long)x.Su * x.Jodai);
			den.GedaiTotal = meisai.Sum(x => (long)x.Su * x.Gedai);
			if (den is ITranTax tax && unit != null) {
				var denDay = den.DenDay;
				var totals = TaxCalculator.Apply(meisai, id => TaxRateResolver.ResolveTaxRatePercent(sysman, id, denDay), unit.Value, rounding);
				tax.TaxableAmount1 = totals.TaxableAmount1;
				tax.TaxableAmount2 = totals.TaxableAmount2;
				tax.TaxableAmount3 = totals.TaxableAmount3;
				tax.Tax1 = totals.Tax1;
				tax.Tax2 = totals.Tax2;
				tax.Tax3 = totals.Tax3;
				tax.Total = Math.Abs(den.KingakuTotal) + totals.TaxTotal;
			}
			StampTran(den, d, minutes);
			db.Insert(den);
		}

		/// <summary>品番単位でまとめ、1伝票の明細を MaxLines 行以内に分ける。</summary>
		static List<List<(Sku Sku, int Su)>> Chunk(IEnumerable<(Sku Sku, int Su)> lines) {
			var result = new List<List<(Sku Sku, int Su)>>();
			List<(Sku Sku, int Su)>? current = null;
			foreach (var group in lines.GroupBy(x => x.Sku.P).OrderBy(x => x.Key.M.Code, StringComparer.Ordinal)) {
				var list = group.OrderBy(x => x.Sku.Index).ToList();
				if (current == null || current.Count + list.Count > MaxLines) {
					current = [];
					result.Add(current);
				}
				current.AddRange(list);
			}
			return result;
		}

		List<T> PickDistinct<T>(IReadOnlyList<T> source, Func<T, double> weight, int count) {
			var pool = source.ToList();
			var result = new List<T>();
			while (result.Count < count && pool.Count > 0) {
				var index = WeightedIndex([.. pool.Select(weight)]);
				result.Add(pool[index]);
				pool.RemoveAt(index);
			}
			return result;
		}

		int WeightedIndex(double[] weights) {
			var total = weights.Sum();
			var r = rng.NextDouble() * total;
			for (var i = 0; i < weights.Length; i++) {
				r -= weights[i];
				if (r <= 0) return i;
			}
			return weights.Length - 1;
		}

		static int WholesalePrice(Sku k, Wholesale c) => Floor10(k.P.M.TankaJodai * c.M.RateProper / 100.0);
		static int Floor10(double value) => (int)(Math.Floor(value / 10) * 10);
		static long RoundTo(double value, int unit) => (long)Math.Round(value / unit, MidpointRounding.AwayFromZero) * unit;
		static string Ymd(DateOnly d) => d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		static CodeNameView V(MasterMeisho m) => new(m.Id, m.Code, m.Name);
		static CodeNameView V(MasterShain m) => new(m.Id, m.Code, m.Name);
		static CodeNameView V(MasterTokui m) => new(m.Id, m.Code, m.Name);
		static CodeNameView V(MasterShiire m) => new(m.Id, m.Code, m.Name);
		static CodeNameView V(MasterEndCustomer m) => new(m.Id, m.Code, m.Name);

		/// <summary>マスタの作成・更新日時は運用開始前（2026-07-20）に揃える</summary>
		static void StampMaster(BaseDbClass row) {
			var ticks = new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Local).ToUniversalTime().Ticks;
			row.Vdc = ticks;
			row.Vdu = ticks;
		}

		/// <summary>伝票の作成・更新日時を計上日の入力時刻にする（当日に依存させない）</summary>
		static void StampTran(BaseDbClass row, DateOnly d, int minutes) {
			var ticks = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).AddMinutes(minutes).ToUniversalTime().Ticks;
			row.Vdc = ticks;
			row.Vdu = ticks;
		}

		public void Report() {
			foreach (var table in new[] { "MasterMeisho", "MasterShain", "MasterTokui", "MasterShiire", "MasterShohin", "DerivedShohinColSiz", "MasterEndCustomer",
				"Tran13Hachu", "Tran03Shiire", "Tran05Ido", "Tran12Jyuchu", "Tran00Uriage", "Tran01Tenuri", "Tran06Nyukin", "Tran07Shiharai", "MasterYosanBrand", "MasterYosanHanbai" }) {
				trace($"件数 {table}: {db.ExecuteScalar<long>($"SELECT COUNT(*) FROM {table}")}");
			}
			trace($"在庫（投入時追跡）: 総数 {stock.Values.Sum():N0} 点 / マイナス {stock.Values.Count(x => x < 0)} 件 / 発注残の伝票 {openHachu.Count} 件 / 受注残の伝票 {openJuchu.Count} 件");
		}
	}
}
