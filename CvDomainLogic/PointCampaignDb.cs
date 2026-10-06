using CvBase;
using CvBase.Share;
using System.Globalization;

namespace CvDomainLogic;

/// <summary>
/// ポイントキャンペーンの保存検証と対象（店舗・商品）の置換。呼出元のSerializableトランザクション内で使用する。
/// 重複は同じ優先区分・双方有効・期間重複・ランク衝突(どちらかが0または同値)・同じ対象で判定する。
/// </summary>
public sealed class PointCampaignDb(ExDatabase db) {
	public const int TargetShop = 1;
	public const int TargetShohin = 2;

	public static bool IsTarget(Type type) => type == typeof(MasterPointCampaignShop) || type == typeof(MasterPointCampaignShohin);

	public void ValidateSave(MasterPointCampaign row, MasterPointCampaign? previous) {
		Text(row.Code, 20, "キャンペーンコード"); Text(row.Name, 80, "キャンペーン名称");
		Require((row.Memo ?? string.Empty).Length <= 200, "備考は200文字以内で指定してください。");
		Require(Date(row.DayFrom) && Date(row.DayTo) && string.CompareOrdinal(row.DayFrom, row.DayTo) <= 0, "適用期間は実在する日付(yyyyMMdd)で開始日以前にならない終了日を指定してください。");
		Require(Enum.IsDefined(typeof(EnumYesNo), row.IsEnabled), "有効の区分が不正です。");
		Require(Enum.IsDefined(typeof(EnumPointCampaignPriority), row.PriorityType), "優先区分が不正です。");
		Require(row.PointUnitPrice > 0 && row.PointAmountProper >= 0 && row.PointAmountSale >= 0, "付与単価は正数、付与ポイントは0以上で指定してください。");
		Require(row.Id_PointBase > 0, "ベース版を指定してください。");
		var parent = Fetch<MasterPointBase>("Id=@0", row.Id_PointBase).FirstOrDefault() ?? throw new ArgumentException("指定されたベース版が存在しません。");
		Require(string.CompareOrdinal(row.DayFrom, parent.DayFrom) >= 0 && string.CompareOrdinal(row.DayTo, parent.DayTo) <= 0, "キャンペーンの適用期間はベース版の期間内にしてください。");
		Require(row.RankKubun >= 0, "対象ランクコードは0以上で指定してください。");
		if (row.RankKubun != 0)
			Require(Exists<MasterPointRank>("Id_PointBase=@0 AND Kubun=@1", row.Id_PointBase, row.RankKubun), "対象ランクが同じベース版に存在しません。");
		Require(!Exists<MasterPointCampaign>("Code=@0 AND Id<>@1", row.Code, previous?.Id ?? 0), "同じキャンペーンコードが存在します。");
		if (previous == null) return;
		var shops = TargetIds<MasterPointCampaignShop>(previous.Id);
		var shohins = TargetIds<MasterPointCampaignShohin>(previous.Id);
		if (row.PriorityType != previous.PriorityType)
			Require(shops.Count == 0 && shohins.Count == 0, "対象店舗・商品が設定されているキャンペーンの優先区分は変更できません。対象を解除してから変更してください。");
		// マスタ画面の変更で生じた重複は自動削除せず拒否する（対象設定画面だけが相手を置換する）。
		var conflicts = Conflicts(row, previous.Id, shops, shohins);
		Require(conflicts.Count == 0, $"期間が重なる他キャンペーンと対象が重複します: {string.Join("、", conflicts.Select(x => x.Code).Distinct())}");
	}

	public void ValidateDelete(MasterPointCampaign row) =>
		Require(!Exists<MasterPointCampaignShop>("Id_PointCampaign=@0", row.Id) && !Exists<MasterPointCampaignShohin>("Id_PointCampaign=@0", row.Id), "対象店舗・商品が設定されているキャンペーンは削除できません。対象を解除してから削除してください。");

