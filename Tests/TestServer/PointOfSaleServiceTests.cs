using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvBaseSqlite;
using CvServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.CvServer;

/// <summary>
/// POS端末の売上・返品・取消・精算が、標準CRUD（Msg201_Op_Execute の InsertParam）経由で
/// <see cref="PointOfSaleService.PrepareInsert"/> のサーバ補正（単価・税・採番）を通ることを検証する。
/// 価格付き商品検索は Msg101_Op_Query の <see cref="PosBarcodeLookupRequest"/> で行う（設計判断記録 §2.18）。
/// </summary>
[TestClass]
public class PointOfSaleServiceTests {
	private ExDatabaseSqlite? _db;
	private SqliteConnection? _anchorConnection;
	private CoreService? _coreService;
	private ServiceProvider? _scopeFactoryProvider;
	private long _storeId;
	private long _warehouseId;
	private long _staffId;

	private ExDatabaseSqlite Db => _db ?? throw new AssertFailedException("Database not initialized");
	private CoreService CoreService => _coreService ?? throw new AssertFailedException("CoreService not initialized");

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
		_scopeFactoryProvider = new ServiceCollection().BuildServiceProvider();
		_coreService = new CoreService(
			NullLogger<CoreService>.Instance,
			new ConfigurationBuilder().AddInMemoryCollection([]).Build(),
			new FakeWebHostEnvironment(),
			new HttpContextAccessor(),
			Db,
			_scopeFactoryProvider.GetRequiredService<IServiceScopeFactory>(),
			new PointOfSaleService(Db, NullLogger<PointOfSaleService>.Instance));
	}

	[TestCleanup]
	public void Cleanup() {
		_db?.Close();
		(_db?.Connection as SqliteConnection)?.Close();
		_anchorConnection?.Close();
		_scopeFactoryProvider?.Dispose();
	}

	[TestMethod]
	public async Task Checkout_標準軽減非課税混在を税区分別に計算して税込合計を返す() {
		var standardId = InsertProduct("STANDARD", 1000, idTax: 1);
		var reducedId = InsertProduct("REDUCED", 1000, idTax: 2);
		var exemptId = InsertProduct("EXEMPT", 500, idTax: 0);
		var request = Sale("mixed-tax", cashAmount: 2700,
			Line(standardId), Line(reducedId), Line(exemptId));

		var response = await InsertOkAsync(request);

		Assert.AreEqual(2680L, response.Total);
		Assert.AreEqual(20, response.JposPayment.ChangeAmount);

		var slip = Db.Single<Tran01Tenuri>("where PosClientSaleId=@0", request.PosClientSaleId);
		Assert.AreEqual(response.Id, slip.Id);
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
	public async Task Checkout_店舗端数処理を伝票へスナップショットする(int rounding, int expectedTotal) {
		var store = Db.Single<MasterTokui>("where Id=@0", _storeId);
		store.TaxRounding = rounding;
		Db.Update(store);
		var productId = InsertProduct($"ROUND{rounding}", 105, idTax: 1);

		await InsertOkAsync(Sale($"rounding-{rounding}", 200, Line(productId)));

		var slip = Db.Single<Tran01Tenuri>("where PosClientSaleId=@0", $"rounding-{rounding}");
		Assert.AreEqual(rounding, slip.TaxRounding);
		Assert.AreEqual(expectedTotal, slip.Total);
		Assert.AreEqual(expectedTotal - 105, slip.Tax1);
	}

	[TestMethod]
	public async Task Checkout_端末が送った単価や合計は使わずサーバの店舗上代と税で確定する() {
		var productId = InsertProduct("TAMPER", 1000, idTax: 1);
		var request = Sale("tamper", 1100, new Tran99Meisai { Id_Shohin = productId, Su = 1, Tanka = 1, Kingaku = 1, Id_Tax = 0 });
		request.Total = 1;
		request.Tax1 = 0;

		var response = await InsertOkAsync(request);

		var line = response.Jmeisai!.Single();
		Assert.AreEqual(1000, line.Tanka);
		Assert.AreEqual(1000L, line.Kingaku);
		Assert.AreEqual(1L, line.Id_Tax);
		Assert.AreEqual(100L, response.Tax1);
		Assert.AreEqual(1100L, response.Total);
		Assert.AreEqual("STORE", response.VTenpo.Cd);
		Assert.AreEqual(DateTime.Today.ToString("yyyyMMdd"), response.DenDay);
	}

	[TestMethod]
	public async Task Checkout_税抜合計以上でも税込合計未満の決済はロールバックする() {
		var productId = InsertProduct("SHORT", 1000, idTax: 1);

		var reply = await InsertAsync(Sale("short-payment", 1000, Line(productId)));

		Assert.IsTrue(reply.Code < 0);
		StringAssert.Contains(reply.Option, "不足");
		Assert.AreEqual(0, Db.Fetch<Tran01Tenuri>().Count);
		Assert.AreEqual(0, Db.Fetch<SummaryStock>().Count);
		Assert.AreEqual(0, Db.Fetch<SummaryRealStock>().Count);
	}

	[TestMethod]
	public async Task Checkout_同一端末取引IDは保存済み税込金額を返して二重登録しない() {
		var productId = InsertProduct("IDEMPOTENT", 1000, idTax: 1);
		var request = Sale("same-client-sale", 1200, Line(productId));

		var first = await InsertOkAsync(request);
		var stockAfterFirst = Db.Fetch<SummaryStock>().Single();
		var realStockAfterFirst = Db.Fetch<SummaryRealStock>().Single();
		Assert.AreEqual(-1, stockAfterFirst.Su, "POS売上は月次在庫を1点減らす");
		Assert.AreEqual(1, stockAfterFirst.OutQty, "POS売上は出庫数を1点積む");
		Assert.AreEqual(-1, realStockAfterFirst.Su, "POS売上は実在庫を1点減らす");
		var resend = Sale("same-client-sale", 2000, Line(productId));
		var second = await InsertOkAsync(resend);

		Assert.AreEqual(first.Id, second.Id);
		Assert.AreEqual(first.Total, second.Total);
		Assert.AreEqual(first.JposPayment.ChangeAmount, second.JposPayment.ChangeAmount);
		Assert.AreEqual(1, Db.Fetch<Tran01Tenuri>().Count);
		var stockAfterSecond = Db.Fetch<SummaryStock>().Single();
		var realStockAfterSecond = Db.Fetch<SummaryRealStock>().Single();
		Assert.AreEqual(stockAfterFirst.Su, stockAfterSecond.Su, "冪等再送で月次在庫を再更新しない");
		Assert.AreEqual(stockAfterFirst.OutQty, stockAfterSecond.OutQty, "冪等再送で出庫数を再更新しない");
		Assert.AreEqual(realStockAfterFirst.Su, realStockAfterSecond.Su, "冪等再送で実在庫を再更新しない");
	}

	[TestMethod]
	public async Task Checkout_社販売上区分14でチェックアウトできる() {
		var productId = InsertProduct("SHAHAN", 1000, idTax: 1);
		var request = Sale("shahan-sale", 1100, Line(productId));
		request.Kubun = (int)EnumUri01.UriShahan;

		var response = await InsertOkAsync(request);

		var slip = Db.Single<Tran01Tenuri>("where Id=@0", response.Id);
		Assert.AreEqual((int)EnumUri01.UriShahan, slip.Kubun);
	}

	[TestMethod]
	public async Task Cancel_社販売上区分14の取消は区分24になる() {
		var productId = InsertProduct("SHAHAN-CANCEL", 1000, idTax: 1);
		var source = Sale("shahan-cancel-source", 1100, Line(productId));
		source.Kubun = (int)EnumUri01.UriShahan;
		var checkout = await InsertOkAsync(source);

		var response = await InsertOkAsync(Cancel(checkout));

		var cancelled = Db.Single<Tran01Tenuri>("where Id=@0", response.Id);
		Assert.AreEqual((int)EnumUri01.HenShahan, cancelled.Kubun);
		Assert.AreEqual("shahan-cancel-source:C", cancelled.PosClientSaleId);
	}

	[TestMethod]
	public async Task Checkout_POS返品も税額を正値で保持して返品区分で確定する() {
		var productId = InsertProduct("RETURN", 1000, idTax: 1);
		var request = Sale("return-sale", 1100, Line(productId));
		request.Kubun = (int)EnumUri01.Henpin;

		var response = await InsertOkAsync(request);

		var slip = Db.Single<Tran01Tenuri>("where Id=@0", response.Id);
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
	public async Task Checkout_上代計と下代計は数量を掛けて積む() {
		var productId = InsertProduct("QTY", 1000, idTax: 1);

		var response = await InsertOkAsync(Sale("qty-total", 3300, Line(productId, quantity: 3)));

		var slip = Db.Single<Tran01Tenuri>("where Id=@0", response.Id);
		Assert.AreEqual(3, slip.SuTotal);
		Assert.AreEqual(3000L, slip.KingakuTotal);
		Assert.AreEqual(3000L, slip.JodaiTotal, "上代計 = 数量3 × 上代単価1000");
		Assert.AreEqual(1500L, slip.GedaiTotal, "下代計 = 数量3 × 下代単価500");
	}

	[TestMethod]
	public async Task Cancel_税率マスタ変更後も元売上の税スナップショットを継承する() {
		var standardId = InsertProduct("CANCEL1", 1000, idTax: 1);
		var reducedId = InsertProduct("CANCEL2", 1000, idTax: 2);
		var thirdId = InsertProduct("CANCEL3", 1000, idTax: 3);
		var checkout = await InsertOkAsync(Sale(
			"cancel-source", 3300, Line(standardId), Line(reducedId), Line(thirdId)));
		var original = Db.Single<Tran01Tenuri>("where Id=@0", checkout.Id);
		var sysman = Db.Single<MasterSysman>("where Id=1");
		sysman.Jsub = [new MasterSysTax { Id = 1, TaxRate = 20 }];
		Db.Update(sysman);

		var response = await InsertOkAsync(Cancel(original));

		var cancelled = Db.Single<Tran01Tenuri>("where Id=@0", response.Id);
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
	public async Task Cancel_旧税未設定伝票は取消だけを現在税率で再計算しない() {
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

		var response = await InsertOkAsync(Cancel(original));

		var cancelled = Db.Single<Tran01Tenuri>("where Id=@0", response.Id);
		Assert.AreEqual(0L, cancelled.TaxableAmount1);
		Assert.AreEqual(0L, cancelled.Tax1);
		var cancelledLine = (cancelled.Jmeisai ?? throw new AssertFailedException("取消明細がありません")).Single();
		Assert.AreEqual(0, cancelledLine.TaxRate);
		Assert.AreEqual(0, cancelledLine.Tax);
		Assert.AreEqual(1000L, cancelled.Total);
	}

	[TestMethod]
	public async Task Cancel_同じ売上の二重取消は拒否する() {
		var productId = InsertProduct("DOUBLE-CANCEL", 1000, idTax: 1);
		var checkout = await InsertOkAsync(Sale("double-cancel", 1100, Line(productId)));
		await InsertOkAsync(Cancel(checkout));

		var reply = await InsertAsync(Cancel(checkout));

		Assert.IsTrue(reply.Code < 0);
		StringAssert.Contains(reply.Option, "既に取消");
		Assert.AreEqual(2, Db.Fetch<Tran01Tenuri>().Count);
	}

	[TestMethod]
	public async Task Cancel_POS以外の売上は取消しない() {
		var reply = await InsertAsync(new Tran01Tenuri { PosClientSaleId = PointOfSaleService.CancelSuffix, Id_Shain = _staffId });

		Assert.IsTrue(reply.Code < 0);
		StringAssert.Contains(reply.Option, "POS売上以外");
		Assert.AreEqual(0, Db.Fetch<Tran01Tenuri>().Count);
	}

	[TestMethod]
	public async Task Cancel_元売上が無いか返品伝票なら拒否する() {
		var missing = await InsertAsync(new Tran01Tenuri { PosClientSaleId = "no-such-sale" + PointOfSaleService.CancelSuffix, Id_Shain = _staffId });
		Assert.IsTrue(missing.Code < 0);
		StringAssert.Contains(missing.Option, "見つかりません");

		var productId = InsertProduct("CANCEL-RETURN", 1000, idTax: 1);
		var source = Sale("return-source", 1100, Line(productId));
		source.Kubun = (int)EnumUri01.Henpin;
		var returned = await InsertOkAsync(source);
		var reply = await InsertAsync(Cancel(returned));

		Assert.IsTrue(reply.Code < 0);
		StringAssert.Contains(reply.Option, "売上伝票のみ");
		Assert.AreEqual(1, Db.Fetch<Tran01Tenuri>().Count);
	}

	/// <summary>端末取引IDの無い店舗売上（事務所入力）は従来どおりサーバで補正しない。</summary>
	[TestMethod]
	public async Task Insert_POS以外の店舗売上は補正しない() {
		var productId = InsertProduct("OFFICE", 1000, idTax: 1);
		var office = new Tran01Tenuri {
			DenDay = "20260901",
			Kubun = (int)EnumUri01.Uriage,
			Id_Tenpo = _storeId,
			Id_Soko = _warehouseId,
			Id_Shain = _staffId,
			Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = productId, Su = 2, Tanka = 900, Kingaku = 1800 }],
			SuTotal = 2,
			KingakuTotal = 1800,
			Total = 1800,
		};

		var response = await InsertOkAsync(office);

		var slip = Db.Single<Tran01Tenuri>("where Id=@0", response.Id);
		Assert.AreEqual("20260901", slip.DenDay);
		Assert.AreEqual(900, slip.Jmeisai!.Single().Tanka);
		Assert.AreEqual(1800L, slip.Total);
		Assert.AreEqual(0L, slip.Tax1);
	}

	[TestMethod]
	public async Task InsertBulk_POS伝票は一括追加できない() {
		var productId = InsertProduct("BULK", 1000, idTax: 1);
		var reply = await CoreService.QueryMsgAsync(new CvMsg {
			Flag = CvFlag.Msg201_Op_Execute,
			DataType = typeof(InsertBulkParam),
			DataMsg = Common.SerializeObject(new InsertBulkParam(typeof(Tran01Tenuri), Common.SerializeObject(new List<Tran01Tenuri> { Sale("bulk", 1100, Line(productId)) }))),
		});

		Assert.IsTrue(reply.Code < 0);
		StringAssert.Contains(reply.Option, "一括追加");
		Assert.AreEqual(0, Db.Fetch<Tran01Tenuri>().Count);
	}

	[TestMethod]
	public async Task Lookup_Msg101でバーコードとSKUキーから店舗上代と税区分を返す() {
		var productId = InsertProduct("LOOKUP", 1200, idTax: 2);
		var sku = new DerivedShohinColSiz { Id_Shohin = productId, Id_Col = 11, Code_Col = "10", Mei_Col = "シロ", Id_Siz = 21, Code_Siz = "M", Mei_Siz = "エム", Jan1 = "4900000000011", Jan2 = "4900000000028" };
		Db.Insert(sku);

		var notFound = await LookupAsync(new PosBarcodeLookupRequest { Barcode = "" });
		Assert.AreEqual(CvMsgErrorCode.NotFound, notFound.Code);
		Assert.AreEqual(CvFlag.Msg101_Op_Query, notFound.Flag);

		var byBarcode = await LookupOkAsync(new PosBarcodeLookupRequest { Barcode = "4900000000028", StoreId = _storeId });
		Assert.AreEqual(productId, byBarcode.ProductId);
		Assert.AreEqual(1200, byBarcode.UnitPrice);
		Assert.AreEqual(2L, byBarcode.TaxId);
		Assert.AreEqual("4900000000028", byBarcode.Barcode);
		Assert.AreEqual(21L, byBarcode.SizeId);

		var bySku = await LookupOkAsync(new PosBarcodeLookupRequest { SkuId = sku.Id, StoreId = _storeId });
		Assert.AreEqual(productId, bySku.ProductId);
		Assert.AreEqual("4900000000011", bySku.Barcode, "SKUキーで引いた場合はJAN1を返す");
	}

	private void PrepareTables() {
		foreach (var type in new[] {
			typeof(MasterSysman), typeof(MasterTokui), typeof(MasterShain), typeof(MasterShohin),
			typeof(DerivedJodai), typeof(DerivedShohinColSiz), typeof(Tran01Tenuri), typeof(SummaryStock), typeof(SummaryRealStock),
			typeof(Tran04PosSeisan), typeof(TranPointEvent),
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

	private static Tran99Meisai Line(long productId, int quantity = 1) => new() {
		Id_Shohin = productId,
		Su = quantity,
	};

	private Tran01Tenuri Sale(string clientSaleId, int cashAmount, params Tran99Meisai[] lines) => new() {
		PosClientSaleId = clientSaleId,
		Kubun = (int)EnumUri01.Uriage,
		Id_Tenpo = _storeId,
		Id_Soko = _warehouseId,
		Id_Shain = _staffId,
		Jmeisai = [.. lines],
		JposPayment = new PosPaymentDetail { CashAmount = cashAmount, RegisterNo = "01" },
	};

	/// <summary>端末の取消要求。端末取引IDと担当者だけを送り、返品伝票の内容はサーバが元売上から作る。</summary>
	private Tran01Tenuri Cancel(Tran01Tenuri original) => new() {
		PosClientSaleId = original.PosClientSaleId + PointOfSaleService.CancelSuffix,
		Id_Shain = _staffId,
		JposPayment = new PosPaymentDetail { RegisterNo = "02" },
	};

	private Tran04PosSeisan Seisan(string registerNo = "01") => new() {
		Id_Tenpo = _storeId,
		RegisterNo = registerNo,
		DenDay = DateTime.Today.ToString("yyyyMMdd"),
		Id_Shain = _staffId,
		KyakuSu = 3,
		Mai10000 = 1,
		Mai1000 = 5,
		JunbiAmount = 5000,
		Jsummary = new PosSeisanSummary {
			CashAmount = 15000,
			TotalAmount = 15000,
			TransactionCount = 3,
			TotalQuantity = 4,
			TaxAmount = 1200,
			GiftCertificateAmount = 500,
			CreditSaleAmount = 300,
			StampCount = 2,
			StampAmount = 400,
		},
	};

	private Task<CvMsg> InsertAsync<T>(T item) => CoreService.QueryMsgAsync(new CvMsg {
		Flag = CvFlag.Msg201_Op_Execute,
		DataType = typeof(InsertParam),
		DataMsg = Common.SerializeObject(new InsertParam(typeof(T), Common.SerializeObject(item!))),
	});

	private async Task<T> InsertOkAsync<T>(T item) where T : class {
		var reply = await InsertAsync(item);
		Assert.AreEqual(0, reply.Code, reply.Option);
		Assert.AreEqual(typeof(T), reply.DataType);
		return Common.DeserializeObject(reply.DataMsg, reply.DataType) as T ?? throw new AssertFailedException("登録応答を復元できません");
	}

	private Task<CvMsg> LookupAsync(PosBarcodeLookupRequest request) => CoreService.QueryMsgAsync(new CvMsg {
		Flag = CvFlag.Msg101_Op_Query,
		DataType = typeof(PosBarcodeLookupRequest),
		DataMsg = Common.SerializeObject(request),
	});

	private async Task<PosProduct> LookupOkAsync(PosBarcodeLookupRequest request) {
		var reply = await LookupAsync(request);
		Assert.AreEqual(0, reply.Code, reply.Option);
		Assert.AreEqual(typeof(PosProduct), reply.DataType);
		return Common.DeserializeObject(reply.DataMsg, reply.DataType) as PosProduct ?? throw new AssertFailedException("検索応答を復元できません");
	}

	[TestMethod]
	public async Task Seisan_レジ番号と集計スナップショットを保存し金額をサーバで算出する() {
		var request = Seisan("01");

		var response = await InsertOkAsync(request);

		var seisan = Db.Single<Tran04PosSeisan>("where Id=@0", response.Id);
		Assert.AreEqual("01", seisan.RegisterNo);
		Assert.AreEqual(1, seisan.SeisanCnt);
		Assert.AreEqual(15000, seisan.RealAmount, "金種 10000×1 + 1000×5");
		Assert.AreEqual(20000, seisan.CalcAmount, "準備金5000 + 現金売上15000");
		Assert.AreEqual(-5000, seisan.AmountDiff);
		Assert.AreEqual("STORE", seisan.VTenpo.Cd);
		Assert.AreEqual("STAFF", seisan.VShain.Cd);
		var summary = seisan.Jsummary ?? throw new AssertFailedException("集計スナップショットが保存されていません");
		var expected = request.Jsummary!;
		Assert.AreEqual(expected.TaxAmount, summary.TaxAmount);
		Assert.AreEqual(expected.GiftCertificateAmount, summary.GiftCertificateAmount);
		Assert.AreEqual(expected.CreditSaleAmount, summary.CreditSaleAmount);
		Assert.AreEqual(expected.StampCount, summary.StampCount);
		Assert.AreEqual(expected.StampAmount, summary.StampAmount);
	}

	[TestMethod]
	public async Task Seisan_レジ番号が異なると連番はそれぞれ1から始まる() {
		var register01 = await InsertOkAsync(Seisan("01"));
		var register02 = await InsertOkAsync(Seisan("02"));

		Assert.AreEqual(1, register01.SeisanCnt);
		Assert.AreEqual(1, register02.SeisanCnt);
	}

	[TestMethod]
	public async Task Seisan_同じ営業日と店舗とレジの2回目は二重登録しない() {
		var first = await InsertOkAsync(Seisan("01"));
		var second = await InsertAsync(Seisan("01"));

		Assert.AreEqual(1, first.SeisanCnt);
		Assert.IsTrue(second.Code < 0);
		StringAssert.Contains(second.Option, "登録済み");
		Assert.AreEqual(1, Db.Fetch<Tran04PosSeisan>().Count);
	}

	[TestMethod]
	public async Task Seisan_事務所画面で登録済みの営業日とレジもPOSから二重登録しない() {
		var office = Seisan("01");
		office.SeisanCnt = 1;
		await InsertOkAsync(office);

		var reply = await InsertAsync(Seisan("01"));

		Assert.IsTrue(reply.Code < 0);
		StringAssert.Contains(reply.Option, "登録済み");
	}

	[TestMethod]
	public async Task Checkout_レジ番号を保存し未指定は拒否する() {
		var productId = InsertProduct("REGISTER", 1000, idTax: 1);
		var saved = await InsertOkAsync(Sale("register-sale", 1100, Line(productId)));
		Assert.AreEqual("01", saved.JposPayment.RegisterNo);

		var noRegister = Sale("no-register", 1100, Line(productId));
		noRegister.JposPayment = new PosPaymentDetail { CashAmount = 1100 };
		var reply = await InsertAsync(noRegister);

		Assert.IsTrue(reply.Code < 0);
		StringAssert.Contains(reply.Option, "レジ番号");
	}

	[TestMethod]
	public async Task Checkout_カードの過入金で現金を超える釣銭は拒否する() {
		var productId = InsertProduct("CARD-OVER", 1000, idTax: 1);
		var request = Sale("card-over", 0, Line(productId));
		request.JposPayment = new PosPaymentDetail { CashAmount = 100, CardAmount = 1500, RegisterNo = "01" };

		var reply = await InsertAsync(request);

		Assert.IsTrue(reply.Code < 0);
		StringAssert.Contains(reply.Option, "合計金額を超えています");
		Assert.AreEqual(0, Db.Fetch<Tran01Tenuri>().Count);
	}

	[TestMethod]
	public async Task Seisan_レジ番号は前後空白を除いて二重登録を判定する() {
		var office = Seisan("01");
		office.SeisanCnt = 1;
		await InsertOkAsync(office);

		var reply = await InsertAsync(Seisan(" 01 "));

		Assert.IsTrue(reply.Code < 0);
		StringAssert.Contains(reply.Option, "登録済み");
		var tooLong = await InsertAsync(Seisan("123456789"));
		StringAssert.Contains(tooLong.Option, "8文字以内");
	}

	[TestMethod]
	public async Task Cancel_取消したレジで集計し決済額は元売上を引き継ぐ() {
		var productId = InsertProduct("CANCEL-REGISTER", 1000, idTax: 1);
		var sale = await InsertOkAsync(Sale("cancel-register", 2000, Line(productId)));

		var cancelled = await InsertOkAsync(Cancel(sale));

		Assert.AreEqual("02", cancelled.JposPayment.RegisterNo);
		Assert.AreEqual(2000, cancelled.JposPayment.CashAmount);
		Assert.AreEqual(900, cancelled.JposPayment.ChangeAmount);
		Assert.AreEqual("01", Db.Single<Tran01Tenuri>("where Id=@0", sale.Id).JposPayment.RegisterNo, "元売上のレジ番号は変えない");

		var noRegister = Cancel(sale);
		noRegister.PosClientSaleId = "cancel-register" + PointOfSaleService.CancelSuffix;
		noRegister.JposPayment = new PosPaymentDetail();
		Db.Delete(Db.Single<Tran01Tenuri>("where Id=@0", cancelled.Id));
		var reply = await InsertAsync(noRegister);
		Assert.IsTrue(reply.Code < 0);
		StringAssert.Contains(reply.Option, "レジ番号");
	}

	[TestMethod]
	public async Task Seisan_レジ番号未指定はエラーになる() {
		var reply = await InsertAsync(Seisan("   "));

		Assert.IsTrue(reply.Code < 0);
		StringAssert.Contains(reply.Option, "レジ番号");
		Assert.AreEqual(0, Db.Fetch<Tran04PosSeisan>().Count);
	}

	/// <summary>事務所の精算メンテ画面（SeisanCnt を1以上で入力）は従来どおりサーバで補正しない（設計判断記録 §2.15）。</summary>
	[TestMethod]
	public async Task Seisan_事務所画面の追加はSeisanCnt指定ありならサーバで補正しない() {
		var request = Seisan("09");
		request.SeisanCnt = 5;
		request.CalcAmount = 12345;
		request.RealAmount = 100;
		request.AmountDiff = -12245;

		var response = await InsertOkAsync(request);

		var seisan = Db.Single<Tran04PosSeisan>("where Id=@0", response.Id);
		Assert.AreEqual(5, seisan.SeisanCnt);
		Assert.AreEqual(12345, seisan.CalcAmount);
		Assert.AreEqual(100, seisan.RealAmount);
		Assert.AreEqual(-12245, seisan.AmountDiff);
		Assert.AreEqual(string.Empty, seisan.VTenpo.Cd ?? string.Empty);
	}
}
