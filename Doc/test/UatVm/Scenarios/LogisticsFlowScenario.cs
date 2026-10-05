using System.IO;
using System.Text;
using System.Windows;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.ViewModels._41Logistics;
using CvWpfclient.Views._41Logistics;
using UatVm.Seed;

namespace UatVm.Scenarios;

/// <summary>
/// 物流連携（WMS）の画面からの通し確認。L02 送信 → WMS 応答ファイル作成 → L03 取込・検査・反映 → L04 訂正・除外・送信取消・再出力。
/// <para>
/// 実行は必ず開発DBの複製で行う（<c>--sqlite &lt;複製DB&gt; --manage-server</c>）。シーダーは開発DB本体を指定すると止まる。
/// 連携フォルダは一時フォルダを使い、終了時に画面画像フォルダへ控えを複写してから削除する。
/// </para>
/// <para>仕様は `Doc/spec/2026-10-05_WMS連携_旧AMS連携調査と仮実装仕様.md`（3章 送信、4.3 検査、4.4 反映、5章 ファイル形式）。</para>
/// <list type="bullet">
/// <item>倉庫SK: 仕入20＋発注H1(6)の仕入4＝在庫24。発注H2(5)は未入荷。移動元S2→SKの移動出庫I1(3)は未受入</item>
/// <item>配分（未送信）: 在庫配分 Z1 卸先3・Z2 直営店2・Z3 直営店2・Z4 卸先1、受注配分 J1 卸先4、仕入配分 P1 卸先3（入荷済3）・P2 直営店3（入荷済1）。引当16</item>
/// <item>出荷確定: Z1 2（欠品1）・Z2 2・J1 4・P1 3・P2 5（確定数超過 E12）・存在しない指示ID（E10）。欠品: Z4 全量欠品</item>
/// <item>入荷確定: H1 2・H2 5（仕入）、I1 3（移動受）。棚卸: SK 棚番 A-01 7</item>
/// </list>
/// </summary>
public static class LogisticsFlowScenario {
	const string ScreenDirectory = "..\\Doc\\test\\uat20261005\\logistics\\screens";
	const string DenDay = "20261001";
	const string NouhinDay = "20261003";
	const string WorkDay = "20261005";
	static readonly DateTime TargetDate = new(2026, 10, 5);
	static LogisticsSeeder.Result? _seeded;
	static string? _baseFolder;
	static Encoding? _encoding;

