using System;
using System.Linq;
using System.Threading.Tasks;
using CodeShare;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvServer.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// <see cref="PointOfSaleService"/> のPOS売上・返品・取消が、
/// 商品税区分と店舗端数処理を使って消費税を確定することを検証する。
/// 仕様は `Doc/spec/archive/2026-09-02_R4_POS売上消費税計算_詳細設計.md`。
/// </summary>
[TestClass]
public class PointOfSaleServiceTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;
	private PointOfSaleService? _service;
	private long _storeId;
	private long _warehouseId;
	private long _staffId;

	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");
	private PointOfSaleService Service => _service ?? throw new AssertFailedException("Service not initialized");

	[TestInitialize]
	public void Initialize() {
		var databaseName = $"PointOfSaleServiceTests-{Guid.NewGuid():N}";
		var connectionString = new SqliteConnectionStringBuilder {
			DataSource = databaseName,
			Mode = SqliteOpenMode.Memory,
			Cache = SqliteCacheMode.Shared,
		}.ToString();
		_anchorConnection = new SqliteConnection(connectionString);
		_anchorConnection.Open();
		var conn = new SqliteConnection(connectionString);
		conn.Open();
		_db = new ExDatabaseSqlite(conn) { KeepConnectionAlive = true };

		PrepareTables();
		InsertSysman();
		_storeId = InsertTokui("STORE", "店舗", tenType: 6, taxRounding: EnumRounding.Round);
		_warehouseId = InsertTokui("WAREHOUSE", "倉庫", tenType: 0, taxRounding: EnumRounding.Round);
		_staffId = InsertStaff();
		_service = new PointOfSaleService(Db, NullLogger<PointOfSaleService>.Instance);
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_anchorConnection?.Close();
	}

	[TestMethod]
	public async Task CheckoutAsync_標準軽減非課税混在を税区分別に計算して税込合計を返す() {
		var standardId = InsertProduct("STANDARD", 1000, idTax: 1);
		var reducedId = InsertProduct("REDUCED", 1000, idTax: 2);
		var exemptId = InsertProduct("EXEMPT", 500, idTax: 0);
		var request = Request("mixed-tax", cashAmount: 2700,
			Line(standardId), Line(reducedId), Line(exemptId));

		var response = await Service.CheckoutAsync(request);

		Assert.IsTrue(response.IsSuccess);
		Assert.AreEqual(2680L, response.TotalAmount);
		Assert.AreEqual(20L, response.ChangeAmount);

		var slip = Db.Single<Tran01Tenuri>("where PosClientSaleId=@0", request.ClientSaleId);
		Assert.AreEqual(2500L, slip.KingakuTotal);
		Assert.AreEqual(1000L, slip.TaxableAmount1);
		Assert.AreEqual(1000L, slip.TaxableAmount2);
		Assert.AreEqual(0L, slip.TaxableAmount3);
		Assert.AreEqual(100L, slip.Tax1);
		Assert.AreEqual(80L, slip.Tax2);
		Assert.AreEqual(0L, slip.Tax3);
		Assert.AreEqual(2680L, slip.Total);
		Assert.AreEqual((int)EnumRounding.Round, slip.TaxRounding);
		var meisai = slip.Jmeisai ?? throw new AssertFailedException("POS明細が保存されていません");
		Assert.AreSequenceEqual(new long[] { 1, 2, 0 }, meisai.Select(m => m.Id_Tax).ToArray());
		Assert.AreSequenceEqual(new[] { 10, 8, 0 }, meisai.Select(m => m.TaxRate).ToArray());
		Assert.AreSequenceEqual(new long[] { 100, 80, 0 }, meisai.Select(m => m.Tax).ToArray());
		Assert.AreEqual(180, meisai.Sum(m => m.Tax));
	}

	[TestMethod]
	[DataRow((int)EnumRounding.Round, 116)]
	[DataRow((int)EnumRounding.Ceiling, 116)]
	[DataRow((int)EnumRounding.Floor, 115)]
	public async Task CheckoutAsync_店舗端数処理を伝票へスナップショットする(int rounding, int expectedTotal) {
		var store = Db.Single<MasterTokui>("where Id=@0", _storeId);
		store.TaxRounding = rounding;
		Db.Update(store);
		var productId = InsertProduct($"ROUND{rounding}", 105, idTax: 1);

		var response = await Service.CheckoutAsync(Request($"rounding-{rounding}", 200, Line(productId)));

		Assert.IsTrue(response.IsSuccess);
		var slip = Db.Single<Tran01Tenuri>("where PosClientSaleId=@0", $"rounding-{rounding}");
		Assert.AreEqual(rounding, slip.TaxRounding);
		Assert.AreEqual(expectedTotal, slip.Total);
		Assert.AreEqual(expectedTotal - 105, slip.Tax1);
	}

	[TestMethod]
	public async Task CheckoutAsync_税抜合計以上でも税込合計未満の決済はロールバックする() {
		var productId = InsertProduct("SHORT", 1000, idTax: 1);

		var response = await Service.CheckoutAsync(Request("short-payment", 1000, Line(productId)));

		Assert.IsFalse(response.IsSuccess);
		StringAssert.Contains(response.Message, "不足");
		Assert.AreEqual(0, Db.Fetch<Tran01Tenuri>().Count);
		Assert.AreEqual(0, Db.Fetch<SummaryStock>().Count);
		Assert.AreEqual(0, Db.Fetch<SummaryRealStock>().Count);
	}

	[TestMethod]
	public async Task CheckoutAsync_同一端末取引IDは保存済み税込金額を返して二重登録しない() {
		var productId = InsertProduct("IDEMPOTENT", 1000, idTax: 1);
		var request = Request("same-client-sale", 1200, Line(productId));

		var first = await Service.CheckoutAsync(request);
		var stockAfterFirst = Db.Fetch<SummaryStock>().Single();
		var realStockAfterFirst = Db.Fetch<SummaryRealStock>().Single();
		Assert.AreEqual(-1, stockAfterFirst.Su, "POS売上は月次在庫を1点減らす");
		Assert.AreEqual(1, stockAfterFirst.OutQty, "POS売上は出庫数を1点積む");
		Assert.AreEqual(-1, realStockAfterFirst.Su, "POS売上は実在庫を1点減らす");
		var second = await Service.CheckoutAsync(request with {
			Payment = new PosPayment { CashAmount = 2000 }
		});

		Assert.IsTrue(first.IsSuccess);
		Assert.IsTrue(second.IsSuccess);
		Assert.IsTrue(second.IsDuplicate);
		Assert.AreEqual(first.SaleId, second.SaleId);
		Assert.AreEqual(first.TotalAmount, second.TotalAmount);
		Assert.AreEqual(first.ChangeAmount, second.ChangeAmount);
		Assert.AreEqual(1, Db.Fetch<Tran01Tenuri>().Count);
		var stockAfterSecond = Db.Fetch<SummaryStock>().Single();
		var realStockAfterSecond = Db.Fetch<SummaryRealStock>().Single();
		Assert.AreEqual(stockAfterFirst.Su, stockAfterSecond.Su, "冪等再送で月次在庫を再更新しない");
		Assert.AreEqual(stockAfterFirst.OutQty, stockAfterSecond.OutQty, "冪等再送で出庫数を再更新しない");
		Assert.AreEqual(realStockAfterFirst.Su, realStockAfterSecond.Su, "冪等再送で実在庫を再更新しない");
	}

	[TestMethod]
	public async Task CheckoutAsync_POS返品も税額を正値で保持して返品区分で確定する() {
		var productId = InsertProduct("RETURN", 1000, idTax: 1);
		var request = Request("return-sale", 1100, Line(productId)) with {
			Kubun = (int)EnumUri01.Henpin,
		};

		var response = await Service.CheckoutAsync(request);

		Assert.IsTrue(response.IsSuccess);
		var slip = Db.Single<Tran01Tenuri>("where Id=@0", response.SaleId);
		Assert.AreEqual((int)EnumUri01.Henpin, slip.Kubun);
		Assert.AreEqual(-1, slip.CalcFlag);
		Assert.AreEqual(100L, slip.Tax1);
		Assert.AreEqual(100, slip.Jmeisai!.Single().Tax);
		Assert.AreEqual(1100L, slip.Total);
	}

	/// <summary>
	/// 上代計/下代計は「数量×単価」で積む（<c>Tran99Meisai.Jodai</c>/<c>Gedai</c> は単価であり金額ではない）。
	/// 以前は数量を掛けずに単価をそのまま合計していたため、他6伝票の集計定義と食い違っていた。
	/// </summary>
	[TestMethod]
	public async Task CheckoutAsync_上代計と下代計は数量を掛けて積む() {
		var productId = InsertProduct("QTY", 1000, idTax: 1);
		var request = Request("qty-total", 3300, Line(productId, quantity: 3));

		var response = await Service.CheckoutAsync(request);

		Assert.IsTrue(response.IsSuccess);
		var slip = Db.Single<Tran01Tenuri>("where Id=@0", response.SaleId);
		Assert.AreEqual(3, slip.SuTotal);
		Assert.AreEqual(3000L, slip.KingakuTotal);
		Assert.AreEqual(3000L, slip.JodaiTotal, "上代計 = 数量3 × 上代単価1000");
		Assert.AreEqual(1500L, slip.GedaiTotal, "下代計 = 数量3 × 下代単価500");
	}

	[TestMethod]
	public async Task CancelSaleAsync_税率マスタ変更後も元売上の税スナップショットを継承する() {
		var standardId = InsertProduct("CANCEL1", 1000, idTax: 1);
		var reducedId = InsertProduct("CANCEL2", 1000, idTax: 2);
		var thirdId = InsertProduct("CANCEL3", 1000, idTax: 3);
		var checkout = await Service.CheckoutAsync(Request(
			"cancel-source", 3300, Line(standardId), Line(reducedId), Line(thirdId)));
		var original = Db.Single<Tran01Tenuri>("where Id=@0", checkout.SaleId);
		var sysman = Db.Single<MasterSysman>("where Id=1");
		sysman.Jsub = [new MasterSysTax { Id = 1, TaxRate = 20 }];
		Db.Update(sysman);

		var response = await Service.CancelSaleAsync(new PosCancelSaleRequest {
			SaleId = original.Id,
			StaffId = _staffId,
		});

		Assert.IsTrue(response.IsSuccess);
		var cancelled = Db.Single<Tran01Tenuri>("where Id=@0", response.CancelSaleId);
		Assert.AreEqual((int)EnumUri01.Henpin, cancelled.Kubun);
		Assert.AreEqual(original.TaxRounding, cancelled.TaxRounding);
		Assert.AreEqual(original.TaxableAmount1, cancelled.TaxableAmount1);
		Assert.AreEqual(original.TaxableAmount2, cancelled.TaxableAmount2);
		Assert.AreEqual(original.TaxableAmount3, cancelled.TaxableAmount3);
		Assert.AreEqual(original.Tax1, cancelled.Tax1);
		Assert.AreEqual(original.Tax2, cancelled.Tax2);
		Assert.AreEqual(original.Tax3, cancelled.Tax3);
		Assert.AreEqual(original.Total, cancelled.Total);
		Assert.AreEqual(original.JposPayment.CashAmount, cancelled.JposPayment.CashAmount);
		Assert.AreEqual(original.JposPayment.CardAmount, cancelled.JposPayment.CardAmount);
		Assert.AreEqual(original.JposPayment.OtherAmount, cancelled.JposPayment.OtherAmount);
		Assert.AreEqual(original.JposPayment.ChangeAmount, cancelled.JposPayment.ChangeAmount);
		var originalLines = original.Jmeisai ?? throw new AssertFailedException("元売上明細がありません");
		var cancelledLines = cancelled.Jmeisai ?? throw new AssertFailedException("取消明細がありません");
		Assert.AreSequenceEqual(originalLines.Select(m => m.Id_Tax), cancelledLines.Select(m => m.Id_Tax));
		Assert.AreSequenceEqual(originalLines.Select(m => m.TaxRate), cancelledLines.Select(m => m.TaxRate));
		Assert.AreSequenceEqual(originalLines.Select(m => m.Tax), cancelledLines.Select(m => m.Tax));
		Assert.IsTrue(Db.Fetch<SummaryStock>().All(s => s.Su == 0 && s.OutQty == 0),
			"売上と取消で月次在庫・出庫数が相殺される");
		Assert.IsTrue(Db.Fetch<SummaryRealStock>().All(s => s.Su == 0),
			"売上と取消で実在庫が相殺される");
	}

	[TestMethod]
	public async Task CancelSaleAsync_旧税未設定伝票は取消だけを現在税率で再計算しない() {
		Db.Insert(new Tran01Tenuri {
			DenDay = DateTime.Today.ToString("yyyyMMdd"),
			Kubun = (int)EnumUri01.Uriage,
			Id_Tenpo = _storeId,
			Id_Soko = _warehouseId,
			Id_Shain = _staffId,
			Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = InsertProduct("LEGACY", 1000, 1), Su = 1, Kingaku = 1000 }],
			SuTotal = 1,
			KingakuTotal = 1000,
			Total = 1000,
			PosClientSaleId = "legacy-source",
		});
		var original = Db.Single<Tran01Tenuri>("where PosClientSaleId=@0", "legacy-source");

		var response = await Service.CancelSaleAsync(new PosCancelSaleRequest {
			SaleId = original.Id,
			StaffId = _staffId,
		});

		Assert.IsTrue(response.IsSuccess);
		var cancelled = Db.Single<Tran01Tenuri>("where Id=@0", response.CancelSaleId);
		Assert.AreEqual(0L, cancelled.TaxableAmount1);
		Assert.AreEqual(0L, cancelled.Tax1);
		var cancelledLine = (cancelled.Jmeisai ?? throw new AssertFailedException("取消明細がありません")).Single();
		Assert.AreEqual(0, cancelledLine.TaxRate);
		Assert.AreEqual(0, cancelledLine.Tax);
		Assert.AreEqual(1000L, cancelled.Total);
	}

	private void PrepareTables() {
		foreach (var type in new[] {
			typeof(MasterSysman), typeof(MasterTokui), typeof(MasterShain), typeof(MasterShohin),
			typeof(DerivedJodai), typeof(Tran01Tenuri), typeof(SummaryStock), typeof(SummaryRealStock),
		}) {
			Db.CreateTable(type, true, false);
		}
		Db.Execute("CREATE UNIQUE INDEX SummaryStock_unq1 ON SummaryStock (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
		Db.Execute("CREATE UNIQUE INDEX SummaryRealStock_unq1 ON SummaryRealStock (Id_Soko, Id_Shohin, Id_Col, Id_Siz)");
	}

	private void InsertSysman() => Db.Insert(new MasterSysman {
		Id = 1,
		ShimeBi = 99,
		TaxRounding = (int)EnumRounding.Round,
		Jsub = [
			new MasterSysTax { Id = 1, TaxRate = 8, DateFrom = "20191001", TaxNewRate = 10 },
			new MasterSysTax { Id = 2, TaxRate = 8, DateFrom = "20191001", TaxNewRate = 8 },
			new MasterSysTax { Id = 3, TaxRate = 5 },
		],
	});

	private long InsertTokui(string code, string name, int tenType, EnumRounding taxRounding) {
		var row = new MasterTokui {
			Code = code,
			Name = name,
			TenType = tenType,
			IsZaiko = 1,
			TaxRounding = (int)taxRounding,
		};
		Db.Insert(row);
		return row.Id;
	}

	private long InsertStaff() {
		var row = new MasterShain { Code = "STAFF", Name = "担当者" };
		Db.Insert(row);
		return row.Id;
	}

	private long InsertProduct(string code, int unitPrice, long idTax) {
		var row = new MasterShohin {
			Code = code,
			Name = code,
			TankaJodai = unitPrice,
			TankaGenka = unitPrice / 2,
			Id_Tax = idTax,
			IsZaiko = 1,
		};
		Db.Insert(row);
		return row.Id;
	}

	private static PosCheckoutLine Line(long productId, int quantity = 1) => new() {
		ProductId = productId,
		Quantity = quantity,
	};

	private PosCheckoutRequest Request(string clientSaleId, int cashAmount, params PosCheckoutLine[] lines) => new() {
		ClientSaleId = clientSaleId,
		StoreId = _storeId,
		WarehouseId = _warehouseId,
		StaffId = _staffId,
		Lines = [.. lines],
		Payment = new PosPayment { CashAmount = cashAmount },
		Kubun = (int)EnumUri01.Uriage,
	};
}
