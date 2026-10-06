namespace CvBase.Share;

/// <summary>ポイント付与対象金額の税基準（売価の外税・内税区分とは独立）。</summary>
[Comment("ポイント付与対象金額の税基準")]
public enum EnumPointTaxBasis : int {
	[Comment("税抜")]
	Exclusive = 0,
	[Comment("税込")]
	Inclusive = 1
}

/// <summary>ポイントの端数処理を適用する単位。</summary>
[Comment("ポイント計算単位")]
public enum EnumPointCalcUnit : int {
	[Comment("伝票")]
	Slip = 0,
	[Comment("明細")]
	Detail = 1
}

/// <summary>購入確定時に判定するボーナスの付与契機。</summary>
[Comment("ボーナスポイント付与契機")]
public enum EnumPointBonusTrigger : int {
	[Comment("期間内購入")]
	Purchase = 0,
	[Comment("誕生月購入")]
	BirthdayMonth = 1,
	[Comment("初回購入")]
	FirstPurchase = 2
}

/// <summary>ボーナスコードごとの付与回数を数える期間。会計年度は会社の年度開始日に従う。</summary>
[Comment("ボーナスポイント回数制限期間")]
public enum EnumPointLimitPeriod : int {
	[Comment("伝票")]
	Slip = 0,
	[Comment("適用期間")]
	Period = 1,
	[Comment("会計年度")]
	FiscalYear = 2,
	[Comment("生涯")]
	Lifetime = 3
}
