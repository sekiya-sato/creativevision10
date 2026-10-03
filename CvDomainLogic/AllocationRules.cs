using CvBase;

namespace CvDomainLogic;

/// <summary>
/// 配分確定時の在庫検査に使う、倉庫+SKU単位の集計値。
/// </summary>
/// <param name="Id_Soko">出庫元倉庫</param>
/// <param name="Id_Shohin">商品</param>
/// <param name="Id_Col">色</param>
/// <param name="Id_Siz">サイズ</param>
/// <param name="RealSu">実在庫（<see cref="SummaryRealStock.Su"/>。行が無ければ0）</param>
/// <param name="ReserveQty">引当数（<see cref="SummaryRealStock.ReserveQty"/>。行が無ければ0）</param>
/// <param name="OwnReserved">確定対象の行が既に引当数へ積んでいる数量</param>
/// <param name="CommitSu">今回の確定数の合計</param>
public readonly record struct CommitStockInput(long Id_Soko, long Id_Shohin, long Id_Col, long Id_Siz,
	int RealSu, int ReserveQty, int OwnReserved, int CommitSu);

/// <summary>
/// 配分（<see cref="TranHaibun"/>）の制約判定。DBに依存しない純粋関数だけを置く。
/// <para>
/// 配分保存（<c>HaibunSaveParam</c>）と配分確定（<see cref="ShippingDb.Commit"/>）が同じ判定を使うため、
/// 修正可能条件・作成／確定できる区分・確定時の在庫検査をここへ集める。
/// 仕様は `Doc/spec/2026-10-03_配分再設計_Step1_共通基盤・確定一本化_詳細設計.md` 3章を参照する。
/// </para>
/// </summary>
public static class AllocationRules {
	/// <summary>
	/// 修正（洗い替えでの削除）できる行か。<see cref="TranHaibun.EditableWhereSql"/> と同じ条件。
	/// </summary>
	public static bool IsEditable(TranHaibun row) =>
		row.SendFlg == 0 && row.EndFlag == 0 && string.IsNullOrEmpty(row.KakuteiDay);

	/// <summary>
	/// 新規作成できる区分か。仕入配分(0)・在庫配分(1)・受注配分(2)・取置配分(6) の4区分だけで、
	/// 得意先別(3)・店舗出荷依頼(4)・在庫品(5)・移動指示(7) は廃止した（決定 D2）。
	/// </summary>
	public static bool IsCreatableKubun(int kubun) => (EnumHaibun)kubun is
		EnumHaibun.Hatsukai or EnumHaibun.Zaiko or EnumHaibun.Juchu or EnumHaibun.Reservation;

	/// <summary>
	/// 配分確定（出荷売上／移動伝票の作成）で扱える区分か。
	/// 取置配分(6)は店舗売上へ変換する別経路（Step 5）なので対象外。
	/// </summary>
	public static bool IsCommittableKubun(int kubun) => (EnumHaibun)kubun is
		EnumHaibun.Hatsukai or EnumHaibun.Zaiko or EnumHaibun.Juchu;

	/// <summary>
	/// 新しく登録する行の入力検査。違反が無ければ null、あれば画面へそのまま出せるメッセージを返す。
	/// </summary>
	public static string? ValidateNewRow(TranHaibun row) {
		if (row.Su <= 0) {
			return "配分数は1以上で登録してください。";
		}
		if (row.Id_Soko <= 0 || row.Id_Tenpo <= 0 || row.Id_Shohin <= 0) {
			return "出庫元・配分先・商品が指定されていない配分は登録できません。";
		}
		if (!IsCreatableKubun(row.Kubun)) {
			return $"配分区分 {row.Kubun} は新規に登録できません。";
		}
		// 仕入配分は発注の入荷数で引当・確定を判定するため、発注に紐付かない行は作らせない（Step 4 3.4）
		if (row.Kubun == (int)EnumHaibun.Hatsukai && row.RelateNo1 <= 0) {
			return "仕入配分は発注に紐付けて登録してください。";
		}
		if (row.Kubun == (int)EnumHaibun.Reservation) {
			return ValidateReservation(row);
		}
		return null;
	}

	/// <summary>
	/// 取置配分(区分6)の入力検査（Step 5 4.1）。顧客・期限日が必須で、在庫は店舗自身（出庫元 = 店舗）で押さえる。
	/// 有効在庫の不足は保存を止めない（判断 2。画面で警告する）。
	/// </summary>
	static string? ValidateReservation(TranHaibun row) {
		if (row.Id_Customer <= 0) {
			return "取置は顧客を指定して登録してください。";
		}
		if (!IsYmd(row.DenDay) || !IsYmd(row.LimitDay)) {
			return "取置日・期限日を yyyyMMdd で指定してください。";
		}
		if (string.CompareOrdinal(row.LimitDay, row.DenDay) < 0) {
			return "期限日は取置日以降の日付にしてください。";
		}
		if (row.Id_Soko != row.Id_Tenpo) {
			return "取置は店舗自身の在庫で登録してください（出庫元と店舗を同じにする）。";
		}
		if (row.RelateNo1 != 0) {
			return "取置に元伝票は紐付けられません。";
		}
		return null;
	}

