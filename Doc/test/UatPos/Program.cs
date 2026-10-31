using System.IO;
using System.Text;
using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfpos.Devices;
using CvWpfpos.Models;
using CvWpfpos.Services;
using CvWpfpos.ViewModels._06Uriage;
using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;
using UatVm;

// POS（CvWpfpos）のVM駆動UAT。設計判断記録 §2.18（POSサーバI/Fの標準CRUD統合）の受入確認。
//
//   UatPos.exe --manage-server --url http://127.0.0.1:5031 [--sqlite <専用DBの絶対パス>]
//
// --sqlite 省略時は out/uatpos-<日時>/uatpos.db に空DBを新規作成する（開発DBは使わない）。
// 認証は使用ガイドライン「UAT前後の認証切替」で解除してからCvServerをビルドしておくこと。

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var url = "http://127.0.0.1:5031";
string? sqlite = null;
var manageServer = false;
for (var i = 0; i < args.Length; i++) {
	switch (args[i]) {
		case "--url": url = args[++i]; break;
		case "--sqlite": sqlite = args[++i]; break;
		case "--manage-server": manageServer = true; break;
		default: Console.Error.WriteLine($"不明な引数: {args[i]}"); return 2;
	}
}

var repoRoot = FindRepoRoot();
var outDir = Path.Combine(repoRoot, "Doc", "test", "UatPos", "out", $"uatpos-{DateTime.Now:yyyyMMdd-HHmmss}");
Directory.CreateDirectory(outDir);
sqlite ??= Path.Combine(outDir, "uatpos.db");
if (Path.GetFileName(sqlite) is "server-user52.db" or "server-user163.db") {
	Console.Error.WriteLine($"開発DB・旧開発DBは使用できません: {sqlite}");
	return 2;
}

var uat = new UatLog(outDir);
uat.Trace($"url={url} sqlite={sqlite} manageServer={manageServer}");
CvServerProcess? server = null;
try {
	if (manageServer) server = CvServerProcess.Start(repoRoot, url, uat.Trace, sqlite, timeoutSeconds: 300);
	await PosScenario.RunAsync(url, uat);
}
catch (Exception ex) {
	uat.Fail("シナリオ実行", ex.ToString());
}
finally {
	server?.Dispose();
	uat.Write();
}
return uat.Failed ? 1 : 0;

static string FindRepoRoot() {
	var dir = new DirectoryInfo(AppContext.BaseDirectory);
	while (dir != null && !File.Exists(Path.Combine(dir.FullName, "creativevision10.slnx"))) dir = dir.Parent;
	return dir?.FullName ?? throw new InvalidOperationException("リポジトリルートが見つかりません。");
}

/// <summary>判定結果と証跡（result.md）を記録する。</summary>
sealed class UatLog(string outDir) {
	private readonly List<(string Name, bool Pass, string Detail)> checks = [];
	private readonly StringBuilder trace = new();
	public string OutDir => outDir;
	public bool Failed => checks.Count == 0 || checks.Any(c => !c.Pass);

	public void Trace(string message) {
		var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
		Console.WriteLine(line);
		trace.AppendLine(line);
	}

	public void Check(string name, object? expected, object? actual) {
		var pass = string.Equals(Convert.ToString(expected), Convert.ToString(actual), StringComparison.Ordinal);
		Record(name, pass, $"期待={expected} 実際={actual}");
	}

	public void IsTrue(string name, bool condition, string detail = "") => Record(name, condition, detail);

	public void Fail(string name, string detail) => Record(name, false, detail);

	private void Record(string name, bool pass, string detail) {
		checks.Add((name, pass, detail));
		Trace($"{(pass ? "PASS" : "FAIL")} {name} {detail}");
	}

	public void Write() {
		var md = new StringBuilder();
		md.AppendLine("# UatPos 結果");
		md.AppendLine();
		md.AppendLine($"- 実行: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		md.AppendLine($"- 判定: {(Failed ? "FAIL" : "PASS")}（{checks.Count(c => c.Pass)}/{checks.Count}）");
		md.AppendLine();
		md.AppendLine("| 判定 | 確認項目 | 詳細 |");
		md.AppendLine("| --- | --- | --- |");
		foreach (var (name, pass, detail) in checks) md.AppendLine($"| {(pass ? "PASS" : "FAIL")} | {name} | {detail.Replace("|", "/").Replace("\r", "").Replace("\n", " ")} |");
		File.WriteAllText(Path.Combine(outDir, "result.md"), md.ToString(), new UTF8Encoding(false));
		File.WriteAllText(Path.Combine(outDir, "trace.log"), trace.ToString(), new UTF8Encoding(false));
	}
}

/// <summary>シード投入と結果照合用の汎用CRUDクライアント（製品のPOSクライアントとは独立）。</summary>
sealed class CoreClient(string url) : IDisposable {
	private readonly GrpcChannel channel = GrpcChannel.ForAddress(url);
	private ICoreService Core => channel.CreateGrpcService<ICoreService>();

