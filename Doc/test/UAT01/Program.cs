using CvBase;
using CvBaseSqlite;
using CvDomainLogic;
using CvServer.Services;
using Microsoft.Data.Sqlite;
using NPoco;
using System.Data;

const string Month = "202609";
const string SupplierCode = "UAT01-SI";
const string WarehouseCode = "UAT01-SK";
const string MakerCode = "UAT01-SI";
const string ProductCode = "UAT01-P01";
const string DayOrderA = "20260901";
const string DayReceiptA1 = "20260905";
const string DayReceiptA2 = "20260910";
const string DayOrderB = "20260911";
const string DayReceiptB1 = "20260912";
const string DayReceiptB2 = "20260913";
const string DayReturn = "20260915";
const string DayPayment = "20260920";

if (args.Length != 1) throw new ArgumentException("usage: Uat01Runner <dbPath>");
var dbPath = Path.GetFullPath(args[0]);
var connectionString = new SqliteConnectionStringBuilder {
    DataSource = dbPath,
    Mode = SqliteOpenMode.ReadWrite,
    Pooling = false,
}.ToString();

using var conn = new SqliteConnection(connectionString);
conn.Open();
var db = new ExDatabaseSqlite(conn) { KeepConnectionAlive = true };
await UpdateDb.WriteVersionInfoAsync(db);
var effects = new WriteEffectRunner(db);
var summary = new SummaryDb(db);

static CodeNameView View(BaseDbClass row, string code, string name) => new(row.Id, code, name);
static int TaxOf(int net, int rate) => (int)Math.Round(net * rate / 100.0, MidpointRounding.AwayFromZero);

void AssertThat(bool condition, string message) {
    if (!condition) throw new InvalidOperationException("ASSERT FAILED: " + message);
    Console.WriteLine("PASS " + message);
}

void InsertWithEffects<T>(T item) where T : BaseDbClass {
    var vdate = DateTime.UtcNow.Ticks;
    item.Vdc = vdate;
    item.Vdu = vdate;
    try {
        db.BeginTransaction(IsolationLevel.Serializable);
        db.Insert(item);
        var result = effects.After(WriteOp.Insert, typeof(T), item, null, vdate);
        db.CompleteTransaction();
        Console.WriteLine($"INSERT {typeof(T).Name} Id={item.Id} effects={result}");
    }
    catch {
        db.AbortTransaction();
        throw;
    }
}

void PartialEndFlag<T>(long id, int flag) where T : BaseDbClass {
    var row = db.Single<T>("where Id=@0", id);
    var vdate = DateTime.UtcNow.Ticks;
    try {
        db.BeginTransaction(IsolationLevel.Serializable);
        var count = db.Execute($"UPDATE {db.GetTableName(typeof(T))} SET EndFlag=@0,Vdu=@1 WHERE Id=@2 AND Vdu=@3", flag, vdate, id, row.Vdu);
        AssertThat(count == 1, $"partial EndFlag update {typeof(T).Name} Id={id} flag={flag}");
        effects.AfterPartialUpdate(typeof(T), [nameof(ITranReserve.EndFlag)], [id]);
        db.CompleteTransaction();
    }
    catch {
        db.AbortTransaction();
        throw;
    }
}

var sys = db.Single<MasterSysman>("where Id=@0", 1);
var taxRate = sys.Jsub?.Where(x => x.Id == 1).OrderByDescending(x => x.DateFrom).FirstOrDefault()?.TaxNewRate ?? 10;
AssertThat(taxRate == 10, "MasterSysman tax1 current new rate is 10 percent");
Console.WriteLine($"DB={dbPath} month={Month} taxRate={taxRate}");
var employee = db.Fetch<MasterShain>("order by Id").First();
var color = db.Fetch<MasterMeisho>("where Kubun=@0 order by Id", "COL").First();
var size = db.Fetch<MasterMeisho>("where Kubun=@0 order by Id", "SIZ").First();
var kinCash = db.Single<MasterMeisho>("where Kubun=@0 and Code=@1", "KIN", "01");