	/// <summary>対象を確認または置換する。更新時は重複先の該当行を削除し、キャンペーンのVduを進める。</summary>
	public PointCampaignTargetResult SaveTargets(PointCampaignTargetParameter param, long vdate) {
		var campaign = Fetch<MasterPointCampaign>("Id=@0", param.Id_PointCampaign).FirstOrDefault();
		if (campaign == null || campaign.Vdu != param.Vdu)
			throw new PointCampaignConcurrencyException();
		var shops = param.Ids_Tenpo.Distinct().ToList();
		var shohins = param.Ids_Shohin.Distinct().ToList();
		ValidateTargets(campaign.EnPriorityType, shops, shohins);
		var conflicts = Conflicts(campaign, campaign.Id, shops, shohins);
		if (param.IsPreview) return new PointCampaignTargetResult { Conflicts = conflicts, Vdu = campaign.Vdu };

		// 確認後に他端末が対象・期間を変えた場合は、利用者が見ていない削除をしない。
		Require(conflicts.OrderBy(Key).SequenceEqual(param.Confirmed.OrderBy(Key)), "確認後に他キャンペーンの設定が変更されました。もう一度登録してください。");
		var shopTable = db.GetTableName(typeof(MasterPointCampaignShop));
		var shohinTable = db.GetTableName(typeof(MasterPointCampaignShohin));
		foreach (var c in conflicts) {
			if (c.TargetKind == TargetShop) db.ExecuteDialect($"DELETE FROM {shopTable} WHERE Id_PointCampaign=@0 AND Id_Tenpo=@1", c.Id_PointCampaign, c.Id_Target);
			else db.ExecuteDialect($"DELETE FROM {shohinTable} WHERE Id_PointCampaign=@0 AND Id_Shohin=@1", c.Id_PointCampaign, c.Id_Target);
		}
		db.ExecuteDialect($"DELETE FROM {shopTable} WHERE Id_PointCampaign=@0", campaign.Id);
		db.ExecuteDialect($"DELETE FROM {shohinTable} WHERE Id_PointCampaign=@0", campaign.Id);
		foreach (var id in shops) db.Insert(new MasterPointCampaignShop { Id_PointCampaign = campaign.Id, Id_Tenpo = id, Vdc = vdate, Vdu = vdate });
		foreach (var id in shohins) db.Insert(new MasterPointCampaignShohin { Id_PointCampaign = campaign.Id, Id_Shohin = id, Vdc = vdate, Vdu = vdate });
		// 重複先も対象が変わるため、画面が保持する相手のVduを無効化する。
		foreach (var id in conflicts.Select(x => x.Id_PointCampaign).Append(campaign.Id).Distinct())
			db.ExecuteDialect($"UPDATE {db.GetTableName(typeof(MasterPointCampaign))} SET Vdu=@0 WHERE Id=@1", vdate, id);
		return new PointCampaignTargetResult { Conflicts = conflicts, Vdu = vdate };
	}

	private void ValidateTargets(EnumPointCampaignPriority priority, List<long> shops, List<long> shohins) {
		var needShop = priority is EnumPointCampaignPriority.Shop or EnumPointCampaignPriority.ShohinShop;
		var needShohin = priority is EnumPointCampaignPriority.ShohinAllShops or EnumPointCampaignPriority.ShohinShop;
		Require(needShop || shops.Count == 0, "この優先区分では対象店舗を設定できません。");
		Require(needShohin || shohins.Count == 0, "この優先区分では対象商品を設定できません。");
		Require(shops.Count == 0 || CountIn<MasterTokui>(shops, "TenType IN (3,6)") == shops.Count, "対象店舗に存在しない店舗が含まれています。");
		Require(shohins.Count == 0 || CountIn<MasterShohin>(shohins, "1=1") == shohins.Count, "対象商品に存在しない商品が含まれています。");
	}

