namespace CvBase;

/// <summary>
/// 店別売上表(DailyShopBudgetQueryViewModel)が使う「店舗×日」の集計結果1行。
/// Msg101_Op_Query の ItemType としてクライアント・サーバーの双方で解決できる共有DTOである。
/// Tran01Tenuri(実績・前年実績)と MasterYosanBrand(予算)は別テーブルのため、
/// UNION ALL で同じ形へ正規化してから C# 側で Id_Tenpo・Day をキーに合算する。
/// </summary>
public sealed class ShopDailySalesRow {
	/// <summary>店舗キー(MasterTokui.Id)</summary>
	public long Id_Tenpo { get; set; }
	/// <summary>日(1〜31)</summary>
	public int Day { get; set; }
	/// <summary>当年売上(Tran01Tenuri.KingakuTotal 合計)</summary>
	public long Sales { get; set; }
	/// <summary>予算(MasterYosanBrand.UriYosan のブランド横断合計)</summary>
	public long Budget { get; set; }
	/// <summary>前年売上(前年同月同日の Tran01Tenuri.KingakuTotal 合計)</summary>
	public long PrevSales { get; set; }
}
