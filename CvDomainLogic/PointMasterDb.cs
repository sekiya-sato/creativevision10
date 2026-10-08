using CvBase;
using CvBase.Share;
using NPoco;
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace CvDomainLogic;

/// <summary>ポイント条件の保存検証。呼出元のSerializableトランザクション内で使用する。</summary>
public sealed class PointMasterDb(ExDatabase db) {
	public static bool IsMaster(Type type) => type == typeof(MasterPointBase) || type == typeof(MasterPointRank) || type == typeof(MasterPointBonus) || type == typeof(MasterPointCampaign);

	/// <param name="insert">追加(単件・一括)か。ポイント台帳は手動登録の追加だけを汎用CRUDで受け付ける</param>
	public static void EnsureGenericWriteAllowed(Type type, bool partialUpdate, bool insert = false) {
		if (type == typeof(TranPointEvent) && !insert)
			throw new ArgumentException("ポイント台帳は追記のみです。訂正は取消を登録してください。");
		if (type == typeof(SummaryPoint))
			throw new ArgumentException("ポイント残高は専用処理で保存してください。");
		if (PointCampaignDb.IsTarget(type))
			throw new ArgumentException("キャンペーン対象は店舗別・商品店舗別の設定画面で保存してください。");
		if (partialUpdate && IsMaster(type))
			throw new ArgumentException("ポイント条件は部分更新できません。レコード全体を更新してください。");
	}

	// 重複検査の除外IdはDBから読んだ更新前行だけを使い、追加要求のIdは信用しない。
	public void ValidateSave(object item, BaseDbClass? previous) {
		switch (item) {
			case MasterPointBase row:
				ValidateBase(row, previous);
				break;
			case MasterPointRank row:
				ValidateRank(row, previous as MasterPointRank);
				break;
			case MasterPointBonus row:
				ValidateBonus(row, previous as MasterPointBonus);
				break;
			case MasterPointCampaign row:
				new PointCampaignDb(db).ValidateSave(row, previous as MasterPointCampaign);
				break;
			case TranPointEvent row:
				new PointLedgerDb(db).ValidateManual(row);
				break;
		}
	}

	public void ValidateDelete(BaseDbClass item) {
		switch (item) {
			case MasterPointBase row:
				Require(!IsBaseUsed(row.Id), "使用済みベース版は削除できません。");
				Require(!Exists<MasterPointRank>("Id_PointBase=@0", row.Id) && !Exists<MasterPointBonus>("Id_PointBase=@0", row.Id) && !Exists<MasterPointCampaign>("Id_PointBase=@0", row.Id), "子ランク・ボーナス・キャンペーンが存在するベース版は削除できません。");
				break;
			case MasterPointRank row:
				Require(!IsBaseUsed(row.Id_PointBase) && !EventExists("Id_PointRank=@0", row.Id), "使用済みランク条件は削除できません。");
				Require(!RankReferenced(row), "ボーナス・キャンペーンが参照するランクは削除できません。");
				break;
			case MasterPointBonus row:
				Require(!IsBaseUsed(row.Id_PointBase) && !EventExists("Id_PointBonus=@0", row.Id), "使用済みボーナス版は削除できません。");
				break;
			case MasterPointCampaign row:
				new PointCampaignDb(db).ValidateDelete(row);
				break;
		}
	}

