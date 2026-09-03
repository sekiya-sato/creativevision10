using CvAsset;
using CvBase;
using CvBase.Share;
using Microsoft.Extensions.Logging;

namespace CvDomainLogic;

// SqlDepends: HC$tran_tori0, HC$tran_tori1, HC$tran_tana0, HC$tran_tana1, MasterShain, MasterTokui, MasterShiire, MasterEndCustomer, MasterShohin, MasterMaterial, MasterMeisho, DerivedShohinColSiz
public partial class ConvertDb {
	/// <summary>
	/// 本部売上変換
	/// </summary>
	public int CnvTran00HonUri(bool isInit = true) {
		var tokuiTaxMap = GetTokuiTaxMap();
		var sysman = GetSysman();
		var mismatch = new TaxMismatchCounter();
		var count = ConvertTranHeadersByRange(
			0,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var soko = getCodeNameView<MasterTokui>(getString(rec, "倉庫CD")) ?? new();
				var tokui = getCodeNameView<MasterTokui>(getString(rec, "取引先CD1")) ?? new();
				var kubun = getDataInt(rec, "取引区分");
				var meisaiList = BuildTranMeisaiList(rec);
				var kingakuTotal = getDataLong(rec, "明細金額合計");
				var oldTax = getDataLong(rec, "内税消費税") + getDataLong(rec, "外税消費税");
				var rate = getDataInt(rec, "掛率1");
				var memo = getString(rec, "メモ", getString(rec,"MEMO2"));
				var denDay = getString(rec, "在庫計上日", "19010101");
				var slip = new Tran00Uriage() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					DenDay = denDay,
					KakeDay = getString(rec, "掛計上日", "19010101"),
					Kubun = kubun,
					ManualNo = getString(rec, "手入力伝票NO"),
					RelateNo1 = getDataLong(rec, "関連伝票NO"),
					RelateNo2 = getDataInt(rec, "関連伝票NO2"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = kingakuTotal,
					JodaiTotal = getDataLong(rec, "上代合計"),
					GedaiTotal = getDataLong(rec, "下代合計"),
					Nebiki00Total = getHeaderNebiki(rec),
					Nebiki01Meisai = 0,
					Memo = memo,
					Jdetail = new BaseDetailClass() {
						Yobi1 = getString(rec, "取引先CD2"),
					},
					Jmeisai = meisaiList,
					// 旧「掛計上FLG」は移行データで全件0のまま業務上意味を持たず、2026-08-16に売掛から除外しない方針を確定した
					// （ユーザーが移行済み50,311件を1へ一括更新済み。Doc/aicoding_log_013.md参照）。再変換でも同じ値になるようここで固定する。
					IsPay = 1,
					Id_Shain = shain.Sid,
					VShain = shain,
					Id_Soko = soko.Sid,
					VSoko = soko,
					Id_Tokui = tokui.Sid,
					VTokui = tokui,
					Rate = rate,
				};
				// 税計算単位・消費税端数処理は得意先マスタの伝票作成時点のスナップショット(Doc/spec/2026-09-01 2.2)
				var (calcUnit, rounding) = ResolveTorihikiTax(tokuiTaxMap, tokui.Sid, sysman);
				slip.TaxCalcUnit = (int)calcUnit;
				slip.TaxRounding = (int)rounding;
				var newTax = FinalizeTax(meisaiList ?? [], sysman, denDay, kingakuTotal,
					GetShohinTaxIdMap(), m => m.Id_Shohin, calcUnit, rounding, slip);
				mismatch.Record(calcUnit, oldTax, newTax);
				return slip;
			});
		mismatch.LogIfAny(_logger, nameof(Tran00Uriage));
		return count;
	}
	/// <summary>
	/// 店舗売上変換
	/// </summary>
	public int CnvTran01TenUri(bool isInit = true) {
		var tokuiTaxMap = GetTokuiTaxMap();
		var sysman = GetSysman();
		var mismatch = new TaxMismatchCounter();
		var count = ConvertTranHeadersByRange(
			1,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var soko = getCodeNameView<MasterTokui>(getString(rec, "倉庫CD")) ?? new();
				var tenpo = getCodeNameView<MasterTokui>(getString(rec, "取引先CD1")) ?? new();
				var kokyakuCode = getString(rec, "顧客TEL");
				var kokyaku = getCodeNameView<MasterEndCustomer>(kokyakuCode) ?? new();
				var kubun = getDataInt(rec, "取引区分");
				var meisaiList = BuildTranMeisaiList(rec);
				var kingakuTotal = getDataLong(rec, "明細金額合計");
				var rate = getDataInt(rec, "掛率1");
				var oldTax = getDataLong(rec, "内税消費税") + getDataLong(rec, "外税消費税");
				var denDay = getString(rec, "在庫計上日", "19010101");
				var slip = new Tran01Tenuri() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					DenDay = denDay,
					Kubun = kubun,
					RelateNo1 = getDataInt(rec, "関連伝票NO"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = kingakuTotal,
					JodaiTotal = getDataLong(rec, "上代合計"),
					GedaiTotal = getDataLong(rec, "下代合計"),
					Nebiki00Total = getHeaderNebiki(rec),
					Nebiki01Meisai = 0,
					Memo = getString(rec, "メモ"),
					Jdetail = new BaseDetailClass() {
						Yobi1 = getString(rec, "手入力伝票NO"),
						Yobi2 = getString(rec, "関連伝票NO2"),
					},
					Jmeisai = meisaiList,
					Id_Shain = shain.Sid,
					VShain = shain,
					Id_Soko = soko.Sid,
					VSoko = soko,
					Id_Tenpo = tenpo.Sid,
					VTenpo = tenpo,
					Id_Customer = kokyaku.Sid,
					VCustomer = kokyaku,
					Code_Customer = kokyakuCode,
					Rate = rate,
				};
				// 店舗売上はTaxCalcUnitを持たず常に伝票単位。端数処理は店舗(Id_Tenpo)のMasterTokuiのスナップショット(3.7)
				var (_, rounding) = ResolveTorihikiTax(tokuiTaxMap, tenpo.Sid, sysman);
				slip.TaxRounding = (int)rounding;
				var newTax = FinalizeTax(meisaiList ?? [], sysman, denDay, kingakuTotal,
					GetShohinTaxIdMap(), m => m.Id_Shohin, EnumTaxCalcUnit.Slip, rounding, slip);
				mismatch.Record(EnumTaxCalcUnit.Slip, oldTax, newTax);
				return slip;
			});
		mismatch.LogIfAny(_logger, nameof(Tran01Tenuri));
		return count;
	}
	/// <summary>
	/// 生地・付属仕入変換（旧伝票処理区分2）
	/// </summary>
	/// <remarks>
	/// 旧区分は 10:仕入 20:返品 30:値引 99:その他(消費税調整) の4種。区分30/99は明細の商品CDが常に
	/// 空欄(".")のため、<see cref="CnvMasterMaterial"/> が用意するプレースホルダ資材（Code=000030 値引き /
	/// Code=000099 消費税）へ紐付ける（<see cref="BuildMaterialMeisaiList"/> 参照）。
	/// <para>
	/// 消費税はヘッダの内税消費税/外税消費税を直接使う（Tran03Shiireのような掛率1からの逆算は行わない。
	/// 本区分は当該列が実額で入っている）。区分99は金額(明細金額合計)が常に0で税額列にのみ実額が入るため、
	/// Tax=0固定・Totalへ丸ごと計上する。これは<see cref="SummaryDb.CalcSummaryKaiKake"/>の
	/// 「区分99はTotalをTaxバケットへ加算する」ルールと合わせて二重計上を避けるため。
	/// </para>
	/// <para>
	/// 掛計上FLGは旧Uriage/Shiireと異なり0/1が実際に混在する（意味のあるフラグ）ため、固定値にせずそのまま反映する。
	/// </para>
	/// </remarks>
	public int CnvTran02Material(bool isInit = true) {
		var shiireTaxMap = GetShiireTaxMap();
		var sysman = GetSysman();
		var mismatch = new TaxMismatchCounter();
		var count = ConvertTranHeadersByRange(
			2,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var shiire = getCodeNameView<MasterShiire>(getString(rec, "取引先CD1")) ?? new();
				var kubun = getDataInt(rec, "取引区分");
				var meisaiList = BuildMaterialMeisaiList(rec, kubun);
				var kingakuTotalRaw = getDataLong(rec, "明細金額合計");
				// Tran02Material は Rate 列を持たないため旧「掛率1」は読まない
				var oldTax = getDataLong(rec, "内税消費税") + getDataLong(rec, "外税消費税");
				var denDay = getString(rec, "在庫計上日", "19010101");
				// 区分99(その他)は消費税調整伝票で、明細金額合計は常に0・実額は内税消費税/外税消費税にのみ入る。
				// SummaryDb.CalcSummaryKaiKake/CalcSummaryKaiShi は区分99をKingakuTotal(Sonota99)からTaxバケットへ
				// 丸めずそのまま積む(Doc/spec 3.8 A-6)ため、ここでもKingakuTotalへ実額を入れる。
				// 明細に課税対象が無いためFinalizeTaxの計算結果は自然にTax1/2/3=0となり、
				// SummaryDb側のSonota99加算と二重計上しない。
				var kingakuTotal = kubun == 99 ? oldTax : kingakuTotalRaw;
				var slip = new Tran02Material() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					DenDay = denDay,
					KakeDay = getString(rec, "掛計上日", "19010101"),
					Kubun = kubun,
					IsPay = getDataInt(rec, "掛計上FLG"),
					ManualNo = getString(rec, "手入力伝票NO"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = kingakuTotal,
					Memo = getString(rec, "メモ"),
					Jmeisai = meisaiList,
					Id_Shain = shain.Sid,
					VShain = shain,
					Id_Shiire = shiire.Sid,
					VShiire = shiire,
				};
				// 税計算単位・消費税端数処理は仕入先マスタの伝票作成時点のスナップショット(Doc/spec/2026-09-01 2.2)
				var (calcUnit, rounding) = ResolveTorihikiTax(shiireTaxMap, shiire.Sid, sysman);
				slip.TaxCalcUnit = (int)calcUnit;
				slip.TaxRounding = (int)rounding;
				var newTax = FinalizeTax(meisaiList ?? [], sysman, denDay, kingakuTotal,
					GetMaterialTaxIdMap(), m => m.Id_Material, calcUnit, rounding, slip);
				// 区分99はTaxが常に0になる設計(上記コメント)なので、旧税額との比較対象から除外する
				if (kubun != 99) {
					mismatch.Record(calcUnit, oldTax, newTax);
				}
				return slip;
			});
		mismatch.LogIfAny(_logger, nameof(Tran02Material));
		return count;
	}
	/// <summary>
	/// 仕入変換
	/// </summary>
	public int CnvTran03Shiire(bool isInit = true) {
		var shiireTaxMap = GetShiireTaxMap();
		var sysman = GetSysman();
		var mismatch = new TaxMismatchCounter();
		var count = ConvertTranHeadersByRange(
			3,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var soko = getCodeNameView<MasterTokui>(getString(rec, "倉庫CD")) ?? new();
				var shiire = getCodeNameView<MasterShiire>(getString(rec, "取引先CD1")) ?? new();
				var kubun = getDataInt(rec, "取引区分");
				var meisaiList = BuildTranMeisaiList(rec);
				var kingakuTotal = getDataLong(rec, "明細金額合計");
				var rate = getDataInt(rec, "掛率1");
				var oldTax = getDataLong(rec, "内税消費税") + getDataLong(rec, "外税消費税");
				var denDay = getString(rec, "在庫計上日", "19010101");

				var slip = new Tran03Shiire() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					DenDay = denDay,
					KakeDay = getString(rec, "掛計上日", "19010101"),
					Kubun = kubun,
					ManualNo = getString(rec, "手入力伝票NO"),
					RelateNo1 = getDataLong(rec, "関連伝票NO"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = kingakuTotal,
					JodaiTotal = getDataLong(rec, "上代合計"),
					GedaiTotal = getDataLong(rec, "下代合計"),
					Nebiki00Total = getHeaderNebiki(rec),
					Nebiki01Meisai = 0,
					Memo = getString(rec, "メモ"),
					Jdetail = new BaseDetailClass() {
						Yobi1 = getString(rec, "関連伝票NO"),
						Yobi2 = getString(rec, "関連伝票NO2"),
					},
					Jmeisai = meisaiList,
					// Tran00Uriage と同じ理由でIsPayを固定する（Tran03Shiireの移行済み25件も同様に1へ一括更新済み）。
					IsPay = 1,
					Id_Shain = shain.Sid,
					VShain = shain,
					Id_Soko = soko.Sid,
					VSoko = soko,
					Id_Shiire = shiire.Sid,
					VShiire = shiire,
					Rate = rate,
				};
				// 税計算単位・消費税端数処理は仕入先マスタの伝票作成時点のスナップショット(Doc/spec/2026-09-01 2.2)
				var (calcUnit, rounding) = ResolveTorihikiTax(shiireTaxMap, shiire.Sid, sysman);
				slip.TaxCalcUnit = (int)calcUnit;
				slip.TaxRounding = (int)rounding;
				var newTax = FinalizeTax(meisaiList ?? [], sysman, denDay, kingakuTotal,
					GetShohinTaxIdMap(), m => m.Id_Shohin, calcUnit, rounding, slip);
				mismatch.Record(calcUnit, oldTax, newTax);
				return slip;
			});
		mismatch.LogIfAny(_logger, nameof(Tran03Shiire));
		return count;
	}
	/// <summary>
	/// 移動変換
	/// </summary>
	public int CnvTran05Ido(bool isInit = true) {
		return ConvertTranHeadersByRange(
			5,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var soko = getCodeNameView<MasterTokui>(getString(rec, "倉庫CD")) ?? new(); // 移動元倉庫
				var nyuko = getCodeNameView<MasterTokui>(getString(rec, "取引先CD1")) ?? new(); // 移動先倉庫
				var kubun = getDataInt(rec, "取引区分");
				var meisaiList = BuildTranMeisaiList(rec);

				return new Tran05Ido() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					DenDay = getString(rec, "在庫計上日", "19010101"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = getDataLong(rec, "明細金額合計"),
					JodaiTotal = getDataLong(rec, "上代合計"),
					GedaiTotal = getDataLong(rec, "下代合計"),
					Nebiki00Total = getHeaderNebiki(rec),
					Nebiki01Meisai = 0,
					ManualNo = getString(rec, "手入力伝票NO"),
					RelateNo1 = getDataLong(rec, "関連伝票NO"),
					Memo = getString(rec, "メモ"),
					Jdetail = new BaseDetailClass() {
						Yobi1 = getString(rec, "関連伝票NO2"),
					},
					Jmeisai = meisaiList,
					Id_Shain = shain.Sid,
					VShain = shain,
					Id_Soko = soko.Sid,
					VSoko = soko,
					Id_Ido = nyuko.Sid,
					VIdo = nyuko,
				};
			});
	}
	/// <summary>
	/// 入金変換
	/// </summary>
	public int CnvTran06Nyukin(bool isInit = true) {
		return ConvertTranHeadersByRange(
			6,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var kakesaki = getCodeNameView<MasterTokui>(getString(rec, "取引先CD1")) ?? new(); // 掛先
				var meisaiList = BuildKinMeisaiList(rec);

				return new Tran06Nyukin() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					KakeDay = getString(rec, "掛計上日", "19010101"),
					KingakuTotal = getDataLong(rec, "明細金額合計"),
					Memo = getString(rec, "メモ"),
					Jmeisai = meisaiList,
					Id_Shain = shain.Sid,
					VShain = shain,
					Id_Torisaki = kakesaki.Sid,
					VTori = kakesaki,
				};
			});
	}
	/// <summary>
	/// 支払変換
	/// </summary>
	public int CnvTran07Shiharai(bool isInit = true) {
		return ConvertTranHeadersByRange(
			7,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var kakesaki = getCodeNameView<MasterShiire>(getString(rec, "取引先CD1")) ?? new(); // 掛先(支払はMasterShiire)
				var meisaiList = BuildKinMeisaiList(rec);

				return new Tran07Shiharai() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					KakeDay = getString(rec, "掛計上日", "19010101"),
					KingakuTotal = getDataLong(rec, "明細金額合計"),
					Memo = getString(rec, "メモ"),
					Jmeisai = meisaiList,
					Id_Shain = shain.Sid,
					VShain = shain,
					Id_Torisaki = kakesaki.Sid,
					VTori = kakesaki,
				};
			});
	}
	/// <summary>
	/// 棚卸変換
	/// </summary>
	public int CnvTran60Tana(bool isInit = true) {
		return ConvertTranHeadersByRange(
			60,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var soko = getCodeNameView<MasterTokui>(getString(rec, "倉庫CD")) ?? new(); // 移動元倉庫
				var kubun = getDataInt(rec, "取引区分");
				var meisaiList = BuildTranMeisaiList(rec, "HC$tran_tana1");

				return new Tran60Tana() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					DenDay = getString(rec, "在庫計上日", "19010101"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = getDataLong(rec, "明細金額合計"),
					Memo = getString(rec, "メモ"),
					// 棚卸の元テーブル HC$tran_tana0 は「関連伝票NO」「関連伝票NO2」を持たない
					// （対向エンティティが無い）。getString は存在しないキーを空文字で返すため、
					// 以前はこの予備欄へ黙って空文字が入るだけで何も移行できていなかった。
					// 実在する旧「棚番」を専用列 TanaNo へ移す。
					TanaNo = getString(rec, "棚番"),
					Jdetail = new BaseDetailClass(),
					Jmeisai = meisaiList,
					Id_Shain = shain.Sid,
					VShain = shain,
					Id_Soko = soko.Sid,
					VSoko = soko,
				};
			});
	}
	/// <summary>
	/// 在庫調整変換（旧伝票処理区分18）
	/// </summary>
	/// <remarks>
	/// 旧マスタ名称区分 T18（入庫/出庫/盗難/破損/検品ミス/その他）は CV10 の調整理由区分 CHR と
	/// コード番号が完全一致するため、そのまま <see cref="MasterMeisho"/>(Kubun="CHR") を引ける。
	/// 旧「取引先CD1」は倉庫CDの重複入力（対向エンティティなし）のため使わない（倉庫CDのみ見る）。
	/// 区分はすべて強制調整(<see cref="EnumChosei.Kyosei"/>)固定とする。旧18は棚卸確定由来ではなく
	/// 個別入力の調整のため。数量は旧データでは絶対値のため、<see cref="ChoseiRiyu.CalcFlag(string)"/>
	/// （10-19:入庫方向 / 20-29:出庫方向）で符号を掛けて明細へ積む。
	/// 金額・上代・下代・消費税はCV10の在庫調整が金額を持たない設計のため移行しない（常に0）。
	/// </remarks>
	public int CnvTran61Chosei(bool isInit = true) {
		return ConvertTranHeadersByRange(
			18,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var soko = getCodeNameView<MasterTokui>(getString(rec, "倉庫CD")) ?? new();
				var meisaiList = BuildChoseiMeisaiList(rec);

				return new Tran61Chosei() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					DenDay = getString(rec, "在庫計上日", "19010101"),
					EnKubun = EnumChosei.Kyosei,
					Id_Riyu = getChoseiRiyuId(getString(rec, "取引区分")),
					Memo = getString(rec, "メモ"),
					Jmeisai = meisaiList,
					Id_Shain = shain.Sid,
					VShain = shain,
					Id_Soko = soko.Sid,
					VSoko = soko,
					SuTotal = meisaiList?.Sum(m => m.Su) ?? 0,
				};
			});
	}
	/// <summary>
	/// 積送移動変換
	/// </summary>
	public int CnvTran10Ido(bool isInit = true) {
		return ConvertTranHeadersByRange(
			10,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var soko = getCodeNameView<MasterTokui>(getString(rec, "倉庫CD")) ?? new(); // 移動元倉庫
				var nyuko = getCodeNameView<MasterTokui>(getString(rec, "取引先CD1")) ?? new(); // 移動先倉庫
				var kubun = getDataInt(rec, "取引区分");
				var meisaiList = BuildTranMeisaiList(rec);

				return new Tran10IdoOut() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					DenDay = getString(rec, "在庫計上日", "19010101"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = getDataLong(rec, "明細金額合計"),
					JodaiTotal = getDataLong(rec, "上代合計"),
					GedaiTotal = getDataLong(rec, "下代合計"),
					Nebiki00Total = getHeaderNebiki(rec),
					Nebiki01Meisai = 0,
					ManualNo = getString(rec, "手入力伝票NO"),
					RelateNo1 = getDataLong(rec, "関連伝票NO"),
					Memo = getString(rec, "メモ"),
					Jdetail = new BaseDetailClass() {
						Yobi1 = getString(rec, "関連伝票NO2"),
					},
					Jmeisai = meisaiList,
					Id_Shain = shain.Sid,
					VShain = shain,
					Id_Soko = soko.Sid,
					VSoko = soko,
					Id_Ido = nyuko.Sid,
					VIdo = nyuko,
				};
			});
	}
	/// <summary>
	/// 移動受変換
	/// </summary>
	public int CnvTran11IdoIn(bool isInit = true) {
		return ConvertTranHeadersByRange(
			11,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var soko = getCodeNameView<MasterTokui>(getString(rec, "倉庫CD")) ?? new(); // 移動元倉庫
				var nyuko = getCodeNameView<MasterTokui>(getString(rec, "取引先CD1")) ?? new(); // 移動先倉庫
				var kubun = getDataInt(rec, "取引区分");
				var meisaiList = BuildTranMeisaiList(rec);

				return new Tran11IdoIn() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					DenDay = getString(rec, "在庫計上日", "19010101"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = getDataLong(rec, "明細金額合計"),
					JodaiTotal = getDataLong(rec, "上代合計"),
					GedaiTotal = getDataLong(rec, "下代合計"),
					Nebiki00Total = getHeaderNebiki(rec),
					Nebiki01Meisai = 0,
					ManualNo = getString(rec, "手入力伝票NO"),
					RelateNo1 = getDataLong(rec, "関連伝票NO"),
					Memo = getString(rec, "メモ"),
					Jdetail = new BaseDetailClass() {
						Yobi1 = getString(rec, "関連伝票NO2"),
					},
					Jmeisai = meisaiList,
					Id_Shain = shain.Sid,
					VShain = shain,
					Id_Soko = soko.Sid,
					VSoko = soko,
					Id_Ido = nyuko.Sid,
					VIdo = nyuko,
				};
			});
	}
	/// <summary>
	/// 受注変換
	/// </summary>
	public int CnvTran12Jyuchu(bool isInit = true) {
		var tokuiTaxMap = GetTokuiTaxMap();
		var sysman = GetSysman();
		var mismatch = new TaxMismatchCounter();
		var count = ConvertTranHeadersByRange(
			12,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var soko = getCodeNameView<MasterTokui>(getString(rec, "倉庫CD")) ?? new();
				var tokui = getCodeNameView<MasterTokui>(getString(rec, "取引先CD1")) ?? new();
				var kubun = getDataInt(rec, "取引区分");
				var meisaiList = BuildTranMeisaiList(rec);
				var kingakuTotal = getDataLong(rec, "明細金額合計");
				var rate = getDataInt(rec, "掛率1");
				var oldTax = getDataLong(rec, "内税消費税") + getDataLong(rec, "外税消費税");
				var denDay = getString(rec, "在庫計上日", "19010101");

				var slip = new Tran12Jyuchu() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					DenDay = denDay,
					Kubun = kubun,
					RelateNo1 = getDataInt(rec, "関連伝票NO"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = kingakuTotal,
					JodaiTotal = getDataLong(rec, "上代合計"),
					GedaiTotal = getDataLong(rec, "下代合計"),
					Nebiki00Total = getHeaderNebiki(rec),
					Nebiki01Meisai = 0,
					Memo = getString(rec, "メモ"),
					Jdetail = new BaseDetailClass() {
						Yobi1 = getString(rec, "関連伝票NO2"),
						Yobi2 = getString(rec, "手入力伝票NO"),
					},
					Jmeisai = meisaiList,
					Id_Shain = shain.Sid,
					VShain = shain,
					Id_Soko = soko.Sid,
					VSoko = soko,
					Id_Tokui = tokui.Sid,
					VTokui = tokui,
					Rate = rate,
				};
				// 受注はTaxCalcUnitを持たず常に伝票単位。端数処理は得意先マスタのスナップショット(3.7)
				var (_, rounding) = ResolveTorihikiTax(tokuiTaxMap, tokui.Sid, sysman);
				slip.TaxRounding = (int)rounding;
				var newTax = FinalizeTax(meisaiList ?? [], sysman, denDay, kingakuTotal,
					GetShohinTaxIdMap(), m => m.Id_Shohin, EnumTaxCalcUnit.Slip, rounding, slip);
				mismatch.Record(EnumTaxCalcUnit.Slip, oldTax, newTax);
				return slip;
			});
		mismatch.LogIfAny(_logger, nameof(Tran12Jyuchu));
		return count;
	}
	/// <summary>
	/// 発注変換
	/// </summary>
	public int CnvTran13Hachu(bool isInit = true) {
		var shiireTaxMap = GetShiireTaxMap();
		var sysman = GetSysman();
		var mismatch = new TaxMismatchCounter();
		var count = ConvertTranHeadersByRange(
			13,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var soko = getCodeNameView<MasterTokui>(getString(rec, "倉庫CD")) ?? new();
				var shiire = getCodeNameView<MasterShiire>(getString(rec, "取引先CD1")) ?? new();
				var kubun = getDataInt(rec, "取引区分");
				var meisaiList = BuildTranMeisaiList(rec);
				var kingakuTotal = getDataLong(rec, "明細金額合計");
				var rate = getDataInt(rec, "掛率1");
				var oldTax = getDataLong(rec, "内税消費税") + getDataLong(rec, "外税消費税");
				var denDay = getString(rec, "在庫計上日", "19010101");

				var slip = new Tran13Hachu() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					DenDay = denDay,
					Kubun = kubun,
					RelateNo1 = getDataInt(rec, "関連伝票NO"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = kingakuTotal,
					JodaiTotal = getDataLong(rec, "上代合計"),
					GedaiTotal = getDataLong(rec, "下代合計"),
					Nebiki00Total = getHeaderNebiki(rec),
					Nebiki01Meisai = 0,
					Memo = getString(rec, "メモ"),
					Jdetail = new BaseDetailClass() {
						Yobi1 = getString(rec, "関連伝票NO2"),
						Yobi2 = getString(rec, "手入力伝票NO"),
					},
					Jmeisai = meisaiList,
					Id_Shain = shain.Sid,
					VShain = shain,
					Id_Soko = soko.Sid,
					VSoko = soko,
					Id_Shiire = shiire.Sid,
					VShiire = shiire,
					Rate = rate,
				};
				// 発注はTaxCalcUnitを持たず常に伝票単位。端数処理は仕入先マスタのスナップショット(3.7)
				var (_, rounding) = ResolveTorihikiTax(shiireTaxMap, shiire.Sid, sysman);
				slip.TaxRounding = (int)rounding;
				var newTax = FinalizeTax(meisaiList ?? [], sysman, denDay, kingakuTotal,
					GetShohinTaxIdMap(), m => m.Id_Shohin, EnumTaxCalcUnit.Slip, rounding, slip);
				mismatch.Record(EnumTaxCalcUnit.Slip, oldTax, newTax);
				return slip;
			});
		mismatch.LogIfAny(_logger, nameof(Tran13Hachu));
		return count;
	}
	public int CnvTranSize1(bool isInit = true) {
		var cnt = 0;
		cnt += subCnvTranHeaderSize<Tran00Uriage>();
		cnt += subCnvTranHeaderSize<Tran01Tenuri>();
		cnt += subCnvTranHeaderSize<Tran03Shiire>();
		cnt += subCnvTranHeaderSize<Tran05Ido>();
		return cnt;
	}
	public int CnvTranSize2(bool isInit = true) {
		var cnt = 0;
		cnt += subCnvTranHeaderSize<Tran10IdoOut>();
		cnt += subCnvTranHeaderSize<Tran11IdoIn>();
		cnt += subCnvTranHeaderSize<Tran12Jyuchu>();
		cnt += subCnvTranHeaderSize<Tran13Hachu>();
		return cnt;
	}
	public int CnvTranSize3(bool isInit = true) {
		var cnt = 0;
		cnt += subCnvTranHeaderSize<Tran60Tana>();
		return cnt;
	}
	/// <summary>
	/// 明細サイズコード変換
	/// </summary>
	public int subCnvTranHeaderSize<T>(bool isInit = true) where T : ITranDetail {
		var cnt = 0;
		var tname = typeof(T).Name;
		var sql = @$"
UPDATE {tname}
SET Jmeisai = (
  SELECT json_group_array(
           json_set(
             j.value,
             '$.Id_Siz',
             m.Id_Siz
           )
         )
  FROM json_each({tname}.Jmeisai) AS j
  JOIN DerivedShohinColSiz AS m
    ON json_extract(j.value, '$.Id_Shohin') = m.Id_Shohin
   AND json_extract(j.value, '$.Id_Col')    = m.Id_Col
   AND json_extract(j.value, '$.Code_Siz')  = m.Code_Siz
   AND json_extract(j.value, '$.Id_Siz')   = 0
)
WHERE EXISTS (
  SELECT 1
  FROM json_each({tname}.Jmeisai) AS j
  JOIN DerivedShohinColSiz AS m
    ON json_extract(j.value, '$.Id_Shohin') = m.Id_Shohin
   AND json_extract(j.value, '$.Id_Col')    = m.Id_Col
   AND json_extract(j.value, '$.Code_Siz')  = m.Code_Siz
   AND json_extract(j.value, '$.Id_Siz')   = 0
)
";
		cnt = _toDb.ExecuteDialect(sql);
		var sql2 = $"SELECT changes() AS updated_count";
		cnt = _toDb.FirstOrDefault<int>(sql2);
		return cnt;
		// SQLite の JSON 関数を使用して、Jmeisai 内のサイズコードを一括で更新する SQL クエリを実行する。
		// このクエリは、Jmeisai 内の Id_Siz が 0 のレコードに対して、DerivedShohinColSiz テーブルから対応するサイズコードを取得して更新します。
		// Executeメソッドの仕様で、正常終了は0を返すため、更新件数は'SELECT changes()'で取得、クエリの実行自体は効率的に行われます。
		// アプリ側でレコード1件づつの処理をした場合、実データ5万件程度で数10分、300万件で4時間以上かかって途中リタイア。-> SQLクエリで一括更新する方法に変更して全体で5分程度で完了。
	}
	/// <summary>
	/// 関連伝票の紐付けを旧SEQ_NOから cv10 の Id へ張り替える（全Tran変換の後に実行する後処理）。
	/// <para>
	/// 変換直後の <c>RelateNo1</c> には旧「関連伝票NO」（=親伝票の旧 SEQ_NO）が入っているが、
	/// アプリ側は <c>RelateNo1</c> を cv10 の Id として扱う（<c>WHERE a.RelateNo1 = h.Id</c>。
	/// 発注残完了設定・入荷予定表・配分など）。張り替えないとこれらの機能で変換データが紐付かない。
	/// </para>
	/// <para>
	/// 変換ステップの順序は 売上→…→仕入→…→受注→発注 で、依存関係（発注→仕入、受注→売上）と逆のため
	/// 各変換の中では親の Id を引けない。そこで全変換の後に走る独立ステップとして一括UPDATEする。
	/// 変換画面で個別タスクだけ実行しても壊れないよう、親が未変換なら単に0件で終わる作りにしている。
	/// </para>
	/// </summary>
	public int CnvTranRelateFix(bool isInit = true) {
		var cnt = 0;
		cnt += subRelinkRelateNo1<Tran03Shiire, Tran13Hachu>();  // 仕入 → 発注
		cnt += subRelinkRelateNo1<Tran00Uriage, Tran12Jyuchu>(); // 売上 → 受注
		cnt += subRelinkRelateNo1<Tran11IdoIn, Tran10IdoOut>();  // 移動受 → 積送移動
		return cnt;
	}
	/// <summary>
	/// <typeparamref name="TChild"/> の <c>RelateNo1</c>（旧SEQ_NO）を、
	/// <typeparamref name="TParent"/> の <c>OldSeqNo</c> で引いた <c>Id</c> へ置き換える。
	/// <para>
	/// 再実行しても二重変換にならない。張替後の <c>RelateNo1</c> は親の Id（小さい値）になるが、
	/// <c>OldSeqNo</c> は旧SEQ_NO（実データでは500万以上）で値域が重ならないため、
	/// 2回目以降は EXISTS が成立せず0件で終わる。
	/// </para>
	/// <para>
	/// 判定は「親に一致する <c>OldSeqNo</c> があるか」だけで、子側の <c>OldSeqNo</c> は見ていない。
	/// したがってアプリで作られた子（<c>OldSeqNo</c>=0）も対象になり得るが、その <c>RelateNo1</c> には
	/// 既に cv10 の Id（小さい値）が入っており旧SEQ_NOと一致しないため実際には書き換わらない。
	/// 親が未変換・別伝票種別を指している場合も EXISTS が成立せず手つかずで残る。
	/// </para>
	/// </summary>
	int subRelinkRelateNo1<TChild, TParent>() {
		var child = typeof(TChild).Name;
		var parent = typeof(TParent).Name;
		var sql = @$"
UPDATE {child}
SET RelateNo1 = (
  SELECT p.Id FROM {parent} p WHERE p.OldSeqNo = {child}.RelateNo1
)
WHERE {child}.RelateNo1 > 0
  AND EXISTS (
    SELECT 1 FROM {parent} p WHERE p.OldSeqNo = {child}.RelateNo1
  )
";
		_toDb.ExecuteDialect(sql);
		// Execute系は正常終了で0を返すため、更新件数は changes() で取る（subCnvTranHeaderSize と同じ規約）
		var cnt = _toDb.FirstOrDefault<int>("SELECT changes() AS updated_count");
		_logger.LogInformation("関連伝票の張替 {Child}.RelateNo1 -> {Parent}.Id : {Count}件", child, parent, cnt);
		return cnt;
	}
	// 旧「掛率1」は掛率(%)そのものなので、Tran*.Rate へはそのまま移行する。
	// かつて「旧の掛率1には実質すべて消費税率が入っているのでCV10マスタの掛率を採用する」という
	// 前提で getTorihikiRatePercent/getShiireRatePercent を用意していたが、実データを確認すると
	// 掛率1 の値は 0/60/90/100 のみ(区分0,1,3,12,13で確認)で消費税率(3/5/8/10)ではなかった。
	// 前提が誤りで両メソッドも呼ばれていなかったため、誤解を招くだけなので削除した。
	T? getMaster<T>(string code) where T : class, IBaseCodeName, new() {
		if (string.IsNullOrWhiteSpace(code))
			return null;
		var current = _toDb.FirstOrDefault<T>("where Code=@0", code);
		return current;
	}
	MasterMeisho? getMeisho(string kubun, string code) {
		if (string.IsNullOrWhiteSpace(code))
			return null;
		var current = _toDb.FirstOrDefault<MasterMeisho>("where Kubun=@0 and Code=@1", [kubun, code]);
		return current;
	}
	/// <summary>
	/// 旧システムの入金・支払明細区分（<c>HC$tran_tori1.明細取引区分</c>）から
	/// <see cref="MasterMeisho"/> の <c>KIN</c> 区分コードへの対応表（2026-08-16 ユーザー決定）。
	/// <para>
	/// 旧コードは 80/82/85/88/89 の5種類で、KIN 区分の 01〜05 とは体系が異なる。
	/// 旧82（振込）は入金手段として旧80（現金）と同じ扱いにするため 01 現金入金へ寄せる。
	/// KIN の 03 手形入金へ対応する旧コードは実データに存在しない。
	/// </para>
	/// <para>
	/// 対応の無いコードは変換せず、<see cref="TranKinMeisai.Id_Kin"/> を 0 のままとし
	/// <see cref="TranKinMeisai.Code_Kin"/> へ旧コードを残す（消込画面では「未分類」として1本に集約される）。
	/// </para>
	/// </summary>
	static readonly Dictionary<string, string> KinKubunCodeMap = new() {
		["80"] = "01",  // 現金       -> 01 現金入金
		["81"] = "01",  // 小切手     -> 01 現金入金
		["82"] = "01",  // 振込       -> 01 現金入金
		["83"] = "02",  // 振込手数料 -> 02 振込手数料
		["85"] = "03",  // 手形入金   -> 03 手形入金
		["88"] = "04",  // 相殺       -> 04 相殺入金
		["89"] = "05",  // その他     -> 05 その他入金
		["99"] = "05",  // 関連伝票？ -> 05 その他入金
	};
	/// <summary>
	/// 旧システムの明細取引区分から KIN 区分の <see cref="MasterMeisho"/> を引く。
	/// 対応表に無い旧コードは <c>null</c> を返す。
	/// </summary>
	MasterMeisho? getKinMeisho(string oldCode) {
		if (!KinKubunCodeMap.TryGetValue(oldCode, out var kinCode))
			return null;
		return getMeisho(MasterMeisho.KubunKin, kinCode);
	}
	/// <summary>
	/// 旧「取引区分」(T18)から CHR区分の <see cref="MasterMeisho"/> Id を引く。
	/// コード番号がT18/CHRで一致するためそのまま引ける。マスタに無いコード（未知の理由コード）は
	/// <see cref="ChoseiRiyu.CalcFlag(string)"/> の符号帯（10-19/20-29）に応じて代表コード(10/20)へ丸める。
	/// </summary>
	long getChoseiRiyuId(string kubunCode) {
		var riyu = getMeisho(ChoseiRiyu.Kubun, kubunCode);
		if (riyu != null)
			return riyu.Id;
		var fallbackCode = ChoseiRiyu.CalcFlag(kubunCode) == 1 ? "10" : "20";
		return getMeisho(ChoseiRiyu.Kubun, fallbackCode)?.Id ?? 0;
	}
	long getHeaderNebiki(Dictionary<string, object> rec) {
		return getDataLong(rec, "値引1") + getDataLong(rec, "値引2") + getDataLong(rec, "値引3");
	}

	List<Tran99Meisai>? BuildTranMeisaiList(Dictionary<string, object> rec, string table = "HC$tran_tori1") { // 棚卸は別テーブル
		var detailRows = _fromDb.Fetch<Dictionary<string, object>>($"select t1.* from {table} t1 where t1.ヘッダNO=@0 order by t1.行NO", getDataLong(rec, "SEQ_NO"));
		if (detailRows.Count == 0)
			return null;

		List<Tran99Meisai> meisaiList = new(detailRows.Count);
		foreach (var detailRec in detailRows) {
			var shohinCode = getString(detailRec, "商品CD");
			var colCode = getString(detailRec, "色CD");
			var sizCode = getString(detailRec, "サイズCD");
			var shohin = getMaster<MasterShohin>(shohinCode);
			var col = getMeisho(MasterMeisho.KubunColor, colCode);
			var siz = getMeisho(shohin?.SizeKu ?? string.Empty, sizCode);
			int kubun = 0, jodai = 0, gedai = 0, nebiki00 = 0, nebiki01 = 0, nebiki02 = 0;
			if (table == "HC$tran_tori1") {
				kubun = getDataInt(detailRec, "明細取引区分");
				jodai = getDataInt(detailRec, "上代単価");
				gedai = getDataInt(detailRec, "下代単価");
				nebiki00 = getDataInt(detailRec, "明細値引");
				nebiki01 = getDataInt(detailRec, "明細値引1");
				nebiki02 = getDataInt(detailRec, "小計値引") + getDataInt(detailRec, "小計値引1");
			}
			meisaiList.Add(new Tran99Meisai() {
				No = getDataInt(detailRec, "行NO"),
				Id_Shohin = shohin?.Id ?? 0,
				Code_Shohin = shohin?.Code ?? shohinCode,
				Mei_Shohin = shohin?.Name ?? getString(detailRec, "明細名称"),
				JanCode = getString(detailRec, "JANCODE"),
				Id_Col = col?.Id ?? 0,
				Code_Col = col?.Code ?? colCode,
				Mei_Col = col?.Name ?? string.Empty,
				Id_Siz = siz?.Id ?? 0,
				Code_Siz = siz?.Code ?? sizCode,
				Mei_Siz = siz?.Name ?? string.Empty,
				Su = getDataInt(detailRec, "数量"),
				Tanka = getDataInt(detailRec, "単価"),
				Kingaku = getDataLong(detailRec, "金額"),
				Memo = getString(detailRec, "明細メモ"),
				Kubun = kubun,
				Jodai = jodai,
				Gedai = gedai,
				Nebiki00 = nebiki00,
				Nebiki01 = nebiki01,
				Nebiki02 = nebiki02,
				// Id_Tax/TaxRate/Tax はヘッダ側の FinalizeTax(TaxCalculator.Apply) が
				// MasterShohin.Id_Tax から解決して確定させるため、ここでは既定値のまま積む(Doc/spec 3.2)。
			});
		}

		return meisaiList;
	}
	/// <summary>
	/// 生地・付属仕入(<see cref="Tran02Material"/>)向け明細リストの作成。
	/// <para>
	/// 区分30(値引)/99(その他)は商品CDが常に空欄のため、<see cref="CnvMasterMaterial"/> が用意した
	/// プレースホルダ資材（Code=000030 値引き / Code=000099 消費税）へ固定で紐付ける。
	/// それ以外(仕入/返品)は商品CDから <see cref="MasterMaterial"/> を通常どおり解決する。
	/// Id_Tax/TaxRate/Tax はヘッダ側の FinalizeTax(TaxCalculator.Apply) が
	/// MasterMaterial.Id_Tax から解決して確定させるため、ここでは既定値のまま積む(Doc/spec 3.2)。
	/// </para>
	/// </summary>
	List<Tran99MaterialMeisai>? BuildMaterialMeisaiList(Dictionary<string, object> rec, int kubun) {
		var detailRows = _fromDb.Fetch<Dictionary<string, object>>("select * from HC$tran_tori1 where ヘッダNO=@0 order by 行NO", getDataLong(rec, "SEQ_NO"));
		if (detailRows.Count == 0)
			return null;

		var placeholderCode = kubun switch {
			30 => "000030",
			99 => "000099",
			_ => null,
		};

		List<Tran99MaterialMeisai> meisaiList = new(detailRows.Count);
		foreach (var detailRec in detailRows) {
			var oldCode = getString(detailRec, "商品CD");
			var material = getMaster<MasterMaterial>(placeholderCode ?? oldCode);

			meisaiList.Add(new Tran99MaterialMeisai() {
				No = getDataInt(detailRec, "行NO"),
				Id_Material = material?.Id ?? 0,
				Code_Material = material?.Code ?? oldCode,
				Mei_Material = material?.Name ?? getString(detailRec, "明細名称"),
				Su = getDataInt(detailRec, "数量"),
				Tanka = getDataInt(detailRec, "単価"),
				Kingaku = getDataLong(detailRec, "金額"),
				Memo = getString(detailRec, "明細メモ"),
			});
		}

		return meisaiList;
	}
	/// <summary>
	/// 在庫調整(<see cref="Tran61Chosei"/>)向け明細リストの作成。
	/// 旧数量は絶対値のため、明細取引区分(=T18/CHRコード)の符号帯で符号を掛けて積む。
	/// CV10の在庫調整は金額を持たない設計のため、単価・金額・上代・下代・値引・税は移行しない。
	/// </summary>
	List<Tran99Meisai>? BuildChoseiMeisaiList(Dictionary<string, object> rec) {
		var detailRows = _fromDb.Fetch<Dictionary<string, object>>("select * from HC$tran_tori1 where ヘッダNO=@0 order by 行NO", getDataLong(rec, "SEQ_NO"));
		if (detailRows.Count == 0)
			return null;

		List<Tran99Meisai> meisaiList = new(detailRows.Count);
		foreach (var detailRec in detailRows) {
			var shohinCode = getString(detailRec, "商品CD");
			var colCode = getString(detailRec, "色CD");
			var sizCode = getString(detailRec, "サイズCD");
			var shohin = getMaster<MasterShohin>(shohinCode);
			var col = getMeisho(MasterMeisho.KubunColor, colCode);
			var siz = getMeisho(shohin?.SizeKu ?? string.Empty, sizCode);
			var sign = ChoseiRiyu.CalcFlag(getString(detailRec, "明細取引区分"));

			meisaiList.Add(new Tran99Meisai() {
				No = getDataInt(detailRec, "行NO"),
				Id_Shohin = shohin?.Id ?? 0,
				Code_Shohin = shohin?.Code ?? shohinCode,
				Mei_Shohin = shohin?.Name ?? getString(detailRec, "明細名称"),
				JanCode = getString(detailRec, "JANCODE"),
				Id_Col = col?.Id ?? 0,
				Code_Col = col?.Code ?? colCode,
				Mei_Col = col?.Name ?? string.Empty,
				Id_Siz = siz?.Id ?? 0,
				Code_Siz = siz?.Code ?? sizCode,
				Mei_Siz = siz?.Name ?? string.Empty,
				Su = sign * getDataInt(detailRec, "数量"),
				Memo = getString(detailRec, "明細メモ"),
			});
		}

		return meisaiList;
	}
	List<TranKinMeisai>? BuildKinMeisaiList(Dictionary<string, object> rec) {
		var detailRows = _fromDb.Fetch<Dictionary<string, object>>("select * from HC$tran_tori1 where ヘッダNO=@0 order by 行NO", getDataLong(rec, "SEQ_NO"));
		if (detailRows.Count == 0)
			return null;

		List<TranKinMeisai> meisaiList = new(detailRows.Count);
		foreach (var detailRec in detailRows) {
			var code = getString(detailRec, "明細取引区分");
			// 旧コードを KIN 区分へ読み替えてから引く。従来は存在しない区分 "PAY" を引いていたため
			// 常に null となり、移行済みの入金・支払明細が全て Id_Kin=0 / Mei_Kin 空になっていた。
			var kinKubun = getKinMeisho(code);

			meisaiList.Add(new TranKinMeisai() {
				No = getDataInt(detailRec, "行NO"),
				Id_Kin = kinKubun?.Id ?? 0,
				Code_Kin = kinKubun?.Code ?? code,
				Mei_Kin = kinKubun?.Name ?? string.Empty,
				Kingaku = getDataLong(detailRec, "金額"),
				Memo = getString(detailRec, "明細メモ"),
			});
		}

		return meisaiList;
	}

	/// <summary>
	/// 伝票ヘッダを SEQ_NO 範囲ごとに分割して変換する。
	/// </summary>
	public int ConvertTranHeadersByRange<T>(
		int denpyoShoriKubun,
		bool isInit,
		Func<Dictionary<string, object>, T> converter,
		int chunkSize = 20000
	) where T : class {
		var rangeInfo = GetTranHeaderRangeInfo(denpyoShoriKubun);

		if (rangeInfo.Count == 0)
			return 0;

		_toDb.CreateTable(typeof(T), isInit);

		int totalCount = 0;
		foreach (var (rangeStartSeq, rangeEndSeq) in SplitRange(rangeInfo.SeqMin, rangeInfo.SeqMax, chunkSize)) {
			var tranHeader = _fromDb.Fetch<Dictionary<string, object>>(
				BuildTranHeaderSelectSql(denpyoShoriKubun, $"SEQ_NO between {rangeStartSeq} and {rangeEndSeq}")
			);
			if (tranHeader.Count == 0)
				continue;

			totalCount += InsertConvertedHeaders(tranHeader, converter);
		}

		return totalCount;
	}
	#region ConvertTranHeadersByRange のヘルパーメソッド
	private int InsertConvertedHeaders<T>(List<Dictionary<string, object>> tranHeader, Func<Dictionary<string, object>, T> converter) where T : class {
		List<T> list = new(tranHeader.Count);
		foreach (var rec in tranHeader) {
			list.Add(converter(rec));
		}

		_toDb.BeginTransaction(System.Data.IsolationLevel.Serializable);
		_toDb.InsertBulk<T>(list);
		_toDb.CompleteTransaction();

		return list.Count;
	}

	private (string TableName, string? BaseWhere) GetTranHeaderQueryParts(int denpyoShoriKubun) {
		return denpyoShoriKubun == 60
			? ("HC$tran_tana0", null)
			: ("HC$tran_tori0", $"伝票処理区分 = {denpyoShoriKubun}");
	}

	private string BuildTranHeaderSelectSql(int denpyoShoriKubun, string? additionalWhere = null) {
		var (tableName, baseWhere) = GetTranHeaderQueryParts(denpyoShoriKubun);
		var whereClause = BuildWhereClause(baseWhere, additionalWhere);
		return string.IsNullOrEmpty(whereClause)
			? $"select * from {tableName} order by SEQ_NO"
			: $"select * from {tableName} where {whereClause} order by SEQ_NO";
	}

	private (long Count, long SeqMin, long SeqMax) GetTranHeaderRangeInfo(int denpyoShoriKubun) {
		var (tableName, baseWhere) = GetTranHeaderQueryParts(denpyoShoriKubun);
		var whereClause = string.IsNullOrEmpty(baseWhere) ? string.Empty : $" where {baseWhere}";
		var sql = $@"
			select
				count(*) as cnt,
				min(SEQ_NO) as seqMin,
				max(SEQ_NO) as seqMax
			from {tableName}{whereClause}";
		var seqData = _fromDb.Fetch<Dictionary<string, object>>(sql);
		if (seqData.Count == 0)
			return (0, 0, 0);

		var row = seqData[0];
		return (
			Convert.ToInt64(row["cnt"]),
			Convert.ToInt64(row["seqMin"]),
			Convert.ToInt64(row["seqMax"])
		);
	}

	private static string? BuildWhereClause(string? baseWhere, string? additionalWhere) {
		if (string.IsNullOrWhiteSpace(baseWhere))
			return string.IsNullOrWhiteSpace(additionalWhere) ? null : additionalWhere;

		if (string.IsNullOrWhiteSpace(additionalWhere))
			return baseWhere;

		return $"{baseWhere} AND {additionalWhere}";
	}

	private List<(long rangeStartSeq, long rangeEndSeq)> SplitRange(long seqMin, long seqMax, int chunkSize = 20000) {
		if (seqMin > seqMax)
			throw new ArgumentException("seqMin must be <= seqMax");

		if (chunkSize <= 0)
			throw new ArgumentException("chunkSize must be positive");

		var ranges = new List<(long, long)>();

		long currentStart = seqMin;

		while (currentStart <= seqMax) {
			long currentEnd = currentStart + chunkSize - 1;
			if (currentEnd > seqMax)
				currentEnd = seqMax;

			ranges.Add((currentStart, currentEnd));

			if (currentEnd == seqMax)
				break;

			currentStart = currentEnd + 1;
		}
		return ranges;
	}
	#endregion
	CodeNameView? getCodeNameView<T>(string code) where T : BaseDbClass, IBaseCodeName, new() {
		if (string.IsNullOrWhiteSpace(code))
			return null;
		var current = _toDb.FirstOrDefault<T>("where Code=@0", code);
		if (current == null)
			return null;
		return new CodeNameView(current.Id, current.Code, current.Name);
	}

	#region 消費税計算単位・端数処理の移行(Doc/spec/2026-09-01_消費税計算単位・端数処理_全体設計.md 2.2/3.2-3.7)
	MasterSysman? _sysman;
	Dictionary<long, long>? _shohinTaxIdMap;
	Dictionary<long, long>? _materialTaxIdMap;
	Dictionary<long, (int TaxCalcUnit, int TaxRounding)>? _tokuiTaxMap;
	Dictionary<long, (int TaxCalcUnit, int TaxRounding)>? _shiireTaxMap;

	/// <summary>税率定義を持つシステム設定。移行全体で1回だけ読む</summary>
	MasterSysman GetSysman() => _sysman ??= _toDb.Fetch<MasterSysman>().FirstOrDefault() ?? new MasterSysman();

	/// <summary>
	/// 商品Id → 消費税区分の対応を一括で読む。明細1行ずつマスタを引くと
	/// 伝票数×明細数ぶんの往復になる（<see cref="Tran01Tenuri"/>は300万件規模）ため、先にまとめて読む。
	/// </summary>
	Dictionary<long, long> GetShohinTaxIdMap() =>
		_shohinTaxIdMap ??= _toDb.Dictionary<long, long>($"SELECT Id, Id_Tax FROM {nameof(MasterShohin)}");

	/// <summary>生地・付属Id → 消費税区分の対応を一括で読む(<see cref="GetShohinTaxIdMap"/>と同じ理由)</summary>
	Dictionary<long, long> GetMaterialTaxIdMap() =>
		_materialTaxIdMap ??= _toDb.Dictionary<long, long>($"SELECT Id, Id_Tax FROM {nameof(MasterMaterial)}");

	/// <summary>
	/// 得意先Id → (税計算単位, 消費税端数処理) の対応を一括で辞書化する。
	/// 取引先Idごとにマスタを引くと伝票件数ぶんの往復になるため、変換タスク開始時に1回だけ読む。
	/// </summary>
	Dictionary<long, (int TaxCalcUnit, int TaxRounding)> GetTokuiTaxMap() =>
		_tokuiTaxMap ??= _toDb.Fetch<MasterTokui>().ToDictionary(t => t.Id, t => (t.TaxCalcUnit, t.TaxRounding));

	/// <summary>仕入先Id → (税計算単位, 消費税端数処理) の対応を一括で辞書化する(<see cref="GetTokuiTaxMap"/>と同じ理由)</summary>
	Dictionary<long, (int TaxCalcUnit, int TaxRounding)> GetShiireTaxMap() =>
		_shiireTaxMap ??= _toDb.Fetch<MasterShiire>().ToDictionary(t => t.Id, t => (t.TaxCalcUnit, t.TaxRounding));

	/// <summary>
	/// 取引先Idから税計算単位・消費税端数処理を解決する(3.7)。取引先が引けない場合(旧データの不整合等)は
	/// 自社既定の消費税端数処理(<see cref="MasterSysman.TaxRounding"/>)を使い、税計算単位は安全側の伝票単位とする。
	/// </summary>
	static (EnumTaxCalcUnit CalcUnit, EnumRounding Rounding) ResolveTorihikiTax(
		Dictionary<long, (int TaxCalcUnit, int TaxRounding)> map, long torihikiId, MasterSysman sysman) {
		if (map.TryGetValue(torihikiId, out var found)) {
			return ((EnumTaxCalcUnit)found.TaxCalcUnit, (EnumRounding)found.TaxRounding);
		}
		return (EnumTaxCalcUnit.Slip, (EnumRounding)sysman.TaxRounding);
	}

	/// <summary>
	/// 明細のId_Taxを商品/資材マスタから解決し、<see cref="TaxCalculator.Apply"/>でヘッダの
	/// TaxableAmount1/2/3・Tax1/2/3・Totalを確定する(3.2-3.4)。戻り値は新しい税額合計(Tax1+Tax2+Tax3)で、
	/// 呼び出し側が旧伝票の税額との比較・ログに使う。
	/// </summary>
	/// <typeparam name="TMeisai"><see cref="Tran99Meisai"/> または <see cref="Tran99MaterialMeisai"/></typeparam>
	static long FinalizeTax<TMeisai>(
		List<TMeisai> meisai, MasterSysman sysman, string denDay, long kingakuTotal,
		Dictionary<long, long> taxIdMap, Func<TMeisai, long> keySelector,
		EnumTaxCalcUnit calcUnit, EnumRounding rounding, ITranTax header)
		where TMeisai : class, ITaxMeisaiLine {

		foreach (var m in meisai) {
			var key = keySelector(m);
			m.TaxId = key > 0 && taxIdMap.TryGetValue(key, out var found) ? found : TaxCalculator.StandardTaxId;
		}
		var rateOf = TaxRateResolver.CreateRateResolver(sysman, denDay);
		var totals = TaxCalculator.Apply(meisai, rateOf, calcUnit, rounding);

		header.TaxableAmount1 = totals.TaxableAmount1;
		header.TaxableAmount2 = totals.TaxableAmount2;
		header.TaxableAmount3 = totals.TaxableAmount3;
		header.Tax1 = totals.Tax1;
		header.Tax2 = totals.Tax2;
		header.Tax3 = totals.Tax3;
		header.Total = Math.Abs(kingakuTotal) + totals.TaxTotal;
		return totals.TaxTotal;
	}

	/// <summary>
	/// 旧伝票の税額(内税消費税+外税消費税)と新計算値の食い違いを集計する。
	/// 移行は旧システムの実績値を保存する性格も持つため、請求単位(旧税額を捨ててTax=0にする)・
	/// 伝票単位(TaxCalculator.Applyで計算し直す)の双方について、失われる／変わる金額を可視化する。
	/// </summary>
	sealed class TaxMismatchCounter {
		int _billingMismatch;
		int _slipMismatch;
		long _slipDiffSum;

		/// <param name="calcUnit">伝票の税計算単位</param>
		/// <param name="oldTax">旧伝票の税額(内税消費税+外税消費税)</param>
		/// <param name="newTax">新計算後の税額合計(Tax1+Tax2+Tax3)</param>
		public void Record(EnumTaxCalcUnit calcUnit, long oldTax, long newTax) {
			if (calcUnit == EnumTaxCalcUnit.Billing) {
				// 請求単位は新Taxが常に0になる設計(3.4)。旧税額が0でなければ、その分は請求計算まで失われる
				if (oldTax != 0) {
					_billingMismatch++;
				}
			} else if (oldTax != newTax) {
				_slipMismatch++;
				_slipDiffSum += newTax - oldTax;
			}
		}

		public void LogIfAny(ILogger logger, string tableName) {
			if (_billingMismatch > 0) {
				logger.LogWarning(
					"{Table} 請求単位: 旧税額(内税+外税消費税)が0でない伝票 {Count} 件。新伝票のTax1/2/3は0になり請求計算側で再計算される",
					tableName, _billingMismatch);
			}
			if (_slipMismatch > 0) {
				logger.LogWarning(
					"{Table} 伝票単位: 旧税額とTaxCalculator.Applyの計算結果が一致しない伝票 {Count} 件 差額合計 {Diff}",
					tableName, _slipMismatch, _slipDiffSum);
			}
		}
	}
	#endregion
}