	public static void Seeder(string dbPath) {
		_baseFolder = Path.Combine(Path.GetTempPath(), "cv10-uatvm-logistics-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
		_seeded = LogisticsSeeder.Seed(dbPath, _baseFolder, message => Console.WriteLine($"[seed] {message}"));
	}

	static Encoding Cp932 {
		get {
			if (_encoding == null) {
				Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
				_encoding = Encoding.GetEncoding(932);
			}
			return _encoding;
		}
	}

	public static async Task RunAsync(VmSession session) {
		var s = _seeded ?? throw new InvalidOperationException("シードが実行されていません（--sqlite で複製DBを指定し、--no-seed を付けないこと）。");
		var baseFolder = _baseFolder!;
		var screens = Path.Combine(Path.GetFullPath(ScreenDirectory), "flow_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
		Directory.CreateDirectory(screens);
		session.SetDialogResponder(request => request.Button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel
			? MessageBoxResult.Yes : MessageBoxResult.OK);
		try {
			await RunFlowAsync(session, s, baseFolder, screens);
		}
		finally {
			session.SetDialogResponder(null);
			// 連携フォルダの控え（送受信ファイル）を画面画像フォルダへ複写してから一時フォルダを消す
			try {
				var send = Path.Combine(baseFolder, LogisticsSettings.SendDir);
				if (File.Exists(send)) File.Delete(send);
				if (Directory.Exists(baseFolder)) {
					CopyTree(baseFolder, Path.Combine(screens, "files"));
					Directory.Delete(baseFolder, recursive: true);
				}
				session.Note("後片付け:連携フォルダ削除", new { baseFolder, copiedTo = Path.Combine(screens, "files") });
			}
			catch (Exception ex) {
				session.Fail("後片付け:連携フォルダ削除", ex.ToString());
			}
		}
	}

	static async Task RunFlowAsync(VmSession session, LogisticsSeeder.Result s, string baseFolder, string screens) {
		// ---- 準備（CvServer の登録経路で伝票を作る。在庫・引当・入荷割当は後処理で整合する）----
		await session.InsertAsync(Purchase(s, s.WarehouseId, 0, 20, "20260901"));
		await session.InsertAsync(Purchase(s, s.SourceWarehouseId, 0, 3, "20260901"));
		var h1 = await session.InsertAsync(Hachu(s, 6, "20261003"));
		var h2 = await session.InsertAsync(Hachu(s, 5, "20261004"));
		var p1 = await session.InsertAsync(Haibun(s, EnumHaibun.Hatsukai, s.TokuiId, 3, (int)h1.Id));
		var p2 = await session.InsertAsync(Haibun(s, EnumHaibun.Hatsukai, s.DirectStoreId, 3, (int)h1.Id));
		await session.InsertAsync(Purchase(s, s.WarehouseId, h1.Id, 4, "20261002"));
		var juchu = await session.InsertAsync(new Tran12Jyuchu {
			DenDay = DenDay, NouhinDay = NouhinDay, Id_Tokui = s.TokuiId,
			VTokui = new CodeNameView(s.TokuiId, s.TokuiCode, s.TokuiCode),
			Id_Soko = s.WarehouseId, VSoko = new CodeNameView(s.WarehouseId, s.WarehouseCode, string.Empty),
			Id_Shain = s.EmployeeId, VShain = new CodeNameView(s.EmployeeId, s.EmployeeCode, string.Empty),
			Kubun = (int)EnumJuchu.Juchu, Rate = 100, SuTotal = 4, KingakuTotal = 8000, Jmeisai = [Line(s, 4, 2000)],
		});
		var z1 = await session.InsertAsync(Haibun(s, EnumHaibun.Zaiko, s.TokuiId, 3, 0));
		var z2 = await session.InsertAsync(Haibun(s, EnumHaibun.Zaiko, s.DirectStoreId, 2, 0));
		var z3 = await session.InsertAsync(Haibun(s, EnumHaibun.Zaiko, s.DirectStoreId, 2, 0));
		var z4 = await session.InsertAsync(Haibun(s, EnumHaibun.Zaiko, s.TokuiId, 1, 0));
		var j1 = await session.InsertAsync(Haibun(s, EnumHaibun.Juchu, s.TokuiId, 4, (int)juchu.Id));
		var i1 = await session.InsertAsync(new Tran10IdoOut {
			DenDay = "20261002", Id_Soko = s.SourceWarehouseId, VSoko = new CodeNameView(s.SourceWarehouseId, s.SourceWarehouseCode, string.Empty),
			Id_Ido = s.WarehouseId, VIdo = new CodeNameView(s.WarehouseId, s.WarehouseCode, string.Empty),
			Id_Shain = s.EmployeeId, SuTotal = 3, KingakuTotal = 3000, Jmeisai = [Line(s, 3, 1000)], Memo = "UAT-VM 物流 移動",
		});
		var haibunIds = new Dictionary<string, long> { ["Z1"] = z1.Id, ["Z2"] = z2.Id, ["Z3"] = z3.Id, ["Z4"] = z4.Id, ["J1"] = j1.Id, ["P1"] = p1.Id, ["P2"] = p2.Id };
		var arrived = await HaibunAsync(session, p1.Id, p2.Id);
		session.Check("準備:仕入配分の入荷済み TK3・TS1（店舗コード順）",
			arrived[p1.Id].ArrivedSu == 3 && arrived[p2.Id].ArrivedSu == 1,
			new { p1 = arrived[p1.Id].ArrivedSu, p2 = arrived[p2.Id].ArrivedSu });
		var stock0 = await StockAsync(session, s);
		session.Check("準備:倉庫SK 在庫24・引当16", stock0.Su == 24 && stock0.Reserve == 16, stock0);
		session.Note("準備:伝票", new { h1 = h1.Id, h2 = h2.Id, juchu = juchu.Id, i1 = i1.Id, haibunIds });

		// ======== a. L02 出荷指示 ========
		var l02 = session.OpenView<IntegrationDataManualTransmitView, IntegrationDataManualTransmitViewModel>();
		if (!await l02.WaitAsync("L02:初期化", vm => vm.SokoOptions.Count > 0 && !vm.IsProcessing && vm.StatusMessage.StartsWith("種別", StringComparison.Ordinal))) return;
		session.Check("L02:設定不備なし・対象倉庫はSKだけ", !l02.Vm.HasUnusableReason && l02.Vm.SokoOptions.Count == 1 && l02.Vm.SokoOptions[0].Code == s.WarehouseCode,
			new { l02.Vm.UnusableReason, soko = l02.Vm.SokoOptions.Select(o => o.Code) });
		l02.Input("L02:出荷指示・指定日", vm => { vm.IsOrder = true; vm.TargetDate = TargetDate; }, new { kind = "ORDER", day = WorkDay });
		await RunCommandAsync(session, l02, "L02:出荷指示 対象取得", vm => vm.QueryCommand);
		var orderRefs = l02.Vm.Candidates.Select(c => c.RefId).ToHashSet();
		session.Check("L02:出荷指示の対象7行（在庫4・受注1・仕入2）", orderRefs.SetEquals(haibunIds.Values),
			new { got = orderRefs.OrderBy(x => x), expected = haibunIds.Values.OrderBy(x => x) });
		var p2Row = l02.Vm.Candidates.FirstOrDefault(c => c.RefId == p2.Id);
		var z1Row = l02.Vm.Candidates.FirstOrDefault(c => c.RefId == z1.Id);
		var z2Row = l02.Vm.Candidates.FirstOrDefault(c => c.RefId == z2.Id);
		session.Check("L02:区分 卸先=20出荷売上・直営店=10移動、仕入配分の指示数は入荷済み数",
			z1Row?.Candidate.Kubun == "20" && z2Row?.Candidate.Kubun == "10" && p2Row?.Su == 1,
			new { z1 = z1Row?.KubunText, z2 = z2Row?.KubunText, p2 = p2Row?.Su });
		var z3Row = l02.Vm.Candidates.First(c => c.RefId == z3.Id);
		l02.Input("L02:Z3のチェックを外す", vm => z3Row.IsChecked = false, new { z3 = z3.Id });
		session.Check("L02:選択6件の集計表示", l02.Vm.SummaryText.Contains("選択 6 件"), new { l02.Vm.SummaryText });
		await HaibunScreenScenario.CaptureAsync(session, l02.View, screens, "01_L02_OrderQuery");
		await RunCommandAsync(session, l02, "L02:出荷指示 送信", vm => vm.SendCommand);
		var orderBatch = await LastBatchAsync(session, EnumLogisticsDirection.Send, LogisticsDataKind.ORDER);
		var sentIds = haibunIds.Where(kv => kv.Key != "Z3").Select(kv => kv.Value).ToHashSet();
		session.Check("L02:出荷指示バッチ 配置済み・6行", orderBatch is { Status: (int)EnumLogisticsSendStatus.Placed, RowCount: 6 },
			new { orderBatch?.Id, orderBatch?.Status, orderBatch?.RowCount, orderBatch?.FileName, orderBatch?.Memo });
		var afterSend = await HaibunAsync(session, [.. haibunIds.Values]);
		session.Check("L02:送信した配分は SendFlg=2、外したZ3は0",
			sentIds.All(id => afterSend[id].SendFlg == 2) && afterSend[z3.Id].SendFlg == 0,
			new { flags = afterSend.Values.Select(h => new { h.Id, h.SendFlg }) });
		var orderFile = CheckSendFile(session, baseFolder, orderBatch, LogisticsDataKind.ORDER, "L02:出荷指示");
		var orderLines = orderBatch == null ? [] : await LinesAsync(session, orderBatch.Id);
		var orderById = orderFile.ToDictionary(f => long.Parse(Field(LogisticsDataKind.ORDER, f, "指示ID")));
		session.Check("L02:送信ファイルの指示IDと指示数（Z1 3・Z2 2・Z4 1・J1 4・P1 3・P2 1）",
			orderById.Keys.ToHashSet().SetEquals(sentIds)
			&& Field(LogisticsDataKind.ORDER, orderById.GetValueOrDefault(z1.Id), "指示数") == "3"
			&& Field(LogisticsDataKind.ORDER, orderById.GetValueOrDefault(p2.Id), "指示数") == "1"
			&& Field(LogisticsDataKind.ORDER, orderById.GetValueOrDefault(j1.Id), "元伝票ID") == juchu.Id.ToString()
			&& Field(LogisticsDataKind.ORDER, orderById.GetValueOrDefault(z1.Id), "区分") == "20"
			&& Field(LogisticsDataKind.ORDER, orderById.GetValueOrDefault(z2.Id), "区分") == "10",
			new { rows = orderFile.Select(f => f.RawText) });
		session.Check("L02:送信行 6行・RefTable=TranHaibun・RefId=配分Id・原文=ファイル行",
			orderLines.Count == 6 && orderLines.All(l => l.RefTable == nameof(TranHaibun) && sentIds.Contains(l.RefId))
			&& orderLines.Select(l => l.RawText).SequenceEqual(orderFile.Select(f => f.RawText)),
			new { lines = orderLines.Select(l => new { l.LineNo, l.RefTable, l.RefId, l.Su }) });
		session.Check("L02:再取得で送信済みが消えZ3だけ残る", l02.Vm.Candidates.Count == 1 && l02.Vm.Candidates[0].RefId == z3.Id,
			new { rows = l02.Vm.Candidates.Select(c => c.RefId), l02.Vm.StatusMessage });
		await HaibunScreenScenario.CaptureAsync(session, l02.View, screens, "02_L02_OrderSent");

		// ======== b. L02 入荷予定・在庫 ========
		l02.Input("L02:入荷予定", vm => vm.IsStock = true);
		await RunCommandAsync(session, l02, "L02:入荷予定 対象取得", vm => vm.QueryCommand);
		session.Check("L02:入荷予定 発注H1残2・H2残5・移動I1 3",
			l02.Vm.Candidates.Count == 3
			&& l02.Vm.Candidates.Any(c => c.RefId == h1.Id && c.Su == 2 && c.Candidate.Kubun == "20")
			&& l02.Vm.Candidates.Any(c => c.RefId == h2.Id && c.Su == 5 && c.Candidate.Kubun == "20")
			&& l02.Vm.Candidates.Any(c => c.RefId == i1.Id && c.Su == 3 && c.Candidate.Kubun == "10"),
			new { rows = l02.Vm.Candidates.Select(c => new { c.RefText, c.Su, c.KubunText, c.PartnerText, c.SkuText }) });
		await HaibunScreenScenario.CaptureAsync(session, l02.View, screens, "03_L02_StockQuery");
		await RunCommandAsync(session, l02, "L02:入荷予定 送信", vm => vm.SendCommand);
		var stockBatch = await LastBatchAsync(session, EnumLogisticsDirection.Send, LogisticsDataKind.STOCK);
		session.Check("L02:入荷予定バッチ 配置済み・3行", stockBatch is { Status: (int)EnumLogisticsSendStatus.Placed, RowCount: 3 },
			new { stockBatch?.Id, stockBatch?.Status, stockBatch?.RowCount, stockBatch?.FileName });
		var stockFile = CheckSendFile(session, baseFolder, stockBatch, LogisticsDataKind.STOCK, "L02:入荷予定");
		session.Check("L02:入荷予定ファイル 発注残・取引先・商品コード",
			stockFile.Count == 3
			&& stockFile.Any(f => Field(LogisticsDataKind.STOCK, f, "元伝票ID") == h1.Id.ToString() && Field(LogisticsDataKind.STOCK, f, "予定数") == "2"
				&& Field(LogisticsDataKind.STOCK, f, "取引先CD") == s.ShiireCode && Field(LogisticsDataKind.STOCK, f, "商品CD") == s.ShohinCode)
			&& stockFile.Any(f => Field(LogisticsDataKind.STOCK, f, "元伝票ID") == i1.Id.ToString() && Field(LogisticsDataKind.STOCK, f, "区分") == "10"
				&& Field(LogisticsDataKind.STOCK, f, "取引先CD") == s.SourceWarehouseCode && Field(LogisticsDataKind.STOCK, f, "入荷倉庫CD") == s.WarehouseCode),
			new { rows = stockFile.Select(f => f.RawText) });
		session.Check("L02:入荷予定 再取得で0件（送信済み）", l02.Vm.Candidates.Count == 0, new { rows = l02.Vm.Candidates.Count });

		l02.Input("L02:在庫", vm => vm.IsZaiko = true);
		await RunCommandAsync(session, l02, "L02:在庫 対象取得", vm => vm.QueryCommand);
		var stockNow = await StockAsync(session, s);
		var transit = (await session.QueryAsync<SummaryStock>($"where Id_Soko={s.WarehouseId} AND Id_Shohin={s.ShohinId}")).Sum(x => x.TransitQty);
		var zaikoRow = l02.Vm.Candidates.SingleOrDefault();
		session.Check("L02:在庫 SKの有効在庫8（24−16）・積送中3",
			zaikoRow != null && zaikoRow.Su == 8 && zaikoRow.Candidate.Su2 == 3 && stockNow.Su - stockNow.Reserve == 8 && transit == 3,
			new { rows = l02.Vm.Candidates.Select(c => new { c.SokoText, c.SkuText, c.Su, c.Su2Text }), stockNow, transit });
		await HaibunScreenScenario.CaptureAsync(session, l02.View, screens, "04_L02_ZaikoQuery");
		await RunCommandAsync(session, l02, "L02:在庫 送信", vm => vm.SendCommand);
		var zaikoBatch = await LastBatchAsync(session, EnumLogisticsDirection.Send, LogisticsDataKind.ZAIKO);
		var zaikoFile = CheckSendFile(session, baseFolder, zaikoBatch, LogisticsDataKind.ZAIKO, "L02:在庫");
		session.Check("L02:在庫ファイル 有効在庫8・積送中3",
			zaikoFile.Count == 1 && Field(LogisticsDataKind.ZAIKO, zaikoFile[0], "有効在庫") == "8" && Field(LogisticsDataKind.ZAIKO, zaikoFile[0], "積送中") == "3"
			&& Field(LogisticsDataKind.ZAIKO, zaikoFile[0], "倉庫CD") == s.WarehouseCode,
			new { rows = zaikoFile.Select(f => f.RawText) });
		session.Check("L02:在庫は送信行を保存しない", zaikoBatch != null && (await LinesAsync(session, zaikoBatch.Id)).Count == 0, new { zaikoBatch?.Id });
		var stockAfterZaiko = await StockAsync(session, s);
		session.CheckEqual("L02:在庫送信で在庫・引当は変わらない", stockNow, stockAfterZaiko);

		// ======== c. WMS 応答ファイル ========
		var recv = Path.Combine(baseFolder, LogisticsSettings.ReceiveDir);
		Directory.CreateDirectory(recv);
		string Ord(long id, string header) => Field(LogisticsDataKind.ORDER, orderById.GetValueOrDefault(id), header);
		string?[] Fix(string kind, long id, string kakutei, string ketsu, string memo) => [kind, Ord(id, "区分"), id.ToString(), WorkDay, Ord(id, "倉庫CD"), Ord(id, "出荷先CD"),
			Ord(id, "商品CD"), Ord(id, "色CD"), Ord(id, "サイズCD"), Ord(id, "JAN"), kakutei, ketsu, "INV-" + id, memo];
		const long BogusId = 999_999_999;
		var orderFixText = LogisticsFileFormat.BuildFile(LogisticsDataKind.ORDERFIX, [
			Fix(LogisticsDataKind.ORDERFIX, z1.Id, "2", "1", "一部欠品"),
			Fix(LogisticsDataKind.ORDERFIX, z2.Id, "2", "0", "直営店へ移動"),
			Fix(LogisticsDataKind.ORDERFIX, j1.Id, "4", "0", "受注出荷"),
			Fix(LogisticsDataKind.ORDERFIX, p1.Id, "3", "0", "仕入配分"),
			Fix(LogisticsDataKind.ORDERFIX, p2.Id, "5", "0", "確定数超過(意図的エラー)"),
			[LogisticsDataKind.ORDERFIX, "20", BogusId.ToString(), WorkDay, s.WarehouseCode, s.TokuiCode, s.ShohinCode, s.ColCode, s.SizCode, LogisticsSeeder.JanCode, "1", "0", "", "存在しない指示ID(意図的エラー)"],
		]).Text;
		var lackText = LogisticsFileFormat.BuildFile(LogisticsDataKind.LACK, [Fix(LogisticsDataKind.LACK, z4.Id, "", "1", "全量欠品")]).Text;
		string?[] Fixed(LogisticsCsvLine f) {
			string F(string h) => Field(LogisticsDataKind.STOCK, f, h);
			return [LogisticsDataKind.STOCKFIX, F("区分"), F("元伝票ID"), F("元伝票行No"), WorkDay, F("入荷倉庫CD"), F("取引先CD"),
				F("商品CD"), F("色CD"), F("サイズCD"), F("JAN"), F("予定数"), "WMS入荷"];
		}
		var stockFixText = LogisticsFileFormat.BuildFile(LogisticsDataKind.STOCKFIX, stockFile.Select(Fixed)).Text;
		var inventoryText = LogisticsFileFormat.BuildFile(LogisticsDataKind.INVENTORY, [
			[LogisticsDataKind.INVENTORY, WorkDay, s.WarehouseCode, "A-01", s.ShohinCode, s.ColCode, s.SizCode, LogisticsSeeder.JanCode, "7", "棚卸"],
		]).Text;
		var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
		var orderFixName = $"WMS_ORDERFIX_{stamp}.csv";
		var lackName = $"WMS_LACK_{stamp}.csv";
		var stockFixName = $"WMS_STOCKFIX_{stamp}.csv";
		var inventoryName = $"WMS_INVENTORY_{stamp}.csv";
		File.WriteAllBytes(Path.Combine(recv, orderFixName), Cp932.GetBytes(orderFixText));
		File.WriteAllBytes(Path.Combine(recv, lackName), Cp932.GetBytes(lackText));
		File.WriteAllBytes(Path.Combine(recv, stockFixName), Cp932.GetBytes(stockFixText));
		File.WriteAllBytes(Path.Combine(recv, inventoryName), Cp932.GetBytes(inventoryText));
		session.Note("WMS応答ファイルを配置", new { orderFixText, lackText, stockFixText, inventoryText });

		// ======== d. L03 取込・検査・反映 ========
		var l03 = session.OpenView<IntegrationDataManualReceiveView, IntegrationDataManualReceiveViewModel>();
		if (!await l03.WaitAsync("L03:初期化", vm => !vm.IsProcessing && vm.StatusMessage.StartsWith("取り込むファイル", StringComparison.Ordinal))) return;
		session.Check("L03:受信フォルダの4ファイル・種別判定",
			l03.Vm.ReceiveFiles.Count == 4 && l03.Vm.ReceiveFiles.All(f => f.IsChecked && f.KindName != "(種別不明)"),
			new { files = l03.Vm.ReceiveFiles.Select(f => new { f.FileName, f.KindName }) });
		await RunCommandAsync(session, l03, "L03:取込・検査", vm => vm.ImportCommand);
		var orderFixBatch = await BatchByFileAsync(session, orderFixName);
		var lackBatch = await BatchByFileAsync(session, lackName);
		var stockFixBatch = await BatchByFileAsync(session, stockFixName);
		var inventoryBatch = await BatchByFileAsync(session, inventoryName);
		session.Check("L03:4バッチ 取込済み・受信フォルダは空・recv_bakへ移動",
			new[] { orderFixBatch, lackBatch, stockFixBatch, inventoryBatch }.All(b => b?.Status == (int)EnumLogisticsReceiveStatus.Imported)
			&& Directory.GetFiles(recv).Length == 0 && Directory.GetFiles(Path.Combine(baseFolder, LogisticsSettings.ReceiveBackupDir)).Length == 4
			&& l03.Vm.ReceiveFiles.Count == 0,
			new { orderFix = orderFixBatch?.Id, lack = lackBatch?.Id, stockFix = stockFixBatch?.Id, inventory = inventoryBatch?.Id, l03.Vm.ResultText });
		if (orderFixBatch == null || lackBatch == null || stockFixBatch == null || inventoryBatch == null) return;

		await SelectReceiveBatchAsync(session, l03, orderFixBatch.Id, "L03:出荷確定");
		var fixLines = l03.Vm.Lines.ToList();
		var p2Line = fixLines.FirstOrDefault(l => l.Line.RefId == p2.Id);
		var bogusLine = fixLines.FirstOrDefault(l => l.RawText.Contains(BogusId.ToString(), StringComparison.Ordinal));
		session.Check("L03:出荷確定 6行 未処理4・エラー2（P2 E12 確定数超過、存在しない指示 E10）",
			fixLines.Count == 6 && l03.Vm.PendingCount == 4 && p2Line is { IsError: true, ErrorCode: "E12" } && bogusLine is { IsError: true, ErrorCode: "E10" },
			new { lines = fixLines.Select(l => new { l.LineNo, l.StatusName, l.ErrorCode, l.ErrorMsg, l.RefText, l.Su, l.Su2 }), l03.Vm.LineSummaryText });
		session.Check("L03:行集計の表示", l03.Vm.LineSummaryText.Contains("未処理 4") && l03.Vm.LineSummaryText.Contains("エラー 2"), new { l03.Vm.LineSummaryText });
		await HaibunScreenScenario.CaptureAsync(session, l03.View, screens, "05_L03_OrderFixChecked");

		var before = await StockAsync(session, s);
		await RunCommandAsync(session, l03, "L03:出荷確定 反映", vm => vm.ApplyCommand);
		var hs = await HaibunAsync(session, [.. haibunIds.Values]);
		session.Check("L03:出荷確定 配分の確定（Z1 2/欠品1、Z2 2、J1 4、P1 3、確定日=出荷日）",
			Committed(hs[z1.Id], 2, 1) && Committed(hs[z2.Id], 2, 0) && Committed(hs[j1.Id], 4, 0) && Committed(hs[p1.Id], 3, 0) && hs[p2.Id].EndFlag == 0,
			new { rows = hs.Values.Select(h => new { h.Id, h.EndFlag, h.JitsuSu, h.ShortSu, h.KakuteiDay, h.SendFlg }) });
		var appliedLines = await LinesAsync(session, orderFixBatch.Id);
		var target = appliedLines.Where(l => l.Status == (int)EnumLogisticsLineStatus.Applied).ToDictionary(l => l.RefId);
		session.Check("L03:出荷確定 生成伝票（卸先=出荷売上、直営店=移動出庫）",
			target.Count == 4 && target.GetValueOrDefault(z1.Id)?.TargetTable == nameof(Tran00Uriage) && target.GetValueOrDefault(j1.Id)?.TargetTable == nameof(Tran00Uriage)
			&& target.GetValueOrDefault(p1.Id)?.TargetTable == nameof(Tran00Uriage) && target.GetValueOrDefault(z2.Id)?.TargetTable == nameof(Tran10IdoOut),
			new { lines = appliedLines.Select(l => new { l.RefId, l.Status, l.ErrorCode, l.TargetTable, l.TargetId }) });
		var uriage = await session.QueryAsync<Tran00Uriage>($"where Id_Tokui={s.TokuiId}");
		var idoOut = await session.QueryAsync<Tran10IdoOut>($"where Id_Soko={s.WarehouseId} AND Id_Ido={s.DirectStoreId}");
		session.Check("L03:出荷売上 計9点（Z1 2・J1 4・P1 3）、受注の売上は RelateNo1=受注、移動出庫 2点",
			uriage.Sum(u => u.SuTotal) == 9 && uriage.Any(u => u.RelateNo1 == (int)juchu.Id && u.SuTotal == 4)
			&& uriage.All(u => u.DenDay == WorkDay) && idoOut.Sum(x => x.SuTotal) == 2,
			new { uriage = uriage.Select(u => new { u.Id, u.SuTotal, u.RelateNo1, u.DenDay }), ido = idoOut.Select(x => new { x.Id, x.SuTotal, x.DenDay }) });
		var afterFix = await StockAsync(session, s);
		session.Check("L03:出荷確定 在庫−11・引当−12", afterFix.Su == before.Su - 11 && afterFix.Reserve == before.Reserve - 12, new { before, afterFix });
		var fixBatch1 = await BatchAsync(session, orderFixBatch.Id);
		session.Check("L03:出荷確定バッチ 一部エラー（適用4・エラー2）",
			fixBatch1 is { Status: (int)EnumLogisticsReceiveStatus.PartialError, OkCount: 4, ErrorCount: 2 },
			new { fixBatch1?.Status, fixBatch1?.OkCount, fixBatch1?.ErrorCount, l03.Vm.ResultText });
		await HaibunScreenScenario.CaptureAsync(session, l03.View, screens, "06_L03_OrderFixApplied");

		await SelectReceiveBatchAsync(session, l03, lackBatch.Id, "L03:欠品");
		await RunCommandAsync(session, l03, "L03:欠品 反映", vm => vm.ApplyCommand);
		var z4After = (await HaibunAsync(session, z4.Id))[z4.Id];
		var lackLine = (await LinesAsync(session, lackBatch.Id)).SingleOrDefault();
		session.Check("L03:欠品 Z4 全量欠品で完了・伝票なし・バッチ反映済み",
			Committed(z4After, 0, 1) && lackLine is { Status: (int)EnumLogisticsLineStatus.Applied, TargetId: 0 }
			&& (await BatchAsync(session, lackBatch.Id))?.Status == (int)EnumLogisticsReceiveStatus.Applied,
			new { z4After.EndFlag, z4After.JitsuSu, z4After.ShortSu, lackLine?.Status, lackLine?.TargetTable });

		await SelectReceiveBatchAsync(session, l03, stockFixBatch.Id, "L03:入荷確定");
		session.Check("L03:入荷確定 3行とも未処理", l03.Vm.Lines.Count == 3 && l03.Vm.PendingCount == 3,
			new { lines = l03.Vm.Lines.Select(l => new { l.LineNo, l.StatusName, l.ErrorCode, l.ErrorMsg }) });
		var beforeStockFix = await StockAsync(session, s);
		await RunCommandAsync(session, l03, "L03:入荷確定 反映", vm => vm.ApplyCommand);
		var shiireH1 = await session.QueryAsync<Tran03Shiire>($"where RelateNo1={h1.Id}");
		var shiireH2 = await session.QueryAsync<Tran03Shiire>($"where RelateNo1={h2.Id}");
		var idoIn = await session.QueryAsync<Tran11IdoIn>($"where RelateNo1={i1.Id}");
		var hachus = await session.QueryAsync<Tran13Hachu>($"where Id IN ({h1.Id},{h2.Id})");
		session.Check("L03:入荷確定 仕入(RelateNo1=発注) H1 2・H2 5、移動受 3、発注完了",
			shiireH1.Count(x => x.Memo.StartsWith("WMS受信", StringComparison.Ordinal) && x.SuTotal == 2 && x.DenDay == WorkDay) == 1
			&& shiireH2.Count == 1 && shiireH2[0].SuTotal == 5 && shiireH2[0].Id_Shiire == s.ShiireId
			&& idoIn.Count == 1 && idoIn[0].SuTotal == 3 && idoIn[0].Id_Ido == s.WarehouseId && idoIn[0].Id_Soko == s.SourceWarehouseId
			&& hachus.Count == 2 && hachus.All(h => h.EndFlag == 1),
			new { shiireH1 = shiireH1.Select(x => new { x.Id, x.SuTotal, x.Memo }), shiireH2 = shiireH2.Select(x => new { x.Id, x.SuTotal, x.Id_Shiire }),
				idoIn = idoIn.Select(x => new { x.Id, x.SuTotal, x.Id_Soko, x.Id_Ido }), hachu = hachus.Select(h => new { h.Id, h.EndFlag }) });
		var afterStockFix = await StockAsync(session, s);
		var transitAfter = (await session.QueryAsync<SummaryStock>($"where Id_Soko={s.WarehouseId} AND Id_Shohin={s.ShohinId}")).Sum(x => x.TransitQty);
		session.Check("L03:入荷確定 在庫+10・積送中0", afterStockFix.Su == beforeStockFix.Su + 10 && transitAfter == 0,
			new { beforeStockFix, afterStockFix, transitAfter });
		session.CheckEqual("L03:入荷確定バッチ 反映済み", (int)EnumLogisticsReceiveStatus.Applied, (await BatchAsync(session, stockFixBatch.Id))?.Status ?? -1);

		await SelectReceiveBatchAsync(session, l03, inventoryBatch.Id, "L03:棚卸");
		var beforeInv = await StockAsync(session, s);
		await RunCommandAsync(session, l03, "L03:棚卸 反映", vm => vm.ApplyCommand);
		var tana = await session.QueryAsync<Tran60Tana>($"where Id_Soko={s.WarehouseId} AND DenDay='{WorkDay}'");
		session.Check("L03:棚卸 棚卸データ（棚番A-01・7点）を作り在庫は変えない",
			tana.Count == 1 && tana[0].TanaNo == "A-01" && tana[0].SuTotal == 7 && (await StockAsync(session, s)) == beforeInv
			&& (await BatchAsync(session, inventoryBatch.Id))?.Status == (int)EnumLogisticsReceiveStatus.Applied,
			new { tana = tana.Select(t => new { t.Id, t.TanaNo, t.SuTotal, t.Memo }) });
		await HaibunScreenScenario.CaptureAsync(session, l03.View, screens, "07_L03_InventoryApplied");

		// 同じ内容の再取込は「取込済み」
		var againName = $"WMS_ORDERFIX_{stamp}_again.csv";
		File.WriteAllBytes(Path.Combine(recv, againName), Cp932.GetBytes(orderFixText));
		await RunCommandAsync(session, l03, "L03:受信フォルダ再読込", vm => vm.RefreshFilesCommand);
		await RunCommandAsync(session, l03, "L03:同じ内容を再取込", vm => vm.ImportCommand);
		session.Check("L03:同じ内容の再取込は取込済み（バッチを作らない）",
			(await BatchByFileAsync(session, againName)) == null && l03.Vm.ResultText.Contains("取込済み", StringComparison.Ordinal) && !File.Exists(Path.Combine(recv, againName)),
			new { l03.Vm.ResultText });

		// ======== e. L04 訂正版・除外・再反映 ========
		var l04 = session.OpenView<IntegrationErrorDataQueryView, IntegrationErrorDataQueryViewModel>();
		if (!await l04.WaitAsync("L04:初期化", vm => !vm.IsProcessing && vm.StatusMessage.StartsWith("条件を指定", StringComparison.Ordinal))) return;
		l04.Input("L04:受信・問題のみ", vm => { vm.Direction = (int)EnumLogisticsDirection.Receive; vm.ProblemOnly = true; });
		await RunCommandAsync(session, l04, "L04:検索", vm => vm.SearchCommand);
		session.Check("L04:問題のある受信バッチは出荷確定だけ", l04.Vm.Batches.Count == 1 && l04.Vm.Batches[0].Id == orderFixBatch.Id && l04.Vm.Batches[0].IsProblem,
			new { batches = l04.Vm.Batches.Select(b => new { b.Id, b.KindName, b.StatusName, b.ErrorCount }) });
		l04.Input("L04:出荷確定バッチを選択", vm => vm.SelectedBatch = vm.Batches.FirstOrDefault(b => b.Id == orderFixBatch.Id));
		await l04.WaitAsync("L04:行一覧", vm => !vm.IsProcessing && vm.Lines.Count == 6 && vm.Lines.All(l => l.Line.Id_Batch == orderFixBatch.Id));
		var editP2 = l04.Vm.Lines.FirstOrDefault(l => l.Line.RefId == p2.Id);
		l04.Input("L04:P2の行を選択", vm => vm.SelectedLine = editP2);
		session.Check("L04:エラー行は訂正・除外できる（理由未入力では除外不可）",
			l04.Vm.BeginCorrectionCommand.CanExecute(null) && !l04.Vm.ExcludeCommand.CanExecute(null), new { editP2?.ErrorCode });
		l04.Run("L04:訂正版の編集開始", vm => vm.BeginCorrectionCommand);
		var kakuteiField = l04.Vm.CorrectionFields.FirstOrDefault(f => f.Header == "確定数");
		session.Check("L04:訂正版は原文の14項目を表示", l04.Vm.IsCorrecting && l04.Vm.CorrectionFields.Count == 14 && kakuteiField?.Original == "5",
			new { fields = l04.Vm.CorrectionFields.Select(f => $"{f.Header}={f.Value}") });
		l04.Input("L04:確定数を1へ訂正・理由", vm => { kakuteiField!.Value = "1"; vm.Reason = "確定数の誤り（入荷済み1）"; });
		await HaibunScreenScenario.CaptureAsync(session, l04.View, screens, "08_L04_Correcting");
		await RunCommandAsync(session, l04, "L04:訂正版を追加", vm => vm.CommitCorrectionCommand);
		var corrected = await LinesAsync(session, orderFixBatch.Id);
		var orgP2 = corrected.FirstOrDefault(l => l.Id == editP2?.Id);
		var newP2 = corrected.FirstOrDefault(l => l.Id_LineOrg == editP2?.Id);
		session.Check("L04:元行は訂正済み（原文は不変）、訂正版は同じ行番号で未処理",
			orgP2 is { Status: (int)EnumLogisticsLineStatus.Corrected } && orgP2.RawText == editP2!.RawText
			&& newP2 is { Status: (int)EnumLogisticsLineStatus.Pending, Su: 1 } && newP2.LineNo == orgP2.LineNo && newP2.ErrorCode.Length == 0,
			new { org = new { orgP2?.Status, orgP2?.ErrorMsg }, corrected = new { newP2?.Id, newP2?.Status, newP2?.ErrorCode, newP2?.ErrorMsg, newP2?.RawText } });
		await RunCommandAsync(session, l04, "L04:再検査", vm => vm.RecheckCommand);
		var rechecked = (await LinesAsync(session, orderFixBatch.Id)).FirstOrDefault(l => l.Id == newP2?.Id);
		session.CheckEqual("L04:再検査後も訂正版は未処理", (int)EnumLogisticsLineStatus.Pending, rechecked?.Status ?? -1);

		await l04.WaitAsync("L04:行一覧(訂正後)", vm => !vm.IsProcessing && vm.Lines.Count == 7);
		var bogusRow = l04.Vm.Lines.FirstOrDefault(l => l.RawText.Contains(BogusId.ToString(), StringComparison.Ordinal));
		l04.Input("L04:存在しない指示の行を選択・理由", vm => { vm.SelectedLine = bogusRow; vm.Reason = "連携先の誤送信"; });
		await RunCommandAsync(session, l04, "L04:除外", vm => vm.ExcludeCommand);
		var excluded = (await LinesAsync(session, orderFixBatch.Id)).FirstOrDefault(l => l.Id == bogusRow?.Id);
		session.Check("L04:除外した行は除外（理由を記録）", excluded is { Status: (int)EnumLogisticsLineStatus.Excluded } && excluded.ErrorMsg.Contains("連携先の誤送信"),
			new { excluded?.Status, excluded?.ErrorMsg });
		await HaibunScreenScenario.CaptureAsync(session, l04.View, screens, "09_L04_AfterExclude");

		var l03b = session.OpenView<IntegrationDataManualReceiveView, IntegrationDataManualReceiveViewModel>();
		if (!await l03b.WaitAsync("L03(再):初期化", vm => !vm.IsProcessing && vm.StatusMessage.StartsWith("取り込むファイル", StringComparison.Ordinal))) return;
		await SelectReceiveBatchAsync(session, l03b, orderFixBatch.Id, "L03(再):出荷確定");
		session.CheckEqual("L03(再):未処理は訂正版の1行", 1, l03b.Vm.PendingCount);
		var beforeP2 = await StockAsync(session, s);
		await RunCommandAsync(session, l03b, "L03(再):出荷確定 反映", vm => vm.ApplyCommand);
		var p2After = (await HaibunAsync(session, p2.Id))[p2.Id];
		var p2Applied = (await LinesAsync(session, orderFixBatch.Id)).FirstOrDefault(l => l.Id == newP2?.Id);
		var fixBatch2 = await BatchAsync(session, orderFixBatch.Id);
		var afterP2 = await StockAsync(session, s);
		session.Check("L03(再):訂正版を反映 P2 確定1・欠品2（残りは欠品で完了）、移動出庫、バッチ反映済み",
			Committed(p2After, 1, 2) && p2Applied is { Status: (int)EnumLogisticsLineStatus.Applied, TargetTable: nameof(Tran10IdoOut) }
			&& fixBatch2?.Status == (int)EnumLogisticsReceiveStatus.Applied && afterP2.Su == beforeP2.Su - 1,
			new { p2After.EndFlag, p2After.JitsuSu, p2After.ShortSu, p2After.ArrivedSu, p2Applied?.TargetTable, fixBatch2?.Status, fixBatch2?.OkCount, beforeP2, afterP2 });
		await HaibunScreenScenario.CaptureAsync(session, l03b.View, screens, "10_L03_CorrectedApplied");

		// ======== e. 配置失敗 → 再出力 → 送信取消 ========
		var sendDir = Path.Combine(baseFolder, LogisticsSettings.SendDir);
		var movedDir = sendDir + "_moved";
		Directory.Move(sendDir, movedDir);
		File.WriteAllText(sendDir, "送信フォルダを一時的に使えなくする");
		l02.Input("L02:出荷指示", vm => vm.IsOrder = true);
		await RunCommandAsync(session, l02, "L02:出荷指示 対象取得(Z3)", vm => vm.QueryCommand);
		session.Check("L02:未送信はZ3だけ", l02.Vm.Candidates.Count == 1 && l02.Vm.Candidates[0].RefId == z3.Id, new { rows = l02.Vm.Candidates.Select(c => c.RefId) });
		await RunCommandAsync(session, l02, "L02:出荷指示 送信(配置失敗)", vm => vm.SendCommand);
		var failedBatch = await LastBatchAsync(session, EnumLogisticsDirection.Send, LogisticsDataKind.ORDER);
		var z3Failed = (await HaibunAsync(session, z3.Id))[z3.Id];
		session.Check("L02:配置失敗 バッチ=配置失敗・Z3 SendFlg=1・画面に警告",
			failedBatch != null && failedBatch.Id != orderBatch?.Id && failedBatch.Status == (int)EnumLogisticsSendStatus.PlaceFailed && z3Failed.SendFlg == 1
			&& l02.Vm.ResultText.Contains("配置失敗", StringComparison.Ordinal),
			new { failedBatch?.Id, failedBatch?.Status, failedBatch?.Memo, z3Failed.SendFlg, l02.Vm.ResultText });
		await HaibunScreenScenario.CaptureAsync(session, l02.View, screens, "11_L02_PlaceFailed");
		File.Delete(sendDir);
		Directory.Move(movedDir, sendDir);
		if (failedBatch == null) return;

		l04.Input("L04:送信・出荷指示・すべて", vm => { vm.Direction = (int)EnumLogisticsDirection.Send; vm.Kind = LogisticsDataKind.ORDER; vm.ProblemOnly = false; });
		await RunCommandAsync(session, l04, "L04:送信バッチ検索", vm => vm.SearchCommand);
		l04.Input("L04:配置失敗バッチを選択", vm => vm.SelectedBatch = vm.Batches.FirstOrDefault(b => b.Id == failedBatch.Id));
		await l04.WaitAsync("L04:送信行一覧", vm => !vm.IsProcessing && vm.Lines.Count == 1 && vm.Lines[0].Line.Id_Batch == failedBatch.Id);
		session.Check("L04:配置失敗バッチは再出力・取消できる", l04.Vm.RewriteCommand.CanExecute(null) && l04.Vm.CancelSendCommand.CanExecute(null),
			new { status = l04.Vm.SelectedBatch?.StatusName });
		await HaibunScreenScenario.CaptureAsync(session, l04.View, screens, "12_L04_PlaceFailedBatch");
		await RunCommandAsync(session, l04, "L04:再出力", vm => vm.RewriteCommand);
		var rewritten = await BatchAsync(session, failedBatch.Id);
		var z3Rewritten = (await HaibunAsync(session, z3.Id))[z3.Id];
		session.Check("L04:再出力 配置済み・同じファイル名で送信フォルダへ・Z3 SendFlg=2",
			rewritten?.Status == (int)EnumLogisticsSendStatus.Placed && File.Exists(Path.Combine(sendDir, failedBatch.FileName)) && z3Rewritten.SendFlg == 2,
			new { rewritten?.Status, rewritten?.FileName, z3Rewritten.SendFlg });
		CheckSendFile(session, baseFolder, rewritten, LogisticsDataKind.ORDER, "L04:再出力");
		session.Check("L04:再出力後も同じバッチを選択中で取消できる", l04.Vm.SelectedBatch?.Id == failedBatch.Id && l04.Vm.CancelSendCommand.CanExecute(null),
			new { selected = l04.Vm.SelectedBatch?.Id, l04.Vm.StatusMessage });
		await RunCommandAsync(session, l04, "L04:送信取消", vm => vm.CancelSendCommand);
		var canceled = await BatchAsync(session, failedBatch.Id);
		var z3Canceled = (await HaibunAsync(session, z3.Id))[z3.Id];
		session.Check("L04:送信取消 バッチ=取消・未確定のZ3は SendFlg=0",
			canceled?.Status == (int)EnumLogisticsSendStatus.Canceled && z3Canceled.SendFlg == 0 && z3Canceled.EndFlag == 0,
			new { canceled?.Status, canceled?.Memo, z3Canceled.SendFlg });
		await HaibunScreenScenario.CaptureAsync(session, l04.View, screens, "13_L04_Canceled");
		await RunCommandAsync(session, l02, "L02:出荷指示 再取得(取消後)", vm => vm.QueryCommand);
		session.Check("L02:取消したZ3は再び送信対象", l02.Vm.Candidates.Count == 1 && l02.Vm.Candidates[0].RefId == z3.Id, new { rows = l02.Vm.Candidates.Select(c => c.RefId) });

		// 観察: 入荷予定バッチの「送信後変更あり」（入荷確定の反映で発注が完了すると Vdu が変わる）
		l04.Input("L04:送信・入荷予定", vm => vm.Kind = LogisticsDataKind.STOCK);
		await RunCommandAsync(session, l04, "L04:入荷予定バッチ検索", vm => vm.SearchCommand);
		var stockRow = l04.Vm.Batches.FirstOrDefault(b => b.Id == stockBatch?.Id);
		session.Note("観察:入荷予定バッチの送信後変更あり", new { stockRow?.ChangedAfterSend, stockRow?.IsProblem, stockRow?.StatusName });

		var final = await StockAsync(session, s);
		session.Check("最終:倉庫SK 在庫22（24−11+10−1）・引当2（未送信Z3）", final.Su == 22 && final.Reserve == 2, final);
	}

	// ---------------- helpers ----------------

	static bool Committed(TranHaibun h, int jitsu, int shortSu) =>
		h.EndFlag == 1 && h.JitsuSu == jitsu && h.ShortSu == shortSu && h.KakuteiDay == WorkDay;

	/// <summary>画面のボタンと同じく実行可否を確かめてからコマンドを実行する</summary>
	static async Task RunCommandAsync<TVm>(VmSession session, ViewDriver<TVm> driver, string name, Func<TVm, CommunityToolkit.Mvvm.Input.IAsyncRelayCommand> selector) where TVm : LogisticsViewModelBase {
		session.ClearDialogs();
		var command = selector(driver.Vm);
		if (!session.Check($"{name}:実行可", command.CanExecute(null), new { driver.Vm.StatusMessage, driver.Vm.UnusableReason })) return;
		await driver.RunAsync(name, selector);
		var errors = session.Dialogs.Where(d => d.Request.Image == MessageBoxImage.Error).Select(d => d.Request.Message).ToList();
		session.Check($"{name}:エラーダイアログなし", errors.Count == 0, new { errors, driver.Vm.StatusMessage });
		driver.Snapshot(name, vm => new { vm.StatusMessage, vm.ResultText });
	}

	static async Task SelectReceiveBatchAsync(VmSession session, ViewDriver<IntegrationDataManualReceiveViewModel> l03, long batchId, string name) {
		var option = l03.Vm.Batches.FirstOrDefault(b => b.Id == batchId);
		if (!session.Check($"{name}:バッチが選択肢にある", option != null, new { batchId, options = l03.Vm.Batches.Select(b => b.DisplayText) })) return;
		if (l03.Vm.SelectedBatch?.Id != batchId) {
			l03.Input($"{name}:バッチ選択", vm => vm.SelectedBatch = option, new { batchId });
		}
		await l03.WaitAsync($"{name}:行一覧", vm => !vm.IsProcessing && vm.Lines.Count > 0 && vm.Lines.All(l => l.Line.Id_Batch == batchId));
	}

	/// <summary>送信ファイル（send と send_bak が同じ内容）と送信行を確かめ、データ行を返す</summary>
	static List<LogisticsCsvLine> CheckSendFile(VmSession session, string baseFolder, TranLogisticsBatch? batch, string kind, string name) {
		if (batch == null || batch.FileName.Length == 0) {
			session.Fail($"{name}:送信ファイル", "バッチまたはファイル名がありません。");
			return [];
		}
		var send = Path.Combine(baseFolder, LogisticsSettings.SendDir, batch.FileName);
		var backup = Path.Combine(baseFolder, LogisticsSettings.SendBackupDir, batch.FileName);
		if (!session.Check($"{name}:send と send_bak にファイルがある", File.Exists(send) && File.Exists(backup), new { batch.FileName })) return [];
		var bytes = File.ReadAllBytes(send);
		var text = Cp932.GetString(bytes);
		var header = LogisticsFileFormat.BuildLine(LogisticsFileFormat.Columns(kind));
		session.Check($"{name}:ファイル名・見出し行・CRLF・send_bakと同一",
			batch.FileName.StartsWith($"WMS_{kind}_", StringComparison.Ordinal) && batch.FileName.EndsWith($"_{batch.Id}.csv", StringComparison.Ordinal)
			&& text.StartsWith(header + "\r\n", StringComparison.Ordinal) && !text.Replace("\r\n", "").Contains('\n') && bytes.SequenceEqual(File.ReadAllBytes(backup)),
			new { batch.FileName, head = text.Length > 300 ? text[..300] : text });
		return LogisticsFileFormat.Parse(kind, text);
	}

	static string Field(string kind, LogisticsCsvLine? line, string header) {
		if (line == null) return string.Empty;
		var index = LogisticsFileFormat.ColumnIndex(kind, header);
		return index < line.Fields.Count ? line.Fields[index] : string.Empty;
	}

	static async Task<TranLogisticsBatch?> LastBatchAsync(VmSession session, EnumLogisticsDirection direction, string kind) =>
		(await session.QueryAsync<TranLogisticsBatch>($"where Direction={(int)direction} AND DataKind=@0 order by Id desc", kind)).FirstOrDefault();

	static async Task<TranLogisticsBatch?> BatchByFileAsync(VmSession session, string fileName) =>
		(await session.QueryAsync<TranLogisticsBatch>("where Direction=2 AND FileName=@0 order by Id desc", fileName)).FirstOrDefault();

	static async Task<TranLogisticsBatch?> BatchAsync(VmSession session, long id) =>
		(await session.QueryAsync<TranLogisticsBatch>($"where Id={id}")).FirstOrDefault();

	static async Task<List<TranLogisticsLine>> LinesAsync(VmSession session, long batchId) =>
		await session.QueryAsync<TranLogisticsLine>($"where Id_Batch={batchId} order by LineNo, Id");

	static async Task<Dictionary<long, TranHaibun>> HaibunAsync(VmSession session, params long[] ids) =>
		(await session.QueryAsync<TranHaibun>($"where Id IN ({string.Join(",", ids)})")).ToDictionary(h => h.Id);

	sealed record StockState(int Su, int Reserve);

	static async Task<StockState> StockAsync(VmSession session, LogisticsSeeder.Result s) {
		var row = (await session.QueryAsync<SummaryRealStock>(
			$"where Id_Soko={s.WarehouseId} AND Id_Shohin={s.ShohinId} AND Id_Col={s.Id_Col} AND Id_Siz={s.Id_Siz}")).SingleOrDefault();
		return new StockState(row?.Su ?? 0, row?.ReserveQty ?? 0);
	}

	static Tran99Meisai Line(LogisticsSeeder.Result s, int su, int tanka) => new() {
		No = 1, Id_Shohin = s.ShohinId, Code_Shohin = s.ShohinCode, Mei_Shohin = LogisticsSeeder.ShohinName,
		Id_Col = s.Id_Col, Code_Col = s.ColCode, Id_Siz = s.Id_Siz, Code_Siz = s.SizCode, JanCode = LogisticsSeeder.JanCode,
		Su = su, Tanka = tanka, Kingaku = su * tanka, Jodai = 2000, Gedai = 1000, Id_Tax = 1,
	};

	static Tran03Shiire Purchase(LogisticsSeeder.Result s, long idSoko, long hachuId, int su, string denDay) {
		var tran = new Tran03Shiire {
			DenDay = denDay, KakeDay = denDay, Id_Soko = idSoko, RelateNo1 = (int)hachuId,
			Id_Shiire = hachuId > 0 ? s.ShiireId : 0, VShiire = hachuId > 0 ? new CodeNameView(s.ShiireId, s.ShiireCode, s.ShiireName) : new CodeNameView(),
			Id_Shain = s.EmployeeId, Rate = 100, SuTotal = su, KingakuTotal = su * 1000, Jmeisai = [Line(s, su, 1000)],
		};
		tran.EnKubun = EnumShiire.Shiire;
		return tran;
	}

	static Tran13Hachu Hachu(LogisticsSeeder.Result s, int su, string nouhinDay) => new() {
		DenDay = DenDay, NouhinDay = nouhinDay, Id_Soko = s.WarehouseId,
		VSoko = new CodeNameView(s.WarehouseId, s.WarehouseCode, string.Empty),
		Id_Shiire = s.ShiireId, VShiire = new CodeNameView(s.ShiireId, s.ShiireCode, s.ShiireName),
		Id_Shain = s.EmployeeId, Kubun = 10, Rate = 100, SuTotal = su, KingakuTotal = su * 1000, Jmeisai = [Line(s, su, 1000)],
		Memo = "UAT-VM 物流 発注",
	};

	static TranHaibun Haibun(LogisticsSeeder.Result s, EnumHaibun kubun, long idTenpo, int su, int relateNo1) => new() {
		DenDay = DenDay, NouhinDay = NouhinDay, Id_Soko = s.WarehouseId, Id_Tenpo = idTenpo, Kubun = (int)kubun,
		Id_Shohin = s.ShohinId, Id_Col = s.Id_Col, Id_Siz = s.Id_Siz, JanCode = LogisticsSeeder.JanCode,
		Su = su, Tanka = 1500, Kingaku = su * 1500, Jodai = 2000, Gedai = 1000, RelateNo1 = relateNo1, Id_Shain = s.EmployeeId,
		Memo = $"UAT-VM 物流 {kubun}",
	};

	static void CopyTree(string from, string to) {
		Directory.CreateDirectory(to);
		foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
		foreach (var dir in Directory.GetDirectories(from)) CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
	}
}