var supplier = db.Fetch<MasterShiire>("where Code=@0", SupplierCode).FirstOrDefault();
if (supplier is null) {
    supplier = new MasterShiire {
        Code = SupplierCode, Name = "UAT01 Supplier", Ryaku = "UAT01 Supplier",
        Id_Shain = employee.Id, VShain = View(employee, employee.Code, employee.Name),
        RateProper = 100, RateSale = 100, Shime1 = 99, PayMonth = 0, PayDay = 0, IsPay = 1,
    };
    InsertWithEffects(supplier);
} else Console.WriteLine($"RESUME MasterShiire Id={supplier.Id}");

var warehouse = db.Fetch<MasterTokui>("where Code=@0", WarehouseCode).FirstOrDefault();
if (warehouse is null) {
    warehouse = new MasterTokui {
        Code = WarehouseCode, Name = "UAT01 Warehouse", Ryaku = "UAT01 Warehouse",
        TenType = 0, IsZaiko = 1, Id_Shain = employee.Id, VShain = View(employee, employee.Code, employee.Name),
    };
    InsertWithEffects(warehouse);
} else Console.WriteLine($"RESUME MasterTokui Id={warehouse.Id}");

var maker = db.Fetch<MasterMeisho>("where Kubun=@0 and Code=@1", "MKR", MakerCode).FirstOrDefault();
if (maker is null) {
    maker = new MasterMeisho {
        Kubun = "MKR", KubunName = "Maker", Code = MakerCode, Name = "UAT01 Maker", Odr = 9001,
    };
    InsertWithEffects(maker);
} else Console.WriteLine($"RESUME MasterMeisho Id={maker.Id}");

var product = db.Fetch<MasterShohin>("where Code=@0", ProductCode).FirstOrDefault();
if (product is null) {
    product = new MasterShohin {
        Code = ProductCode, Name = "UAT01 Product", Id_Maker = maker.Id,
        VMaker = View(maker, maker.Code, maker.Name), Id_Soko = warehouse.Id,
        VSoko = View(warehouse, warehouse.Code, warehouse.Name), Id_Tax = 1, IsZaiko = 1,
        TankaJodaiOrg = 2000, TankaJodai = 2000, TankaGenka = 1000, TankaShiire = 1000,
        Jcolsiz = [new MasterShohinColSiz {
            Id_Col = color.Id, Code_Col = color.Code, Mei_Col = color.Name,
            Id_Siz = size.Id, Code_Siz = size.Code, Mei_Siz = size.Name,
            Jan1 = "UAT01000001",
        }],
    };
    InsertWithEffects(product);
} else Console.WriteLine($"RESUME MasterShohin Id={product.Id}");

var sku = db.Fetch<DerivedShohinColSiz>("where Id_Shohin=@0", product.Id).FirstOrDefault();
var expectedSkuId = product.Id * 100 + 1;
if (sku is null || sku.Id != expectedSkuId || sku.Id_Shohin != product.Id
    || sku.Id_Col != color.Id || sku.Id_Siz != size.Id || sku.Jan1 != "UAT01000001") {
    var derivedId = expectedSkuId;
    db.Execute("DELETE FROM DerivedShohinColSiz WHERE Id=@0", derivedId);
    db.Execute(@"INSERT INTO DerivedShohinColSiz
        (Id,Id_Shohin,RowIdx,Code,Id_Col,Code_Col,Mei_Col,Id_Siz,Code_Siz,Mei_Siz,Jan1,Jan2,Jan3,Vdc,Vdu)
        VALUES (@0,@1,1,@2,@3,@4,@5,@6,@7,@8,@9,'','',@10,@10)",
        derivedId, product.Id, product.Code, color.Id, color.Code, color.Name,
        size.Id, size.Code, size.Name, "UAT01000001", product.Vdc);
    sku = db.Single<DerivedShohinColSiz>("where Id_Shohin=@0", product.Id);
    Console.WriteLine("WARN repaired dedicated derived SKU after production SQL column-order mismatch");
}
if (sku is null) throw new InvalidOperationException("UAT01 derived SKU could not be created");
AssertThat(sku.Jan1 == "UAT01000001", "derived SKU exists");

