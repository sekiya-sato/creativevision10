using CvBase;
using CvWpfclient.ViewModels._01Master;
using CvWpfclient.Views._01Master;

namespace UatVm.Scenarios;

/// <summary>
/// 上代一括変更画面（<see cref="MasterJouDaiBulkChangeView"/>）Step 5「① 対象商品」のUAT。
/// </summary>
/// <remarks>
/// 検証値は事前に cv-sqlite MCP で確認した実データ（server-user163.db、約10GB）:
/// <list type="bullet">
/// <item>素材(Szi.Code)='077' は28件（MasterShohin.Id_Material経由）。うち TankaJodai が4000〜5000円の行は13件。</item>
/// <item>原産国(Gen.Code)='013' は4件（MasterShohin.Id_Country経由）。素材='077'の28件とは重複しない
///   （'077' OR '013' の実測合計は32件で 28+4 と一致）。</item>
/// <item>発売日(DayTento) 20230101〜20230107 は31件。</item>
/// <item>商品CD 00211161001〜00211161010 は4件（既存の検索項目。回帰確認用）。</item>
/// <item>現在上代(TankaJodai) 9800〜12800 は1025件（文字列比較だと "9800"&gt;"12800" になり0件になってしまう値域を
///   意図的に選んでいる。数値比較なら1025件）。既定の取得上限(1000)を超えるため、この検証だけ上限を引き上げる。</item>
/// <item>商品分類(Jsub)の枠 B09(初回販売区分) の Cd='113' は105件。</item>
/// <item>素材='077' の28件に対応する DerivedShohinColSiz（色×サイズ展開＝SKU）は60件。</item>
/// <item>MasterShohin全体は78,937件（既定上限1000を超えるため、無条件抽出で上限メッセージを確認できる）。</item>
/// </list>
/// </remarks>
public static class JodaiBulkExtractScenario {
	public static async Task RunAsync(VmSession session) {
		var d = session.OpenView<MasterJouDaiBulkChangeView, MasterJouDaiBulkChangeViewModel>();

		// BaseWindowがInitCommandを自動実行する。FieldOptionsは固定11件+Jsubの枠(登録されていれば最大10件)。
		// 商品分類の枠がテストDBに登録されていることは事前にcv-sqlite MCPで確認済み(B01〜B10の10件)。
		if (!await d.WaitAsync("初期化:FieldOptions読込", vm => vm.FieldOptions.Count > 11)) return;

		// --- 1) 条件行の追加・削除 ---
		d.Run("新規", vm => vm.DoNewCommand);
		session.CheckEqual("条件行:初期1行", 1, d.Vm.CondRows.Count);

		d.Run("条件行追加1", vm => vm.AddCondRowCommand);
		d.Run("条件行追加2", vm => vm.AddCondRowCommand);
		session.CheckEqual("条件行:2回追加後3行", 3, d.Vm.CondRows.Count);
		session.CheckEqual("条件行:No振り直し(追加後)", "1,2,3", string.Join(",", d.Vm.CondRows.Select(r => r.No)));

		var removeTarget = d.Vm.CondRows[1];
		d.Run("条件行削除(2行目)", vm => vm.RemoveCondRowCommand, removeTarget);
		session.CheckEqual("条件行:削除後2行", 2, d.Vm.CondRows.Count);
		session.CheckEqual("条件行:No振り直し(削除後)", "1,2", string.Join(",", d.Vm.CondRows.Select(r => r.No)));

		// 最後の1行は削除できない(残す)ことの確認
		d.Run("条件行削除(2行目)", vm => vm.RemoveCondRowCommand, d.Vm.CondRows[1]);
		d.Run("条件行削除(1行目・最後の1行なので無視される)", vm => vm.RemoveCondRowCommand, d.Vm.CondRows[0]);
		session.CheckEqual("条件行:最後の1行は残る", 1, d.Vm.CondRows.Count);

		// --- 2) 既存の検索項目(商品CD)での抽出が従来どおり動くこと(回帰確認) ---
		d.Run("新規", vm => vm.DoNewCommand);
		d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "M.Code");
		d.Vm.CondRows[0].CdFrom = "00211161001";
		d.Vm.CondRows[0].CdTo = "00211161010";
		await d.RunAsync("明細取得:商品CD(既存項目)", vm => vm.GetMeisaiCommand);
		var dbCode = await session.QueryAsync<MasterShohin>(
			"SELECT Id, Vdc, Vdu, Code FROM MasterShohin WHERE Code >= @0 AND Code <= @1",
			"00211161001", "00211161010");
		session.CheckEqual("商品CD:DB実データと一致(回帰)", dbCode.Count, d.Vm.MeisaiRows.Count);

