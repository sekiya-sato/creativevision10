using Cvnet10Base;
using Cvnet10Base.Share;

namespace Cvnet10DomainLogic;

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

				return new Tran00Uriage() {
					DenDay = getString(rec, "在庫計上日", "19010101"),
					KakeDay = getString(rec, "掛計上日", "19010101"),
					Kubun = kubun,
					CalcFlag = getCalcFlag(kubun),
					ManualNo = getString(rec, "手入力伝票NO"),
					RelateNo1 = getDataInt(rec, "関連伝票NO"),
					RelateNo2 = getDataInt(rec, "関連伝票NO2"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = getDataInt(rec, "明細金額合計"),
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
					IsPay = getDataInt(rec, "掛計上FLG"),
					Id_Shain = shain.Sid,
					VShain = shain,
					Id_Soko = soko.Sid,
					VSoko = soko,
					Id_Tokui = tokui.Sid,
					VTokui = tokui,
					Rate = getDataInt(rec, "掛率1"),
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

				return new Tran01Tenuri() {
					DenDay = getString(rec, "在庫計上日", "19010101"),
					Kubun = kubun,
					CalcFlag = getCalcFlag(kubun),
					RelateNo1 = getDataInt(rec, "関連伝票NO"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = getDataInt(rec, "明細金額合計"),
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
					Rate = getDataInt(rec, "掛率1"),
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
				var shiire = getCodeNameView<MasterTokui>(getString(rec, "取引先CD1")) ?? new();
				var kubun = getDataInt(rec, "取引区分");
				var meisaiList = BuildTranMeisaiList(rec);

				return new Tran03Shiire() {
					DenDay = getString(rec, "在庫計上日", "19010101"),
					KakeDay = getString(rec, "掛計上日", "19010101"),
					Kubun = kubun,
					CalcFlag = getCalcFlag(kubun),
					ManualNo = getString(rec, "手入力伝票NO"),
					RelateNo1 = getDataInt(rec, "関連伝票NO"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = getDataInt(rec, "明細金額合計"),
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
					IsPay = getDataInt(rec, "掛計上FLG"),
					Id_Shain = shain.Sid,
					VShain = shain,
					Id_Soko = soko.Sid,
					VSoko = soko,
					Id_Shiire = shiire.Sid,
					VShiire = shiire,
					Rate = getDataInt(rec, "掛率1"),
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
					DenDay = getString(rec, "在庫計上日", "19010101"),
					CalcFlag = getCalcFlag(kubun),
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
					DenDay = getString(rec, "在庫計上日", "19010101"),
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
				var kakesaki = getCodeNameView<MasterTokui>(getString(rec, "取引先CD1")) ?? new(); // 掛先
				var meisaiList = BuildKinMeisaiList(rec);

				return new Tran06Nyukin() {
					DenDay = getString(rec, "在庫計上日", "19010101"),
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
					DenDay = getString(rec, "在庫計上日", "19010101"),
					CalcFlag = getCalcFlag(kubun),
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
					DenDay = getString(rec, "在庫計上日", "19010101"),
					CalcFlag = getCalcFlag(kubun),
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
					DenDay = getString(rec, "在庫計上日", "19010101"),
					CalcFlag = getCalcFlag(kubun),
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

				return new Tran12Jyuchu() {
					DenDay = getString(rec, "在庫計上日", "19010101"),
					Kubun = kubun,
					CalcFlag = getCalcFlag(kubun),
					RelateNo1 = getDataInt(rec, "関連伝票NO"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = getDataInt(rec, "明細金額合計"),
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
					Rate = getDataInt(rec, "掛率1"),
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
				var shiire = getCodeNameView<MasterTokui>(getString(rec, "取引先CD1")) ?? new();
				var kubun = getDataInt(rec, "取引区分");
				var meisaiList = BuildTranMeisaiList(rec);

				return new Tran13Hachu() {
					DenDay = getString(rec, "在庫計上日", "19010101"),
					Kubun = kubun,
					CalcFlag = getCalcFlag(kubun),
					RelateNo1 = getDataInt(rec, "関連伝票NO"),
					SuTotal = getDataInt(rec, "数量合計"),
					KingakuTotal = getDataInt(rec, "明細金額合計"),
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
					Rate = getDataInt(rec, "掛率1"),
				};
			});
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
	int getCalcFlag(int kubun) {
		return kubun switch {
			>= 20 and < 40 => -1,
			_ => 1,
		};
	}

	int getHeaderNebiki(Dictionary<string, object> rec) {
		return getDataInt(rec, "値引1") + getDataInt(rec, "値引2") + getDataInt(rec, "値引3");
	}

	List<Tran99Meisai>? BuildTranMeisaiList(Dictionary<string, object> rec, string table = "HC$tran_tori1") { // 棚卸は別テーブル
		var detailRows = _fromDb.Fetch<Dictionary<string, object>>($"select * from {table} where ヘッダNO=@0 order by 行NO", getDataInt(rec, "SEQ_NO"));
		if (detailRows.Count == 0)
			return null;

		List<Tran99Meisai> meisaiList = new(detailRows.Count);
		foreach (var detailRec in detailRows) {
			var shohinCode = getString(detailRec, "商品CD");
			var colCode = getString(detailRec, "色CD");
			var sizCode = getString(detailRec, "サイズCD");
			var shohin = getMaster<MasterShohin>(shohinCode);
			var col = getMeisho("COL", colCode);
			var siz = getMeisho("SIZ", sizCode);
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
	List<TranKinMeisai>? BuildKinMeisaiList(Dictionary<string, object> rec) {
		var detailRows = _fromDb.Fetch<Dictionary<string, object>>("select * from HC$tran_tori1 where ヘッダNO=@0 order by 行NO", getDataInt(rec, "SEQ_NO"));
		if (detailRows.Count == 0)
			return null;

		List<TranKinMeisai> meisaiList = new(detailRows.Count);
		foreach (var detailRec in detailRows) {
			var code = getString(detailRec, "明細取引区分");
			var kinKubun = getMeisho("PAY", code);

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

		_toDb.BeginTransaction();
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