	public async Task<List<T>> QueryAsync<T>(string where, params string[] parameters) {
		var reply = await Core.QueryMsgAsync(new CvMsg {
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListParam),
			DataMsg = Common.SerializeObject(new QueryListParam(typeof(T), where, "Id", parameters)),
		});
		if (reply.Code == CvMsgErrorCode.NotFound) return [];
		if (reply.Code < 0) throw new InvalidOperationException($"照会エラー {typeof(T).Name}: {reply.Option} {reply.DataMsg}");
		return Common.DeserializeObject<List<T>>(reply.DataMsg) ?? [];
	}

	public Task<CvMsg> ExecuteAsync(object param) => Core.QueryMsgAsync(new CvMsg {
		Flag = CvFlag.Msg201_Op_Execute,
		DataType = param.GetType(),
		DataMsg = Common.SerializeObject(param),
	});

	public async Task<T> InsertAsync<T>(T item) where T : class => Ok<T>(await ExecuteAsync(new InsertParam(typeof(T), Common.SerializeObject(item))));

	public async Task<T> UpdateAsync<T>(T item) where T : class => Ok<T>(await ExecuteAsync(new UpdateParam(typeof(T), Common.SerializeObject(item))));

	private static T Ok<T>(CvMsg reply) where T : class {
		if (reply.Code < 0) throw new InvalidOperationException($"更新エラー {typeof(T).Name}: {reply.Option}");
		return Common.DeserializeObject(reply.DataMsg, reply.DataType) as T ?? throw new InvalidOperationException("応答を復元できません。");
	}

	public void Dispose() => channel.Dispose();
}

static class PosScenario {
	// 期待値は手計算（店舗端数処理=四捨五入、標準10%・軽減8%・非課税）。
	// 商品A 105円(標準): 税10.5→11 / 商品B 1,000円(軽減): 税80 / 商品C 500円(非課税): 税0
	private const int PriceA = 105, PriceB = 1000, PriceC = 500;