Tran99Meisai Line(int quantity) => new() {
    No = 1, Kubun = 0, Id_Shohin = product.Id, Code_Shohin = product.Code, Mei_Shohin = product.Name,
    JanCode = sku.Jan1, Id_Col = sku.Id_Col, Code_Col = sku.Code_Col, Mei_Col = sku.Mei_Col,
    Id_Siz = sku.Id_Siz, Code_Siz = sku.Code_Siz, Mei_Siz = sku.Mei_Siz,
    Su = quantity, Tanka = 1000, Kingaku = quantity * 1000,
    Jodai = quantity * 2000, Gedai = quantity * 1000,
};

CodeNameView SupplierView() => new(supplier.Id, supplier.Code, supplier.Name);
CodeNameView WarehouseView() => new(warehouse.Id, warehouse.Code, warehouse.Name);
CodeNameView EmployeeView() => new(employee.Id, employee.Code, employee.Name);

Tran13Hachu Order(string day, int quantity) => new() {
    DenDay = day, NouhinDay = day, Id_Shiire = supplier.Id, VShiire = SupplierView(),
    Id_Soko = warehouse.Id, VSoko = WarehouseView(), Id_Shain = employee.Id, VShain = EmployeeView(),
    Kubun = (int)EnumHachu.Hachu, Rate = 100, Jmeisai = [Line(quantity)],
    SuTotal = quantity, KingakuTotal = quantity * 1000, JodaiTotal = quantity * 2000,
    GedaiTotal = quantity * 1000, Tax = TaxOf(quantity * 1000, taxRate), Total = quantity * 1000,
};

Tran03Shiire Receipt(string day, int quantity, int kind, long relateNo) => new() {
    DenDay = day, KakeDay = day, Id_Shiire = supplier.Id, VShiire = SupplierView(),
    Id_Soko = warehouse.Id, VSoko = WarehouseView(), Id_Shain = employee.Id, VShain = EmployeeView(),
    IsPay = 1, Kubun = kind, RelateNo1 = checked((int)relateNo), Rate = taxRate, Jmeisai = [Line(quantity)],
    SuTotal = quantity, KingakuTotal = quantity * 1000, JodaiTotal = quantity * 2000,
    GedaiTotal = quantity * 1000, Tax = TaxOf(quantity * 1000, taxRate), Total = quantity * 1000,
};

int Remaining(long orderId) => db.Fetch<Tran03Shiire>("where RelateNo1=@0", orderId)
    .SelectMany(x => x.Jmeisai ?? [])
    .Where(x => x.Id_Shohin == product.Id && x.Id_Col == sku.Id_Col && x.Id_Siz == sku.Id_Siz)
    .Sum(x => x.Su) is var used ? db.Single<Tran13Hachu>("where Id=@0", orderId).SuTotal - used : 0;

int Stock() => db.Fetch<SummaryRealStock>("where Id_Soko=@0 and Id_Shohin=@1 and Id_Col=@2 and Id_Siz=@3", warehouse.Id, product.Id, sku.Id_Col, sku.Id_Siz).FirstOrDefault()?.Su ?? 0;

var orderA = Order(DayOrderA, 10);
InsertWithEffects(orderA);
AssertThat(orderA.EndFlag == 0 && Remaining(orderA.Id) == 10, "order A starts with remaining 10");

var receiptA1 = Receipt(DayReceiptA1, 4, (int)EnumShiire.Shiire, orderA.Id);
InsertWithEffects(receiptA1);
orderA = db.Single<Tran13Hachu>("where Id=@0", orderA.Id);
AssertThat(orderA.EndFlag == 0 && Remaining(orderA.Id) == 6 && Stock() == 4, "first receipt leaves order A remaining 6 and stock 4");

var receiptA2 = Receipt(DayReceiptA2, 6, (int)EnumShiire.Shiire, orderA.Id);
InsertWithEffects(receiptA2);
orderA = db.Single<Tran13Hachu>("where Id=@0", orderA.Id);
AssertThat(orderA.EndFlag == 1 && Remaining(orderA.Id) == 0 && Stock() == 10, "second receipt auto-completes order A and stock is 10");