	private void ValidateBase(MasterPointBase row, BaseDbClass? previous) {
		Text(row.Code, 20, "制度コード"); Text(row.Name, 80, "制度名称");
		Require(row.Version >= 1, "版番号は1以上で指定してください。");
		Period(row.DayFrom, row.DayTo);
		EnumValue<EnumYesNo>(row.IsEnabled, "有効"); EnumValue<EnumYesNo>(row.DeductPointUse, "利用ポイント控除");
		EnumValue<EnumPointTaxBasis>(row.TaxBasis, "税基準"); EnumValue<EnumPointCalcUnit>(row.CalcUnit, "計算単位"); EnumValue<EnumRounding>(row.Rounding, "端数処理");
		Amounts(row.PointUnitPrice, row.PointAmountProper, row.PointAmountSale);
		Require(row.ExpireMonths is >= 0 and <= 120, "失効月数は0～120で指定してください。");
		if (previous != null && IsBaseUsed(previous.Id)) Require(SameConditions(row, previous, true), "使用済みベース版は有効フラグ・失効月数以外を変更できません。");
		Require(!Exists<MasterPointBase>("Code=@0 AND Version=@1 AND Id<>@2", row.Code, row.Version, previous?.Id ?? 0), "同じ制度コード・版が存在します。");
		if (row.IsEnabled == (int)EnumYesNo.Yes)
			Require(!Exists<MasterPointBase>("Code=@0 AND IsEnabled=1 AND Id<>@1 AND DayFrom<=@2 AND DayTo>=@3", row.Code, previous?.Id ?? 0, row.DayTo, row.DayFrom), "有効なベース版の適用期間が重複しています。");
		if (previous != null)
			foreach (var child in Fetch<MasterPointBonus>("Id_PointBase=@0", previous.Id)) Contained(child.DayFrom, child.DayTo, row);
		if (previous != null)
			Require(!Exists<MasterPointCampaign>("Id_PointBase=@0 AND (DayFrom<@1 OR DayTo>@2)", previous.Id, row.DayFrom, row.DayTo), "キャンペーンの適用期間がベース版の期間外になります。");
	}

	private void ValidateRank(MasterPointRank row, MasterPointRank? previous) {
		Parent(row.Id_PointBase);
		Text(row.Name, 40, "ランク名称");
		Require(row.Kubun >= 0, "ランクコードは0以上で指定してください。");
		Amounts(row.PointUnitPrice, row.PointAmountProper, row.PointAmountSale);
		var used = IsBaseUsed(row.Id_PointBase) || (previous != null && (IsBaseUsed(previous.Id_PointBase) || EventExists("Id_PointRank=@0", previous.Id)));
		if (used) Require(previous != null && SameConditions(row, previous, false), "使用済みベース版のランク条件は追加・変更できません。");
		if (previous != null && (row.Id_PointBase != previous.Id_PointBase || row.Kubun != previous.Kubun))
			Require(!RankReferenced(previous), "ボーナス・キャンペーンが参照するランクの親・コードは変更できません。");
		Require(!Exists<MasterPointRank>("Id_PointBase=@0 AND Kubun=@1 AND Id<>@2", row.Id_PointBase, row.Kubun, previous?.Id ?? 0), "同じベース版・ランクコードが存在します。");
	}

	private void ValidateBonus(MasterPointBonus row, MasterPointBonus? previous) {
		Text(row.Code, 20, "ボーナスコード"); Text(row.Name, 80, "ボーナス名称");
		Require(row.Version >= 1, "版番号は1以上で指定してください。");
		Period(row.DayFrom, row.DayTo);
		var parent = Parent(row.Id_PointBase);
		Contained(row.DayFrom, row.DayTo, parent);
		EnumValue<EnumYesNo>(row.IsEnabled, "有効"); EnumValue<EnumYesNo>(row.IsAllRanks, "全ランク対象");
		EnumValue<EnumPointBonusTrigger>(row.TriggerType, "付与契機"); EnumValue<EnumPointLimitPeriod>(row.LimitPeriodType, "回数制限期間");
		Require(row.PointAmount > 0 && row.MinimumKingaku >= 0 && row.LimitCount >= 1, "追加ポイントは正数、最低金額は0以上、回数上限は1以上で指定してください。");
		if (row.IsAllRanks == (int)EnumYesNo.Yes) Require(row.RankKubun == 0, "全ランク対象の場合、対象ランクコードは0で指定してください。");
		else Require(Exists<MasterPointRank>("Id_PointBase=@0 AND Kubun=@1", row.Id_PointBase, row.RankKubun), "対象ランクが同じベース版に存在しません。");
		var used = IsBaseUsed(row.Id_PointBase) || (previous != null && (IsBaseUsed(previous.Id_PointBase) || EventExists("Id_PointBonus=@0", previous.Id)));
		if (used) Require(previous != null && SameConditions(row, previous, true), "使用済みボーナス版は追加・条件変更できません。有効フラグのみ変更可能です。");
		Require(!Exists<MasterPointBonus>("Code=@0 AND Version=@1 AND Id<>@2", row.Code, row.Version, previous?.Id ?? 0), "同じボーナスコード・版が存在します。");
		if (row.IsEnabled == (int)EnumYesNo.Yes)
			Require(!Exists<MasterPointBonus>("Code=@0 AND IsEnabled=1 AND Id<>@1 AND DayFrom<=@2 AND DayTo>=@3", row.Code, previous?.Id ?? 0, row.DayTo, row.DayFrom), "有効なボーナス版の適用期間が重複しています。");
	}