	private List<PointCampaignConflict> Conflicts(MasterPointCampaign row, long selfId, List<long> shops, List<long> shohins) {
		if (row.IsEnabled != (int)EnumYesNo.Yes || row.EnPriorityType == EnumPointCampaignPriority.AllShops) return [];
		var others = Fetch<MasterPointCampaign>("Id<>@0 AND IsEnabled=1 AND PriorityType=@1 AND DayFrom<=@2 AND DayTo>=@3 AND (@4=0 OR RankKubun=0 OR RankKubun=@4)",
			selfId, row.PriorityType, row.DayTo, row.DayFrom, row.RankKubun);
		var result = new List<PointCampaignConflict>();
		var shopSet = shops.ToHashSet();
		var shohinSet = shohins.ToHashSet();
		foreach (var other in others) {
			switch (row.EnPriorityType) {
				case EnumPointCampaignPriority.Shop:
					result.AddRange(TargetIds<MasterPointCampaignShop>(other.Id).Where(shopSet.Contains).Select(id => Conflict(other, TargetShop, id)));
					break;
				case EnumPointCampaignPriority.ShohinAllShops:
					result.AddRange(TargetIds<MasterPointCampaignShohin>(other.Id).Where(shohinSet.Contains).Select(id => Conflict(other, TargetShohin, id)));
					break;
				case EnumPointCampaignPriority.ShohinShop:
					// 商品×店舗の組合せで重なれば、旧仕様どおり相手から商品単位で外す。
					if (TargetIds<MasterPointCampaignShop>(other.Id).Any(shopSet.Contains))
						result.AddRange(TargetIds<MasterPointCampaignShohin>(other.Id).Where(shohinSet.Contains).Select(id => Conflict(other, TargetShohin, id)));
					break;
			}
		}
		FillTargetNames(result);
		return result;
	}

	private static PointCampaignConflict Conflict(MasterPointCampaign other, int kind, long id) => new() {
		Id_PointCampaign = other.Id, Code = other.Code, Name = other.Name, DayFrom = other.DayFrom, DayTo = other.DayTo, TargetKind = kind, Id_Target = id,
	};

	private void FillTargetNames(List<PointCampaignConflict> list) {
		var names = new Dictionary<(int, long), (string Code, string Name)>();
		foreach (var (kind, type) in new[] { (TargetShop, typeof(MasterTokui)), (TargetShohin, typeof(MasterShohin)) }) {
			var ids = list.Where(x => x.TargetKind == kind).Select(x => x.Id_Target).Distinct().ToList();
			foreach (var chunk in ids.Chunk(500))
				foreach (var r in db.FetchDialect<CodeNameRow>($"SELECT Id, Code, Name FROM {db.GetTableName(type)} WHERE Id IN ({string.Join(",", chunk)})"))
					names[(kind, r.Id)] = (r.Code, r.Name);
		}
		for (var i = 0; i < list.Count; i++)
			if (names.TryGetValue((list[i].TargetKind, list[i].Id_Target), out var n)) list[i] = list[i] with { TargetCode = n.Code, TargetName = n.Name };
	}

	private sealed class CodeNameRow {
		public long Id { get; set; }
		public string Code { get; set; } = string.Empty;
		public string Name { get; set; } = string.Empty;
	}

	private static (long, int, long) Key(PointCampaignConflict c) => (c.Id_PointCampaign, c.TargetKind, c.Id_Target);
	private List<long> TargetIds<T>(long campaignId) => db.FetchDialect<long>(
		$"SELECT {(typeof(T) == typeof(MasterPointCampaignShop) ? "Id_Tenpo" : "Id_Shohin")} FROM {db.GetTableName(typeof(T))} WHERE Id_PointCampaign=@0", campaignId);
	// Idは数値リストのため直接埋め込む（件数が多くてもバインド数上限に掛からない）。
	private int CountIn<T>(List<long> ids, string where) => ids.Chunk(500).Sum(chunk =>
		db.FetchDialect<int>($"SELECT COUNT(*) FROM {db.GetTableName(typeof(T))} WHERE Id IN ({string.Join(",", chunk)}) AND {where}").Single());
	private bool Exists<T>(string where, params object[] args) => db.FetchDialect<int>($"SELECT 1 FROM {db.GetTableName(typeof(T))} WHERE {where} LIMIT 1", args).Any();
	private List<T> Fetch<T>(string where, params object[] args) => db.FetchDialect<T>($"SELECT * FROM {db.GetTableName(typeof(T))} WHERE {where}", args);
	private static void Text(string value, int max, string name) => Require(!string.IsNullOrWhiteSpace(value) && value.Length <= max && value == value.Trim(), $"{name}は前後空白なしの1～{max}文字で指定してください。");
	private static bool Date(string value) => value?.Length == 8 && DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
	private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}

/// <summary>対象保存時のキャンペーンVdu不一致。</summary>
public sealed class PointCampaignConcurrencyException() : Exception("他の端末でキャンペーンが更新されました。再取得してください。");
