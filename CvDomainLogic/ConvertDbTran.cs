using CvBase;
using CvBase.Share;

namespace CvDomainLogic;

// SqlDepends: HC$tran_tori0, HC$tran_tori1, HC$tran_tana0, HC$tran_tana1, MasterShain, MasterTokui, MasterShiire, MasterEndCustomer, MasterShohin, MasterMaterial, MasterMeisho, DerivedShohinColSiz
public partial class ConvertDb {
	/// <summary>
	/// 本部売上変換
	/// </summary>
	public int CnvTran00HonUri(bool isInit = true) {
		return ConvertTranHeadersByRange(
			0,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var soko = getCodeNameView<MasterTokui>(getString(rec, "倉庫CD")) ?? new();
				var tokui = getCodeNameView<MasterTokui>(getString(rec, "取引先CD1")) ?? new();
				var kubun = getDataInt(rec, "取引区分");
				var meisaiList = BuildTranMeisaiList(rec);
				var kingakuTotal = getDataInt(rec, "明細金額合計");
				// 旧「掛率1」は名称に反して実質すべて消費税率(%)が入っているため、税額計算にだけ使う
				var taxRatePercent = getDataInt(rec, "掛率1");
				var (tax, total) = CalcMigratedTaxTotal(kingakuTotal, taxRatePercent);
				var rate = getTorihikiRatePercent(getString(rec, "取引先CD1"));

				return new Tran00Uriage() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					DenDay = getString(rec, "在庫計上日", "19010101"),
					KakeDay = getString(rec, "掛計上日", "19010101"),
					Kubun = kubun,
					ManualNo = getString(rec, "手入力伝票NO"),
					RelateNo1 = getDataInt(rec, "関連伝票NO"),
					RelateNo2 = getDataInt(rec, "関連伝票NO2"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = kingakuTotal,
					JodaiTotal = getDataInt(rec, "上代合計"),
					GedaiTotal = getDataInt(rec, "下代合計"),
					Nebiki00Total = getHeaderNebiki(rec),
					Nebiki01Meisai = 0,
					Memo = getString(rec, "メモ"),
					Jdetail = new BaseDetailClass() {
						Yobi1 = getString(rec, "取引先CD2"),
						Yobi2 = getString(rec, "顧客TEL"),
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
					Tax = tax,
					Total = total,
				};
			});
	}
	/// <summary>
	/// 店舗売上変換
	/// </summary>
	public int CnvTran01TenUri(bool isInit = true) {
		return ConvertTranHeadersByRange(
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
				var kingakuTotal = getDataInt(rec, "明細金額合計");
				// 旧「掛率1」は名称に反して実質すべて消費税率(%)が入っているため、税額計算にだけ使う
				var taxRatePercent = getDataInt(rec, "掛率1");
				var (tax, total) = CalcMigratedTaxTotal(kingakuTotal, taxRatePercent);
				var rate = getTorihikiRatePercent(getString(rec, "取引先CD1"));

				return new Tran01Tenuri() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					DenDay = getString(rec, "在庫計上日", "19010101"),
					Kubun = kubun,
					RelateNo1 = getDataInt(rec, "関連伝票NO"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = kingakuTotal,
					JodaiTotal = getDataInt(rec, "上代合計"),
					GedaiTotal = getDataInt(rec, "下代合計"),
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
					Tax = tax,
					Total = total,
				};
			});
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
		return ConvertTranHeadersByRange(
			2,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var shiire = getCodeNameView<MasterShiire>(getString(rec, "取引先CD1")) ?? new();
				var kubun = getDataInt(rec, "取引区分");
				var meisaiList = BuildMaterialMeisaiList(rec, kubun);
				var kingakuTotal = getDataInt(rec, "明細金額合計");
				var naizei = getDataInt(rec, "内税消費税");
				var gaizei = getDataInt(rec, "外税消費税");
				// 区分99は金額列が常に0で税額列にのみ実額が入るため、Tax=0固定・Totalへ丸ごと計上する
				var isOther = kubun == 99;
				var tax = isOther ? 0 : naizei + gaizei;
				var total = isOther ? Math.Abs(naizei + gaizei) : Math.Abs(kingakuTotal) + tax;

				return new Tran02Material() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					DenDay = getString(rec, "在庫計上日", "19010101"),
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
					Tax = tax,
					Total = total,
				};
			});
	}
	/// <summary>
	/// 仕入変換
	/// </summary>
	public int CnvTran03Shiire(bool isInit = true) {
		return ConvertTranHeadersByRange(
			3,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var soko = getCodeNameView<MasterTokui>(getString(rec, "倉庫CD")) ?? new();
				var shiire = getCodeNameView<MasterShiire>(getString(rec, "取引先CD1")) ?? new();
				var kubun = getDataInt(rec, "取引区分");
				var meisaiList = BuildTranMeisaiList(rec);
				var kingakuTotal = getDataInt(rec, "明細金額合計");
				// 旧「掛率1」は名称に反して実質すべて消費税率(%)が入っているため、税額計算にだけ使う
				var taxRatePercent = getDataInt(rec, "掛率1");
				var (tax, total) = CalcMigratedTaxTotal(kingakuTotal, taxRatePercent);
				var rate = getShiireRatePercent(getString(rec, "取引先CD1"));

				return new Tran03Shiire() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					DenDay = getString(rec, "在庫計上日", "19010101"),
					KakeDay = getString(rec, "掛計上日", "19010101"),
					Kubun = kubun,
					ManualNo = getString(rec, "手入力伝票NO"),
					RelateNo1 = getDataInt(rec, "関連伝票NO"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = kingakuTotal,
					JodaiTotal = getDataInt(rec, "上代合計"),
					GedaiTotal = getDataInt(rec, "下代合計"),
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
					Tax = tax,
					Total = total,
				};
			});
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
					KingakuTotal = getDataInt(rec, "明細金額合計"),
					JodaiTotal = getDataInt(rec, "上代合計"),
					GedaiTotal = getDataInt(rec, "下代合計"),
					Nebiki00Total = getHeaderNebiki(rec),
					Nebiki01Meisai = 0,
					ManualNo = getString(rec, "手入力伝票NO"),
					RelateNo1 = getDataInt(rec, "関連伝票NO"),
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
					KakeDay = getString(rec, "在庫計上日", "19010101"),
					KingakuTotal = getDataInt(rec, "明細金額合計"),
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
					KakeDay = getString(rec, "在庫計上日", "19010101"),
					KingakuTotal = getDataInt(rec, "明細金額合計"),
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
					KingakuTotal = getDataInt(rec, "明細金額合計"),
					Memo = getString(rec, "メモ"),
					Jdetail = new BaseDetailClass() {
						Yobi1 = getString(rec, "関連伝票NO"),
						Yobi2 = getString(rec, "関連伝票NO2"),
					},
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
					KingakuTotal = getDataInt(rec, "明細金額合計"),
					JodaiTotal = getDataInt(rec, "上代合計"),
					GedaiTotal = getDataInt(rec, "下代合計"),
					Nebiki00Total = getHeaderNebiki(rec),
					Nebiki01Meisai = 0,
					ManualNo = getString(rec, "手入力伝票NO"),
					RelateNo1 = getDataInt(rec, "関連伝票NO"),
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
					KingakuTotal = getDataInt(rec, "明細金額合計"),
					JodaiTotal = getDataInt(rec, "上代合計"),
					GedaiTotal = getDataInt(rec, "下代合計"),
					Nebiki00Total = getHeaderNebiki(rec),
					Nebiki01Meisai = 0,
					ManualNo = getString(rec, "手入力伝票NO"),
					RelateNo1 = getDataInt(rec, "関連伝票NO"),
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
		return ConvertTranHeadersByRange(
			12,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var soko = getCodeNameView<MasterTokui>(getString(rec, "倉庫CD")) ?? new();
				var tokui = getCodeNameView<MasterTokui>(getString(rec, "取引先CD1")) ?? new();
				var kubun = getDataInt(rec, "取引区分");
				var meisaiList = BuildTranMeisaiList(rec);
				var kingakuTotal = getDataInt(rec, "明細金額合計");
				// 旧「掛率1」は名称に反して実質すべて消費税率(%)が入っているため、税額計算にだけ使う
				var taxRatePercent = getDataInt(rec, "掛率1");
				var (tax, total) = CalcMigratedTaxTotal(kingakuTotal, taxRatePercent);
				var rate = getTorihikiRatePercent(getString(rec, "取引先CD1"));

				return new Tran12Jyuchu() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					DenDay = getString(rec, "在庫計上日", "19010101"),
					Kubun = kubun,
					RelateNo1 = getDataInt(rec, "関連伝票NO"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = kingakuTotal,
					JodaiTotal = getDataInt(rec, "上代合計"),
					GedaiTotal = getDataInt(rec, "下代合計"),
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
					Tax = tax,
					Total = total,
				};
			});
	}
	/// <summary>
	/// 発注変換
	/// </summary>
	public int CnvTran13Hachu(bool isInit = true) {
		return ConvertTranHeadersByRange(
			13,
			isInit,
			rec => {
				var shain = getCodeNameView<MasterShain>(getString(rec, "入力社員CD")) ?? new();
				var soko = getCodeNameView<MasterTokui>(getString(rec, "倉庫CD")) ?? new();
				var shiire = getCodeNameView<MasterShiire>(getString(rec, "取引先CD1")) ?? new();
				var kubun = getDataInt(rec, "取引区分");
				var meisaiList = BuildTranMeisaiList(rec);
				var kingakuTotal = getDataInt(rec, "明細金額合計");
				// 旧「掛率1」は名称に反して実質すべて消費税率(%)が入っているため、税額計算にだけ使う
				var taxRatePercent = getDataInt(rec, "掛率1");
				var (tax, total) = CalcMigratedTaxTotal(kingakuTotal, taxRatePercent);
				var rate = getShiireRatePercent(getString(rec, "取引先CD1"));

				return new Tran13Hachu() {
					OldSeqNo = getDataLong(rec, "SEQ_NO"),
					DenDay = getString(rec, "在庫計上日", "19010101"),
					Kubun = kubun,
					RelateNo1 = getDataInt(rec, "関連伝票NO"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = kingakuTotal,
					JodaiTotal = getDataInt(rec, "上代合計"),
					GedaiTotal = getDataInt(rec, "下代合計"),
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
					Tax = tax,
					Total = total,
				};
			});
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
	// 取引先コード→掛率(%)のキャッシュ（マスタ種別ごと）。移行は数万件回るため1件ずつのマスタ取得を避ける。
	readonly Dictionary<string, int> torihikiRateCache = [];
	readonly Dictionary<string, int> shiireRateCache = [];
	/// <summary>
	/// 得意先コードから掛率(%)を引く。<c>Tran*.Rate</c> は掛率であり消費税率ではない。
	/// 旧CVnetの「掛率1」は実質すべて消費税率が入っており掛率の移行元にできないため、CV10マスタの掛率を採用する。
	/// マスタが引けない場合は0（未設定）とし、税率値を掛率として残さない。
	/// </summary>
	int getTorihikiRatePercent(string code) {
		if (string.IsNullOrWhiteSpace(code))
			return 0;
		if (torihikiRateCache.TryGetValue(code, out var cached))
			return cached;
		var rate = getMaster<MasterTokui>(code)?.RateProper ?? 0;
		torihikiRateCache[code] = rate;
		return rate;
	}
	/// <summary>
	/// 仕入先コードから掛率(%)を引く。仕入/発注の取引先CD1はMasterShiireのコード体系のため、
	/// <see cref="getTorihikiRatePercent"/>(MasterTokui参照)とは別キャッシュ・別マスタで引く。
	/// </summary>
	int getShiireRatePercent(string code) {
		if (string.IsNullOrWhiteSpace(code))
			return 0;
		if (shiireRateCache.TryGetValue(code, out var cached))
			return cached;
		var rate = getMaster<MasterShiire>(code)?.RateProper ?? 0;
		shiireRateCache[code] = rate;
		return rate;
	}
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
		["82"] = "01",  // 振込       -> 01 現金入金
		["85"] = "02",  // 振込手数料 -> 02 振込手数料
		["88"] = "04",  // 相殺       -> 04 相殺入金
		["89"] = "05",  // その他     -> 05 その他入金
	};
	/// <summary>
	/// 旧システムの明細取引区分から KIN 区分の <see cref="MasterMeisho"/> を引く。
	/// 対応表に無い旧コードは <c>null</c> を返す。
	/// </summary>
	MasterMeisho? getKinMeisho(string oldCode) {
		if (!KinKubunCodeMap.TryGetValue(oldCode, out var kinCode))
			return null;
		return getMeisho("KIN", kinCode);
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
	int getHeaderNebiki(Dictionary<string, object> rec) {
		return getDataInt(rec, "値引1") + getDataInt(rec, "値引2") + getDataInt(rec, "値引3");
	}
	/// <summary>
	/// 旧システムの「明細金額合計」（税抜、符号付き）と「掛率1」（移行元データは実質すべて消費税率%が入っている）から
	/// 消費税・総合計を導出する。移行売上のTotal/Tax/IsPayが未設定という既知課題への対応。
	/// この値は <c>Tran*.Rate</c> には入れない（Rate は掛率。getTorihikiRatePercent でマスタから引く）。
	/// 詳細: Doc/spec/2026-08-24_Rate列_掛率と税率の分離課題.md
	/// </summary>
	static (int Tax, int Total) CalcMigratedTaxTotal(int kingakuTotal, int ratePercent) {
		var absKingakuTotal = Math.Abs(kingakuTotal);
		var tax = (int)Math.Round(absKingakuTotal * ratePercent / 100.0);
		return (tax, absKingakuTotal + tax);
	}

	List<Tran99Meisai>? BuildTranMeisaiList(Dictionary<string, object> rec, string table = "HC$tran_tori1") { // 棚卸は別テーブル
		var detailRows = _fromDb.Fetch<Dictionary<string, object>>($"select * from {table} where ヘッダNO=@0 order by 行NO", getDataLong(rec, "SEQ_NO"));
		if (detailRows.Count == 0)
			return null;

		List<Tran99Meisai> meisaiList = new(detailRows.Count);
		foreach (var detailRec in detailRows) {
			var shohinCode = getString(detailRec, "商品CD");
			var colCode = getString(detailRec, "色CD");
			var sizCode = getString(detailRec, "サイズCD");
			var shohin = getMaster<MasterShohin>(shohinCode);
			var col = getMeisho("COL", colCode);
			var siz = getMeisho(shohin?.SizeKu ?? string.Empty, sizCode);
			int kubun = 0, jodai = 0, gedai = 0, nebiki00 = 0, nebiki01 = 0, nebiki02 = 0;
			if (table == "HC$tran_tori1") {
				kubun = getDataInt(detailRec, "明細取引区分");
				jodai = getDataInt(detailRec, "上代金額");
				gedai = getDataInt(detailRec, "下代金額");
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
				Kingaku = getDataInt(detailRec, "金額"),
				Memo = getString(detailRec, "明細メモ"),
				Kubun = kubun,
				Jodai = jodai,
				Gedai = gedai,
				Nebiki00 = nebiki00,
				Nebiki01 = nebiki01,
				Nebiki02 = nebiki02,
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
	/// 消費税はヘッダ側で一括計上するため、明細のId_Tax/TaxRate/Taxは持たない（0固定）。
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
				Kingaku = getDataInt(detailRec, "金額"),
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
			var col = getMeisho("COL", colCode);
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
				Kingaku = getDataInt(detailRec, "金額"),
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

}