		// --- 3) 素材(Szi.Code)で絞り込み。Style数/SKU数もDB実データと突合 ---
		d.Run("新規", vm => vm.DoNewCommand);
		d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "Szi.Code");
		d.Vm.CondRows[0].CdFrom = "077";
		d.Vm.CondRows[0].CdTo = "077";
		await d.RunAsync("明細取得:素材=077", vm => vm.GetMeisaiCommand);
		var dbMaterial = await session.QueryAsync<MasterShohin>("""
			SELECT M.Id, M.Vdc, M.Vdu, M.Code
			FROM MasterShohin M LEFT JOIN MasterMeisho Szi ON Szi.Id = M.Id_Material
			WHERE Szi.Code = @0
			""", "077");
		session.CheckEqual("素材:DB実データと一致(Style数)", dbMaterial.Count, d.Vm.MeisaiRows.Count);
		session.CheckEqual("素材:MeisaiCount(Style数表示)", dbMaterial.Count, d.Vm.MeisaiCount);

		var dbSku = await session.QueryAsync<DerivedShohinColSiz>("""
			SELECT Id, Vdc, Vdu, Id_Shohin FROM DerivedShohinColSiz
			WHERE Id_Shohin IN (
			    SELECT M.Id FROM MasterShohin M LEFT JOIN MasterMeisho Szi ON Szi.Id = M.Id_Material
			    WHERE Szi.Code = @0
			)
			""", "077");
		session.CheckEqual("素材:DB実データと一致(SKU数)", dbSku.Count, d.Vm.TargetSkuCount);

		// 1行目のOpeはAND/ORどちらを指定しても無視される(繋ぐ相手が無いため)ことの確認
		d.Vm.CondRows[0].Ope = 1; // OR を指定してみる
		await d.RunAsync("明細取得:素材=077(1行目Ope=OR指定でも無視)", vm => vm.GetMeisaiCommand);
		session.CheckEqual("素材:1行目のOpeは無視される", dbMaterial.Count, d.Vm.MeisaiRows.Count);

		// --- 4) 原産国(Gen.Code)で絞り込み ---
		d.Run("新規", vm => vm.DoNewCommand);
		d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "Gen.Code");
		d.Vm.CondRows[0].CdFrom = "013";
		d.Vm.CondRows[0].CdTo = "013";
		await d.RunAsync("明細取得:原産国=013", vm => vm.GetMeisaiCommand);
		var dbCountry = await session.QueryAsync<MasterShohin>("""
			SELECT M.Id, M.Vdc, M.Vdu, M.Code
			FROM MasterShohin M LEFT JOIN MasterMeisho Gen ON Gen.Id = M.Id_Country
			WHERE Gen.Code = @0
			""", "013");
		session.CheckEqual("原産国:DB実データと一致", dbCountry.Count, d.Vm.MeisaiRows.Count);

		// --- 5) 発売日(DayTento)で絞り込み ---
		d.Run("新規", vm => vm.DoNewCommand);
		d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "M.DayTento");
		d.Vm.CondRows[0].CdFrom = "20230101";
		d.Vm.CondRows[0].CdTo = "20230107";
		await d.RunAsync("明細取得:発売日20230101-07", vm => vm.GetMeisaiCommand);
		var dbTento = await session.QueryAsync<MasterShohin>(
			"SELECT Id, Vdc, Vdu, Code FROM MasterShohin WHERE DayTento >= @0 AND DayTento <= @1",
			"20230101", "20230107");
		session.CheckEqual("発売日:DB実データと一致", dbTento.Count, d.Vm.MeisaiRows.Count);

		// --- 6) 現在上代(TankaJodai)は数値比較になっていること ---
		// "9800"〜"12800" は文字列比較だと "9800" > "12800" になり0件になってしまう値域。
		// 既定の取得上限(1000)を超える(実測1025件)ため、この検証だけ上限を引き上げる。
		d.Run("新規", vm => vm.DoNewCommand);
		d.Input("上限:5000に引き上げ(現在上代テスト用)", vm => vm.MaxCountText = "5000");
		d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "M.TankaJodai");
		d.Vm.CondRows[0].CdFrom = "9800";
		d.Vm.CondRows[0].CdTo = "12800";
		await d.RunAsync("明細取得:現在上代9800-12800(数値比較)", vm => vm.GetMeisaiCommand);
		var dbPrice = await session.QueryAsync<MasterShohin>(
			"SELECT Id, Vdc, Vdu, Code FROM MasterShohin WHERE TankaJodai >= CAST(@0 AS INTEGER) AND TankaJodai <= CAST(@1 AS INTEGER)",
			"9800", "12800");
		session.Check("現在上代:文字列比較なら0件になる値域を使っている", dbPrice.Count > 0, new { dbPrice.Count });
		session.CheckEqual("現在上代:DB実データと一致(数値比較)", dbPrice.Count, d.Vm.MeisaiRows.Count);

		// --- 7) 商品分類(Jsub)の枠。固定名でハードコードせず、登録されている枠から選ぶ ---
		d.Run("新規", vm => vm.DoNewCommand);
		var jsubField = d.Vm.FieldOptions.First(f => f.JsubKb == "B09");
		d.Vm.CondRows[0].Field = jsubField;
		d.Vm.CondRows[0].CdFrom = "113";
		d.Vm.CondRows[0].CdTo = "113";
		await d.RunAsync($"明細取得:商品分類[{jsubField.Name}]=113", vm => vm.GetMeisaiCommand);
		var dbJsub = await session.QueryAsync<MasterShohin>("""
			SELECT M.Id, M.Vdc, M.Vdu, M.Code
			FROM MasterShohin M,
			     json_each(CASE WHEN M.Jsub IS NOT NULL AND json_valid(M.Jsub) THEN M.Jsub ELSE '[]' END) J
			WHERE json_extract(J.value,'$.Kb') = @0
			  AND json_extract(J.value,'$.Cd') >= @1 AND json_extract(J.value,'$.Cd') <= @2
			""", "B09", "113", "113");
		session.CheckEqual("商品分類(Jsub):DB実データと一致", dbJsub.Count, d.Vm.MeisaiRows.Count);

		// --- 8) AND結合: 素材=077 AND 現在上代4000-5000 → 28件から13件に絞られる ---
		d.Run("新規", vm => vm.DoNewCommand);
		d.Vm.CondRows[0].Field = d.Vm.FieldOptions.First(f => f.Column == "Szi.Code");
		d.Vm.CondRows[0].CdFrom = "077";
		d.Vm.CondRows[0].CdTo = "077";
		d.Run("条件行追加(AND用)", vm => vm.AddCondRowCommand);
		d.Vm.CondRows[1].Field = d.Vm.FieldOptions.First(f => f.Column == "M.TankaJodai");
		d.Vm.CondRows[1].CdFrom = "4000";
		d.Vm.CondRows[1].CdTo = "5000";
		d.Vm.CondRows[1].Ope = 0; // AND
		await d.RunAsync("明細取得:素材=077 AND 上代4000-5000", vm => vm.GetMeisaiCommand);
		session.Check("AND結合:単独条件(28件)より絞られている", d.Vm.MeisaiRows.Count < dbMaterial.Count, new { AndCount = d.Vm.MeisaiRows.Count, MaterialOnlyCount = dbMaterial.Count });
		var dbAnd = await session.QueryAsync<MasterShohin>("""
			SELECT M.Id, M.Vdc, M.Vdu, M.Code
			FROM MasterShohin M LEFT JOIN MasterMeisho Szi ON Szi.Id = M.Id_Material
			WHERE Szi.Code = @0 AND M.TankaJodai >= CAST(@1 AS INTEGER) AND M.TankaJodai <= CAST(@2 AS INTEGER)
			""", "077", "4000", "5000");
		session.CheckEqual("AND結合:DB実データと一致", dbAnd.Count, d.Vm.MeisaiRows.Count);

		// --- 9) OR結合: 素材=077(28件) OR 原産国=013(4件、重複無し) → 32件に広がる ---
		d.Vm.CondRows[1].Field = d.Vm.FieldOptions.First(f => f.Column == "Gen.Code");
		d.Vm.CondRows[1].CdFrom = "013";
		d.Vm.CondRows[1].CdTo = "013";
		d.Vm.CondRows[1].Ope = 1; // OR
		await d.RunAsync("明細取得:素材=077 OR 原産国=013", vm => vm.GetMeisaiCommand);
		session.Check("OR結合:単独条件(28件)より広がっている", d.Vm.MeisaiRows.Count > dbMaterial.Count, new { OrCount = d.Vm.MeisaiRows.Count, MaterialOnlyCount = dbMaterial.Count });
		var dbOr = await session.QueryAsync<MasterShohin>("""
			SELECT M.Id, M.Vdc, M.Vdu, M.Code
			FROM MasterShohin M
			     LEFT JOIN MasterMeisho Szi ON Szi.Id = M.Id_Material
			     LEFT JOIN MasterMeisho Gen ON Gen.Id = M.Id_Country
			WHERE Szi.Code = @0 OR Gen.Code = @1
			""", "077", "013");
		session.CheckEqual("OR結合:DB実データと一致", dbOr.Count, d.Vm.MeisaiRows.Count);
		session.CheckEqual("OR結合:28+4件と一致(無関係の重複が無いことも確認済み)", dbMaterial.Count + dbCountry.Count, dbOr.Count);

		// --- 10) 無条件抽出で既定の取得上限(1000)メッセージが出ること ---
		d.Run("新規", vm => vm.DoNewCommand);
		d.Input("上限:既定の1000へ戻す", vm => vm.MaxCountText = "1000");
		await d.RunAsync("明細取得:無条件(上限確認)", vm => vm.GetMeisaiCommand);
		session.CheckEqual("上限確認:1000件で打ち切り", 1000, d.Vm.MeisaiRows.Count);
		session.Check("上限確認:上限到達メッセージ",
			d.Vm.Message.Contains("上限", StringComparison.Ordinal), new { d.Vm.Message });
	}
}