	public static async Task RunAsync(string url, UatLog uat) {
		using var core = new CoreClient(url);
		var today = DateTime.Today.ToString("yyyyMMdd");
		var suffix = DateTime.Now.ToString("HHmmss");

		// ---- シード（汎用CRUD） ----
		var sysman = (await core.QueryAsync<MasterSysman>("Id=@0", "1")).Single();
		sysman.Jsub = [
			new MasterSysTax { Id = 1, DateFrom = "19010101", TaxRate = 10, TaxNewRate = 10 },
			new MasterSysTax { Id = 2, DateFrom = "19010101", TaxRate = 8, TaxNewRate = 8 },
			new MasterSysTax { Id = 3, DateFrom = "19010101", TaxRate = 0, TaxNewRate = 0 },
		];
		await core.UpdateAsync(sysman);
		var store = await core.InsertAsync(new MasterTokui { Code = $"UP{suffix}", Name = "UAT POS店舗", TenType = 6, IsZaiko = 1, TaxRounding = (int)EnumRounding.Round });
		var staff = await core.InsertAsync(new MasterShain { Code = $"US{suffix}", Name = "UAT担当" });
		var productA = await InsertProductAsync(core, $"UA{suffix}", PriceA, 1);
		var productB = await InsertProductAsync(core, $"UB{suffix}", PriceB, 2);
		var productC = await InsertProductAsync(core, $"UC{suffix}", PriceC, 0);
		// 汎用追加はJSONの既定値省略で Id_Tax=0 が既定値1に戻るため、文字列で渡す部分更新で非課税にする
		var taxReply = await core.ExecuteAsync(new PartialUpdateParam(typeof(MasterShohin), [nameof(MasterShohin.Id_Tax)], [new PartialUpdateRow(productC.Id, productC.Vdu, ["0"])]));
		if (taxReply.Code < 0) throw new InvalidOperationException($"非課税商品の設定に失敗: {taxReply.Option}");
		var janA = $"2900{suffix}001"; var janB = $"2900{suffix}002"; var janC = $"2900{suffix}003";
		await core.InsertAsync(new DerivedShohinColSiz { Id_Shohin = productA.Id, Code = productA.Code, Jan1 = janA });
		await core.InsertAsync(new DerivedShohinColSiz { Id_Shohin = productB.Id, Code = productB.Code, Jan1 = janB });
		await core.InsertAsync(new DerivedShohinColSiz { Id_Shohin = productC.Id, Code = productC.Code, Jan1 = janC });
		uat.Trace($"シード 店舗Id={store.Id} 担当Id={staff.Id} 商品A/B/C={productA.Id}/{productB.Id}/{productC.Id}");

		var settings = new PosSettings {
			ServerUrl = url, StoreName = store.Name, StoreId = store.Id, WarehouseId = store.Id, StaffId = staff.Id, StaffCode = staff.Code,
			RegisterNo = "01", DisplayPortName = "COM_UAT_NONE", PrinterPortName = "COM_UAT_NONE",
		};
		using var client = new PosGrpcClient(settings);
		using var peripherals = new PosPeripheralService(settings);
		// MasterShohin をJSONで読み直すと Id_Tax=0 が既定値1に化けるため、POSの商品検索（PosProduct.TaxId）で確認する
		uat.Check("シード: 非課税商品の税区分（商品検索）", 0, (await client.LookupProductAsync(janC, CancellationToken.None))?.TaxId);

		// ---- 1. 売上入力：税区分混在の表示額とサーバ保存額・レシートの一致 ----
		var input = new PosUriageInputViewModel(settings, client, peripherals);
		input.InitCommand.Execute(null);
		foreach (var jan in new[] { janA, janB, janC }) {
			input.BarcodeText = jan;
			await input.ScanBarcodeCommand.ExecuteAsync(null);
		}
		uat.Check("売上入力: 明細数", 3, input.CartLines.Count);
		uat.Check("売上入力: 税抜小計", PriceA + PriceB + PriceC, input.SubTotal);
		uat.Check("売上入力: 消費税（税区分別・四捨五入）", 11 + 80, input.TaxAmount);
		uat.Check("売上入力: ご請求額", 1696, input.TotalAmount);
		input.StartCheckoutCommand.Execute(null);
		input.CashAmount = 2000;
		await input.CompleteCheckoutCommand.ExecuteAsync(null);
		uat.IsTrue("売上入力: 確定メッセージ", input.StatusMessage.Contains("確定"), input.StatusMessage);
		var receipt = input.LastReceipt ?? throw new InvalidOperationException("会計後のレシートがありません: " + input.StatusMessage);
		var sale = (await core.QueryAsync<Tran01Tenuri>("Id=@0", receipt.SaleId.ToString())).Single();
		uat.Check("売上保存: 税込合計", 1696, sale.Total);
		uat.Check("売上保存: 課税対象1/税1", "105/11", $"{sale.TaxableAmount1}/{sale.Tax1}");
		uat.Check("売上保存: 課税対象2/税2", "1000/80", $"{sale.TaxableAmount2}/{sale.Tax2}");
		uat.Check("売上保存: 釣銭", 304, sale.JposPayment.ChangeAmount);
		uat.Check("売上保存: 営業日", today, sale.DenDay);
		uat.Check("売上保存: 店舗V*", store.Code, sale.VTenpo.Cd);
		uat.IsTrue("売上保存: 端末取引ID", !string.IsNullOrEmpty(sale.PosClientSaleId), sale.PosClientSaleId);
		uat.Check("レシート: 合計＝保存値", sale.Total, receipt.TotalAmount);
		uat.Check("レシート: 消費税＝保存値", sale.Tax1 + sale.Tax2 + sale.Tax3, receipt.TaxAmount);
		uat.Check("レシート: 税率別内訳", "10%対象:105:11|8%対象:1000:80|非課税:500:0", string.Join("|", receipt.TaxLines.Select(t => $"{t.Label}:{t.Taxable}:{t.Tax}")));
		uat.Check("レシート: 釣銭＝保存値", sale.JposPayment.ChangeAmount, receipt.ChangeAmount);
		SaveReceiptText(uat, "receipt-sale.txt", receipt);

		// ---- 2. 応答喪失後の再送：同じ端末取引IDは既存売上が返り二重計上しない ----
		var resent = await client.CheckoutAsync(new Tran01Tenuri {
			PosClientSaleId = sale.PosClientSaleId, Kubun = 10, Id_Tenpo = store.Id, Id_Soko = store.Id, Id_Shain = staff.Id,
			Jmeisai = [new Tran99Meisai { Id_Shohin = productA.Id, Su = 1 }], JposPayment = new PosPaymentDetail { CashAmount = 5000 },
		}, CancellationToken.None);
		uat.Check("再送: 既存売上のIdが返る", sale.Id, resent.Id);
		uat.Check("再送: 同じ端末取引IDの伝票数", 1, (await core.QueryAsync<Tran01Tenuri>("PosClientSaleId=@0", sale.PosClientSaleId)).Count);

		// ---- 3. 返品モード ----
		input.ToggleReturnModeCommand.Execute(null);
		input.BarcodeText = janA;
		await input.ScanBarcodeCommand.ExecuteAsync(null);
		input.StartCheckoutCommand.Execute(null);
		await input.CompleteCheckoutCommand.ExecuteAsync(null);
		var returnReceipt = input.LastReceipt!;
		var returned = (await core.QueryAsync<Tran01Tenuri>("Id=@0", returnReceipt.SaleId.ToString())).Single();
		uat.Check("返品: 区分/CalcFlag", "20/-1", $"{returned.Kubun}/{returned.CalcFlag}");
		uat.Check("返品: 税込合計（正値）", 116, returned.Total);
		uat.IsTrue("返品: レシートは返品表示", returnReceipt.IsReturn);
		SaveReceiptText(uat, "receipt-return.txt", returnReceipt);

		// ---- 4. 商品検索：SKU選択はサーバの店舗上代・税区分 ----
		var search = new PosProductSearchViewModel(client);
		search.SelectedProductMaster = productB;
		for (var i = 0; i < 50 && search.Skus.Count == 0; i++) await Task.Delay(100);
		search.SelectedSku = search.Skus.FirstOrDefault(s => s.Jan1 == janB);
		await search.SelectSkuCommand.ExecuteAsync(null);
		uat.Check("商品検索: 単価/税区分/JAN", $"{PriceB}/2/{janB}", $"{search.SelectedProduct?.UnitPrice}/{search.SelectedProduct?.TaxId}/{search.SelectedProduct?.Barcode}");

		// ---- 5. POS以外の売上（事務所入力相当）はサーバ補正を通らない ----
		var office = await core.InsertAsync(new Tran01Tenuri {
			DenDay = today, Kubun = 10, Id_Tenpo = store.Id, Id_Soko = store.Id, Id_Shain = staff.Id,
			Jmeisai = [new Tran99Meisai { No = 1, Id_Shohin = productB.Id, Code_Shohin = productB.Code, Su = 2, Tanka = PriceB, Kingaku = 2 * PriceB }],
			SuTotal = 2, KingakuTotal = 2 * PriceB, Total = 2 * PriceB,
		});
		uat.Check("事務所売上: 送った合計のまま保存", 2000, office.Total);

		// ---- 6. レシート一覧：取消可否・取消・再印字 ----
		var list = new PosReceiptListViewModel(settings, client, peripherals);
		await list.LoadCommand.ExecuteAsync(null);
		uat.IsTrue("レシート一覧: 読込", list.Transactions.Any(t => t.Id == sale.Id) && list.Transactions.Any(t => t.Id == office.Id), list.StatusMessage);
		list.SelectedTransaction = list.Transactions.First(t => t.Id == sale.Id);
		uat.IsTrue("レシート一覧: POS売上は取消可", list.CancelSaleCommand.CanExecute(null));
		list.SelectedTransaction = list.Transactions.First(t => t.Id == office.Id);
		uat.IsTrue("レシート一覧: POS以外の売上は取消不可", !list.CancelSaleCommand.CanExecute(null));
		list.SelectedTransaction = list.Transactions.First(t => t.Id == returned.Id);
		uat.IsTrue("レシート一覧: 返品伝票は取消不可", !list.CancelSaleCommand.CanExecute(null));
		// 取消ボタンは確認ダイアログ（MessageBox）の後に PosGrpcClient.CancelSaleAsync を呼ぶ。ダイアログ以降を同じ呼び出しで確認する
		var cancelled = await client.CancelSaleAsync(sale, staff.Id, CancellationToken.None);
		uat.Check("取消: 区分/合計/税1/税2", "20/1696/11/80", $"{cancelled.Kubun}/{cancelled.Total}/{cancelled.Tax1}/{cancelled.Tax2}");
		uat.Check("取消: 端末取引ID", sale.PosClientSaleId + ":C", cancelled.PosClientSaleId);
		await ExpectErrorAsync(uat, "取消: 二重取消は拒否", "既に取消", () => client.CancelSaleAsync(sale, staff.Id, CancellationToken.None));
		await ExpectErrorAsync(uat, "取消: POS以外の売上は拒否", "POS売上以外", () => client.CancelSaleAsync(office, staff.Id, CancellationToken.None));
		var reprint = ReceiptData.FromSale(sale, settings, "uat");
		uat.Check("再印字: 合計・税・内訳が会計時と一致",
			$"{receipt.TotalAmount}/{receipt.TaxAmount}/{string.Join("|", receipt.TaxLines)}",
			$"{reprint.TotalAmount}/{reprint.TaxAmount}/{string.Join("|", reprint.TaxLines)}");

		// ---- 7. 日次精算：レジ別集計・釣銭控除・二重登録防止（設計判断記録 §2.18 P3/P6-2） ----
		// 別レジ(02)の売上 B×1 現金2,000（税込1,080・釣銭920）。レジ01の精算に含めない
		var settings02 = new PosSettings {
			ServerUrl = url, StoreName = store.Name, StoreId = store.Id, WarehouseId = store.Id, StaffId = staff.Id, StaffCode = staff.Code,
			RegisterNo = "02", DisplayPortName = "COM_UAT_NONE", PrinterPortName = "COM_UAT_NONE",
		};
		using var client02 = new PosGrpcClient(settings02);
		var sale02 = await client02.CheckoutAsync(new Tran01Tenuri {
			PosClientSaleId = Guid.NewGuid().ToString("N"), Kubun = 10, Id_Tenpo = store.Id, Id_Soko = store.Id, Id_Shain = staff.Id,
			Jmeisai = [new Tran99Meisai { Id_Shohin = productB.Id, Su = 1, JanCode = janB, Id_Tax = 2 }],
			JposPayment = new PosPaymentDetail { CashAmount = 2000, RegisterNo = "02" },
		}, CancellationToken.None);
		uat.Check("レジ02売上: 合計/釣銭/レジ", "1080/920/02", $"{sale02.Total}/{sale02.JposPayment.ChangeAmount}/{sale02.JposPayment.RegisterNo}");
		uat.Check("取消: 取消したレジで集計", "01", cancelled.JposPayment.RegisterNo);

		var seisanVm = new PosSeisanViewModel(settings, client);
		await seisanVm.LoadCommand.ExecuteAsync(null);
		uat.IsTrue("精算01: 集計（前回精算0件でもエラーにならない）", seisanVm.StatusMessage.Contains("集計しました"), seisanVm.StatusMessage);
		uat.Check("精算01: 取引件数（自レジの売上・返品・取消のみ）", 3, seisanVm.TransactionCount);
		// 売上 2,000-釣銭304 = 1,696、返品 -116、取消 -(2,000-304) = -1,696
		uat.Check("精算01: 現金売上（釣銭控除・CalcFlag符号付き）", 1696 - 116 - 1696, seisanVm.CashSalesTotal);
		seisanVm.Mai1000 = 2;
		seisanVm.JunbiAmount = 10000;
		var vmCalc = seisanVm.CalcAmount;
		var vmDiff = seisanVm.AmountDiff;
		await seisanVm.SaveSeisanCommand.ExecuteAsync(null);
		uat.IsTrue("精算01: 1回目は保存", !seisanVm.StatusMessage.Contains("エラー"), seisanVm.StatusMessage);
		await seisanVm.SaveSeisanCommand.ExecuteAsync(null);
		uat.IsTrue("精算01: 2回目は二重登録として拒否", seisanVm.StatusMessage.Contains("登録済み"), seisanVm.StatusMessage);

		var seisan02 = new PosSeisanViewModel(settings02, client02);
		await seisan02.LoadCommand.ExecuteAsync(null);
		uat.Check("精算02: 取引件数/現金売上（釣銭控除）", "1/1080", $"{seisan02.TransactionCount}/{seisan02.CashSalesTotal}");
		seisan02.Mai1000 = 1;
		seisan02.Mai50 = 1;
		seisan02.Mai10 = 3;
		await seisan02.SaveSeisanCommand.ExecuteAsync(null);
		uat.IsTrue("精算02: 保存", !seisan02.StatusMessage.Contains("エラー"), seisan02.StatusMessage);

		var seisans = await core.QueryAsync<Tran04PosSeisan>("Id_Tenpo=@0", store.Id.ToString());
		uat.Check("精算保存: レジ別に1件ずつ", "01:1,02:1", string.Join(",", seisans.OrderBy(s => s.RegisterNo).Select(s => $"{s.RegisterNo}:{s.SeisanCnt}")));
		var first = seisans.Single(s => s.RegisterNo == "01");
		uat.Check("精算01保存: 現金実額", 2000, first.RealAmount);
		uat.Check("精算01保存: 計算上の現金＝画面", vmCalc, first.CalcAmount);
		uat.Check("精算01保存: 差額＝画面", vmDiff, first.AmountDiff);
		uat.Check("精算01保存: 店舗V*", store.Code, first.VTenpo.Cd);
		var second = seisans.Single(s => s.RegisterNo == "02");
		uat.Check("精算02保存: 現金実額/計算上の現金/差額", "1080/1080/0", $"{second.RealAmount}/{second.CalcAmount}/{second.AmountDiff}");

		// ---- 8. 在庫：逐次更新の実在庫と、伝票からの独立集計を倉庫×商品×色×サイズで照合 ----
		var slips = await core.QueryAsync<Tran01Tenuri>("Id_Soko=@0", store.Id.ToString());
		var expected = slips.SelectMany(s => (s.Jmeisai ?? []).Select(m => (Key: (m.Id_Shohin, m.Id_Col, m.Id_Siz), Su: -s.CalcFlag * m.Su)))
			.GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Sum(x => x.Su));
		var actual = (await core.QueryAsync<SummaryRealStock>("Id_Soko=@0", store.Id.ToString()))
			.GroupBy(r => (r.Id_Shohin, r.Id_Col, r.Id_Siz)).ToDictionary(g => g.Key, g => g.Sum(r => r.Su));
		foreach (var (key, su) in expected) {
			uat.Check($"実在庫: 商品{key.Id_Shohin}", su, actual.GetValueOrDefault(key));
		}
		uat.Check("実在庫: 商品A=売-1 返+1 取消+1", 1, actual.GetValueOrDefault((productA.Id, 0L, 0L)));
		uat.Check("実在庫: 商品B=売-1 事務所-2 取消+1 レジ02売-1", -3, actual.GetValueOrDefault((productB.Id, 0L, 0L)));