var orderB = Order(DayOrderB, 5);
InsertWithEffects(orderB);
var receiptB1 = Receipt(DayReceiptB1, 3, (int)EnumShiire.Shiire, orderB.Id);
InsertWithEffects(receiptB1);
orderB = db.Single<Tran13Hachu>("where Id=@0", orderB.Id);
AssertThat(orderB.EndFlag == 0 && Remaining(orderB.Id) == 2 && Stock() == 13, "partial receipt leaves order B remaining 2 and stock 13");

PartialEndFlag<Tran13Hachu>(orderB.Id, 1);
orderB = db.Single<Tran13Hachu>("where Id=@0", orderB.Id);
AssertThat(orderB.EndFlag == 1 && Remaining(orderB.Id) == 2, "manual order B completion is recorded with remaining 2");
PartialEndFlag<Tran13Hachu>(orderB.Id, 0);
orderB = db.Single<Tran13Hachu>("where Id=@0", orderB.Id);
AssertThat(orderB.EndFlag == 0, "manual order B completion can be released");

var receiptB2 = Receipt(DayReceiptB2, 2, (int)EnumShiire.Shiire, orderB.Id);
InsertWithEffects(receiptB2);
orderB = db.Single<Tran13Hachu>("where Id=@0", orderB.Id);
AssertThat(orderB.EndFlag == 1 && Remaining(orderB.Id) == 0 && Stock() == 15, "final receipt auto-completes order B and stock is 15");

var returnReceipt = Receipt(DayReturn, 1, (int)EnumShiire.Henpin, 0);
InsertWithEffects(returnReceipt);
AssertThat(returnReceipt.CalcFlag == -1 && Stock() == 14, "purchase return reduces stock to 14");

var purchaseNet = db.Fetch<Tran03Shiire>("where Id_Shiire=@0 and KakeDay like @1", supplier.Id, Month + "%")
    .Where(x => x.Kubun == (int)EnumShiire.Shiire).Sum(x => x.Total);
var returnNet = db.Fetch<Tran03Shiire>("where Id_Shiire=@0 and KakeDay like @1", supplier.Id, Month + "%")
    .Where(x => x.Kubun == (int)EnumShiire.Henpin).Sum(x => x.Total);
var returnTax = db.Fetch<Tran03Shiire>("where Id_Shiire=@0 and KakeDay like @1", supplier.Id, Month + "%")
    .Where(x => x.Kubun == (int)EnumShiire.Henpin).Sum(x => x.Tax * x.CalcFlag);
Console.WriteLine($"purchaseNet={purchaseNet} returnNet={returnNet} returnTaxSigned={returnTax}");

await foreach (var step in summary.SummaryAllAsyncStream(new CalcDateTermParameter(Month, Month))) Console.WriteLine($"REBUILD {step}");
summary.CalcSummaryKaiKake(Month, Month);
summary.CalcSummaryKaiShi(Month, 99, SupplierCode, SupplierCode);

var kakeBeforePayment = db.Single<SummaryKaiKake>("where Id_Shiire=@0 and DenMonth=@1", supplier.Id, Month);
var shiBeforePayment = db.Single<SummaryKaiShi>("where Id_Shiire=@0 and DenDay=@1", supplier.Id, "20260930");
AssertThat(kakeBeforePayment.Shiire == purchaseNet && kakeBeforePayment.Henpin == returnNet
    && kakeBeforePayment.Tax == returnTax + db.Fetch<Tran03Shiire>("where Id_Shiire=@0 and KakeDay like @1", supplier.Id, Month + "%")
        .Where(x => x.Kubun == (int)EnumShiire.Shiire).Sum(x => x.Tax)
    && kakeBeforePayment.TotalOut == 0, "accounts payable reflects purchases, return, tax, and no payment");
AssertThat(shiBeforePayment.TotalOut == 0 && shiBeforePayment.ShiharaiYoteiDay == "20260930", "payment calculation creates due date 20260930");

