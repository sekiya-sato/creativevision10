namespace CvBase;

/// <summary>
/// 店舗1件の棚卸の進行状況。棚卸開始処理・棚卸確定処理の画面が店舗一覧に出す(設計書2.5)。
/// <para>
/// サーバ(<c>StocktakeDb.FetchRefixStatus</c>)が組み立て、`Msg060_StocktakeStatus` で画面へ返す。
/// クライアントと共有するので <c>CvBase</c> に置く。
/// </para>
/// </summary>
public sealed class StocktakeShopStatus {
	/// <summary>店舗Id</summary>
	public long Id_Soko { get; set; }
	/// <summary>棚卸基準日 yyyyMMdd</summary>
	public string TanaDay { get; set; } = string.Empty;
	/// <summary>計上月 yyyyMM。締日が末日でなければ棚卸日の翌月になることがある(設計書2.1)</summary>
	public string SumMonth { get; set; } = string.Empty;
	/// <summary>最終確定日 yyyyMMdd。未確定なら <see cref="StocktakeDaySet.UnsetDay"/></summary>
	public string FixDay { get; set; } = StocktakeDaySet.UnsetDay;
	/// <summary>棚卸日が未設定で計上月末へフォールバックしたか</summary>
	public bool IsFallback { get; set; }
	/// <summary>この基準日で棚卸開始処理が済んでいるか</summary>
	public bool IsStarted { get; set; }
	/// <summary>棚卸確定処理が済んでいるか</summary>
	public bool IsFixed { get; set; }
	/// <summary>確定後に基準日以前の伝票が修正され、再確定が必要か</summary>
	public bool IsRefixRequired { get; set; }
}

/// <summary>
/// 基準日以外の日付で入力された棚卸伝票の1行。
/// <para>
/// 棚卸確定処理は実棚数を基準日と厳密一致で集計する(設計書2.3)ため、日付違いの入力は集計から漏れる。
/// 確定処理の前にこれを検知して「基準日へ補正するか」を利用者に確認する(設計書4)。
/// </para>
/// </summary>
public sealed class StocktakeMisdated {
	/// <summary>店舗Id</summary>
	public long Id_Soko { get; set; }
	/// <summary>棚卸入力の計上日 yyyyMMdd</summary>
	public string DenDay { get; set; } = string.Empty;
	/// <summary>その日付の棚卸伝票の件数</summary>
	public int SlipCount { get; set; }
}

/// <summary>
/// 棚卸の店舗別状況照会(`Msg060_StocktakeStatus`)の応答。
/// 棚卸開始処理・棚卸確定処理の画面が店舗一覧を組み立てるのに使う。
/// </summary>
public sealed class StocktakeStatusReply {
	/// <summary>店舗別の進行状況</summary>
	public List<StocktakeShopStatus> Shops { get; set; } = [];
	/// <summary>基準日以外の日付で入力された棚卸伝票の内訳(全対象店舗ぶん)</summary>
	public List<StocktakeMisdated> Misdated { get; set; } = [];
}

/// <summary>
/// 基準日以外の棚卸入力があるため棚卸確定処理を中断したことを表す例外。
/// <para>
/// ストリーム実行(`Msg055_StocktakeFix`)は件数(int)しか返せないので、確認が必要な中断はこの例外で表に出す。
/// 何も変更せずに投げるため、受け取った側は補正の可否を確認して
/// <see cref="StocktakeParameter.AlignMisdated"/> を true にして呼び直せばよい。
/// </para>
/// </summary>
public sealed class StocktakeMisdatedException(List<StocktakeMisdated> misdated)
	: Exception(BuildMessage(misdated)) {
	/// <summary>基準日以外の日付で入力された棚卸伝票の内訳</summary>
	public List<StocktakeMisdated> Misdated { get; } = misdated;

	private static string BuildMessage(List<StocktakeMisdated> misdated) {
		var slips = misdated.Sum(x => x.SlipCount);
		var days = string.Join("、", misdated.Select(x => $"{x.DenDay}({x.SlipCount}件)"));
		return $"棚卸基準日以外の日付で入力された棚卸伝票が {slips} 件あります。{days} "
			+ "計上日を基準日へ補正するか確認してから再実行してください。";
	}
}

/// <summary>棚卸確定処理の結果</summary>
public sealed class StocktakeFixResult {
	/// <summary>生成した在庫調整伝票の件数</summary>
	public int SlipCount { get; set; }
	/// <summary>
	/// 基準日以外の日付で入力された棚卸伝票。<see cref="IsConfirmationRequired"/> が true のときは
	/// 確定処理を実行していない(何も変更していない)。
	/// </summary>
	public List<StocktakeMisdated> Misdated { get; set; } = [];
	/// <summary>基準日へ補正した棚卸伝票の件数</summary>
	public int AlignedCount { get; set; }
	/// <summary>日付補正の確認が必要で確定処理を中断したか</summary>
	public bool IsConfirmationRequired => Misdated.Count > 0 && AlignedCount == 0 && SlipCount == 0;
}