		// ---- 9. 一括追加の迂回防止 ----
		var bulk = await core.ExecuteAsync(new InsertBulkParam(typeof(Tran01Tenuri), Common.SerializeObject(new List<Tran01Tenuri> {
			new() { PosClientSaleId = $"bulk-{suffix}", Kubun = 10, Id_Tenpo = store.Id, Id_Soko = store.Id, Id_Shain = staff.Id, Jmeisai = [new Tran99Meisai { Id_Shohin = productA.Id, Su = 1 }] },
		})));
		uat.IsTrue("一括追加: POS伝票は拒否", bulk.Code < 0 && bulk.Option.Contains("一括追加"), bulk.Option);
	}

	private static Task<MasterShohin> InsertProductAsync(CoreClient core, string code, int price, long idTax) =>
		core.InsertAsync(new MasterShohin { Code = code, Name = $"UAT商品{code}", TankaJodai = price, TankaGenka = price / 2, Id_Tax = idTax, IsZaiko = 1 });

	private static async Task ExpectErrorAsync(UatLog uat, string name, string message, Func<Task> action) {
		try {
			await action();
			uat.Fail(name, "エラーになりませんでした");
		}
		catch (Exception ex) {
			uat.IsTrue(name, ex.Message.Contains(message), ex.Message);
		}
	}

	/// <summary>レシートの印字データ（ESC/POS）を文字だけにして保存する（目視確認用）。</summary>
	private static void SaveReceiptText(UatLog uat, string fileName, ReceiptData receipt) {
		var sjis = Encoding.GetEncoding(932);
		var bytes = ReceiptDocumentBuilder.BuildSalesReceipt(receipt, PosPaperWidth.Mm80, sjis, DateTime.Now);
		var text = new string(sjis.GetString(bytes).Where(c => c == '\n' || c >= ' ').ToArray());
		File.WriteAllText(Path.Combine(uat.OutDir, fileName), text, new UTF8Encoding(false));
		uat.Trace($"レシート文字列を保存: {fileName}");
	}
}