var payment = new Tran07Shiharai {
    KakeDay = DayPayment, Id_Torisaki = supplier.Id, VTori = SupplierView(),
    Id_Shain = employee.Id, VShain = EmployeeView(), KingakuTotal = 5000,
    Jmeisai = [new TranKinMeisai { No = 1, Id_Kin = kinCash.Id, Code_Kin = kinCash.Code, Mei_Kin = kinCash.Name, Kingaku = 5000 }],
};
InsertWithEffects(payment);
await foreach (var step in summary.SummaryAllAsyncStream(new CalcDateTermParameter(Month, Month))) Console.WriteLine($"REBUILD2 {step}");
summary.CalcSummaryKaiKake(Month, Month);
summary.CalcSummaryKaiShi(Month, 99, SupplierCode, SupplierCode);

var kake = db.Single<SummaryKaiKake>("where Id_Shiire=@0 and DenMonth=@1", supplier.Id, Month);
var shi = db.Single<SummaryKaiShi>("where Id_Shiire=@0 and DenDay=@1", supplier.Id, "20260930");
var expectedTax = db.Fetch<Tran03Shiire>("where Id_Shiire=@0 and KakeDay like @1", supplier.Id, Month + "%")
    .Sum(x => x.Kubun == (int)EnumShiire.Henpin ? x.Tax * x.CalcFlag : x.Tax);
var expectedTotal = purchaseNet - returnNet + expectedTax;
AssertThat(kake.Shiire == purchaseNet && kake.Henpin == returnNet && kake.Tax == expectedTax
    && kake.TotalShiire == expectedTotal && kake.TotalOut == 5000 && kake.Balance == 5000 - expectedTotal,
    "accounts payable reflects the 5000 payment and final balance");
AssertThat(shi.TotalOut == 5000 && shi.TotalShiire == expectedTotal && shi.Balance == 5000 - expectedTotal
    && shi.ShiharaiYoteiDay == "20260930", "payment summary reflects payment and final balance");

var snapshotKake = string.Join("|", new object[] { kake.Shiire, kake.Henpin, kake.Tax, kake.TotalShiire, kake.TotalOut, kake.Balance });
var snapshotShi = string.Join("|", new object[] { shi.TotalShiire, shi.TotalOut, shi.Balance, shi.ShiharaiYoteiDay });
summary.CalcSummaryKaiKake(Month, Month);
summary.CalcSummaryKaiShi(Month, 99, SupplierCode, SupplierCode);
var kake2 = db.Single<SummaryKaiKake>("where Id_Shiire=@0 and DenMonth=@1", supplier.Id, Month);
var shi2 = db.Single<SummaryKaiShi>("where Id_Shiire=@0 and DenDay=@1", supplier.Id, "20260930");
AssertThat(snapshotKake == string.Join("|", new object[] { kake2.Shiire, kake2.Henpin, kake2.Tax, kake2.TotalShiire, kake2.TotalOut, kake2.Balance })
    && snapshotShi == string.Join("|", new object[] { shi2.TotalShiire, shi2.TotalOut, shi2.Balance, shi2.ShiharaiYoteiDay }),
    "accounts payable and payment calculation are idempotent");

Console.WriteLine($"RESULT supplier={supplier.Id}:{supplier.Code} warehouse={warehouse.Id}:{warehouse.Code} maker={maker.Id}:{maker.Code} product={product.Id}:{product.Code} sku={sku.Id}");
Console.WriteLine($"RESULT orderA={orderA.Id} orderB={orderB.Id} receipts={receiptA1.Id},{receiptA2.Id},{receiptB1.Id},{receiptB2.Id} return={returnReceipt.Id} payment={payment.Id}");
Console.WriteLine($"RESULT stock={Stock()} kakeTotal={kake.TotalShiire} kakeOut={kake.TotalOut} kakeBalance={kake.Balance} kaiTotal={shi.TotalShiire} kaiOut={shi.TotalOut} kaiBalance={shi.Balance} due={shi.ShiharaiYoteiDay}");
db.Close();