	private MasterPointBase Parent(long id) {
		Require(id > 0, "ベース版を指定してください。");
		return Fetch<MasterPointBase>("Id=@0", id).FirstOrDefault() ?? throw new ArgumentException("指定されたベース版が存在しません。");
	}

	private bool RankReferenced(MasterPointRank row) => Exists<MasterPointBonus>("Id_PointBase=@0 AND IsAllRanks=0 AND RankKubun=@1", row.Id_PointBase, row.Kubun)
		|| Exists<MasterPointCampaign>("Id_PointBase=@0 AND RankKubun=@1", row.Id_PointBase, row.Kubun);
	private bool IsBaseUsed(long id) => id > 0 && EventExists("Id_PointBase=@0 OR Id_PointRank IN (SELECT Id FROM MasterPointRank WHERE Id_PointBase=@0) OR Id_PointBonus IN (SELECT Id FROM MasterPointBonus WHERE Id_PointBase=@0)", id);
	private bool EventExists(string where, params object[] args) => Exists<TranPointEvent>(where, args);
	private bool Exists<T>(string where, params object[] args) => db.FetchDialect<int>($"SELECT 1 FROM {db.GetTableName(typeof(T))} WHERE {where} LIMIT 1", args).Any();
	private System.Collections.Generic.List<T> Fetch<T>(string where, params object[] args) => db.FetchDialect<T>($"SELECT * FROM {db.GetTableName(typeof(T))} WHERE {where}", args);

	// 監査値とenum表示用プロパティは条件比較から除外する。
	private static bool SameConditions(BaseDbClass row, BaseDbClass previous, bool allowEnabled) => row.GetType().GetProperties()
		.Where(p => p.GetCustomAttribute<IgnoreAttribute>() == null && p.GetCustomAttribute<ResultColumnAttribute>() == null && p.GetCustomAttribute<ComputedColumnAttribute>() == null && p.Name is not nameof(BaseDbClass.Id) and not nameof(BaseDbClass.Vdc) and not nameof(BaseDbClass.Vdu) && (!allowEnabled || p.Name != nameof(MasterPointBase.IsEnabled))
			// 失効月数は付与結果に影響しないため使用済みでも変更できる
			&& p.Name != nameof(MasterPointBase.ExpireMonths))
		.All(p => Equals(p.GetValue(row), p.GetValue(previous)));
	private static void Text(string value, int max, string name) => Require(!string.IsNullOrWhiteSpace(value) && value.Length <= max && value == value.Trim(), $"{name}は前後空白なしの1～{max}文字で指定してください。");
	private static void Period(string from, string to) => Require(Date(from) && Date(to) && string.CompareOrdinal(from, to) <= 0, "適用期間は実在する日付(yyyyMMdd)で開始日以前にならない終了日を指定してください。");
	private static bool Date(string value) => value?.Length == 8 && DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
	private static void Contained(string from, string to, MasterPointBase parent) => Require(string.CompareOrdinal(from, parent.DayFrom) >= 0 && string.CompareOrdinal(to, parent.DayTo) <= 0, "ボーナスの適用期間はベース版の期間内にしてください。");
	private static void Amounts(long price, long proper, long sale) => Require(price > 0 && proper >= 0 && sale >= 0, "付与単価は正数、付与ポイントは0以上で指定してください。");
	private static void EnumValue<T>(int value, string name) where T : struct, Enum => Require(Enum.IsDefined(typeof(T), value), $"{name}の区分が不正です。");
	private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