	/// <summary>yyyyMMdd の実在する日付か</summary>
	static bool IsYmd(string? s) =>
		s is { Length: 8 } && DateTime.TryParseExact(s, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
			System.Globalization.DateTimeStyles.None, out _);

	/// <summary>
	/// 取置の期限日の初期値（取置日の1週間後。決定 D11）。取置日が yyyyMMdd でなければ空文字。
	/// </summary>
	public static string DefaultLimitDay(string denDay) =>
		DateTime.TryParseExact(denDay, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
			System.Globalization.DateTimeStyles.None, out var d)
			? d.AddDays(7).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)
			: string.Empty;

	/// <summary>
	/// 新しく登録する行の状態列を初期値（未確定・未送信・未完了）に揃える。
	/// 画面から状態列が送られても、登録直後の配分は必ず未確定にする。
	/// </summary>
	public static void NormalizeNewRow(TranHaibun row) {
		row.Id = 0;
		row.KakuteiDay = string.Empty;
		row.EndFlag = 0;
		row.JitsuSu = 0;
		row.ShortSu = 0;
		row.RelateNo2 = 0;
		row.SendFlg = 0;
		row.ArrivedSu = 0;
		row.EndReason = (int)EnumHaibunEndReason.Normal;
	}

	/// <summary>確定数を 0〜指示数 に収める</summary>
	public static int ClampCommitSu(int requested, int su) => Math.Clamp(requested, 0, Math.Max(su, 0));

	/// <summary>
	/// 引当数へ積んでいる数量。<c>SummaryDb.ReserveTargetWhere</c> / <c>SummaryDb.ReserveQtySumExpr</c> と同じ式で、
	/// 完了行は0、仕入配分(0)は入荷済み数、それ以外は未確定なら指示数、確定済み（旧状態）なら実数量。
	/// </summary>
	public static int ReservedQty(TranHaibun row) {
		if (row.EndFlag != 0) {
			return 0;
		}
		if (row.Kubun == (int)EnumHaibun.Hatsukai) {
			return row.ArrivedSu;
		}
		return string.IsNullOrEmpty(row.KakuteiDay) ? row.Su : row.JitsuSu;
	}

	/// <summary>
	/// 入荷済み数を超えて確定しようとしている仕入配分(区分0)の行を返す（空なら確定できる）。
	/// </summary>
	/// <param name="rows">確定対象の行（入荷割当を計算し直した直後の値）</param>
	/// <param name="commitSu">行Id → 確定数</param>
	public static IReadOnlyList<TranHaibun> FindNotArrived(IEnumerable<TranHaibun> rows, IReadOnlyDictionary<long, int> commitSu) =>
		[.. rows.Where(r => r.Kubun == (int)EnumHaibun.Hatsukai && commitSu.GetValueOrDefault(r.Id) > r.ArrivedSu)];

	/// <summary>
	/// 確定時の在庫検査。有効在庫を割る倉庫+SKUを返す（空なら確定できる）。
	/// <para>
	/// <c>有効在庫(確定前) = 実在庫 − (引当数 − 確定対象の引当分)</c> とし、<c>有効在庫(確定前) − 確定数 &lt; 0</c> を割れとする。
	/// 確定数は自分の引当分（仕入配分は入荷済み数）を除いた有効在庫から差し引き、欠品になる数は在庫を使わないので差し引かない。
	/// 確定数0（全量欠品）の倉庫+SKUは検査しない。
	/// </para>
	/// </summary>
	public static IReadOnlyList<ShippingConfirmError> FindShortages(IEnumerable<CommitStockInput> inputs) =>
		[.. inputs
			.Where(x => x.CommitSu > 0)
			.Select(x => (x, Yuko: x.RealSu - (x.ReserveQty - x.OwnReserved)))
			.Where(t => t.Yuko - t.x.CommitSu < 0)
			.OrderBy(t => t.x.Id_Soko).ThenBy(t => t.x.Id_Shohin).ThenBy(t => t.x.Id_Col).ThenBy(t => t.x.Id_Siz)
			.Select(t => new ShippingConfirmError(t.x.Id_Soko, t.x.Id_Shohin, t.x.Id_Col, t.x.Id_Siz, t.x.CommitSu, t.Yuko))];
}
