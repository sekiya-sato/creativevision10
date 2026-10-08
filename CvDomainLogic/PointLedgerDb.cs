using System.Globalization;
using CvAsset;
using CvBase;
using CvBase.Share;

namespace CvDomainLogic;

/// <summary>
/// ポイント台帳(TranPointEvent)の手動登録（手動付与・手動使用・調整・手動失効・取消）。
/// <para>
/// 汎用CRUDの追加(単件・一括)だけを受け付け、更新・削除・部分更新は禁止する（台帳は追記のみ。訂正は取消を追記する）。
/// 店舗売上・失効処理が作る行(Id_Tenuri&gt;0・EXPIRE:等)とは EventKey の接頭辞「MANUAL:」で区別し、取消できるのは手動行だけとする。
/// 保存と同じトランザクションで SummaryPoint・会員ポイントを更新する。呼出元のSerializableトランザクション内で使用する。
/// </para>
/// </summary>
public sealed class PointLedgerDb(ExDatabase db) {
	/// <summary>手動登録行の EventKey 接頭辞。取消は「MANUAL:C:{元Id}」で1件に限る</summary>
	public const string ManualKeyPrefix = "MANUAL:";
	private const string CancelKeyPrefix = ManualKeyPrefix + "C:";

	/// <summary>手動登録の入力検査と EventKey・Jcalc の補完。追加前に呼ぶ</summary>
	public void ValidateManual(TranPointEvent row) {
		Require(row.Id_Customer > 0 && db.FetchDialect<long>("SELECT COUNT(*) FROM MasterEndCustomer WHERE Id=@0", row.Id_Customer).FirstOrDefault() > 0, "顧客を指定してください。");
		Require(row.DenDay?.Length == 8 && DateTime.TryParseExact(row.DenDay, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _), "計上日は実在する日付(yyyyMMdd)で指定してください。");
		Require(row.Id_Tenuri == 0 && row.Id_Tenpo == 0 && row.Id_PointBase == 0 && row.Id_PointRank == 0 && row.Id_PointBonus == 0, "手動登録では伝票・店舗・ポイント条件を指定できません。");
		Require(!string.IsNullOrWhiteSpace(row.Memo) && row.Memo.Length <= 200, "理由(備考)を1～200文字で入力してください。");
		Require(row.Id_Shain == 0 || db.FetchDialect<long>("SELECT COUNT(*) FROM MasterShain WHERE Id=@0", row.Id_Shain).FirstOrDefault() > 0, "担当者が存在しません。");
		switch ((EnumPointEventType)row.EventType) {
			case EnumPointEventType.Grant:
				Require(row.PointDelta > 0, "手動付与のポイントは正数で指定してください。");
				break;
			case EnumPointEventType.Use:
			case EnumPointEventType.Expire:
				Require(row.PointDelta < 0, "手動使用・手動失効のポイントは負数(減算)で指定してください。");
				break;
			case EnumPointEventType.Adjustment:
				Require(row.PointDelta != 0, "調整ポイントは0以外で指定してください。");
				break;
			case EnumPointEventType.Cancel:
				var org = db.FetchDialect<TranPointEvent>("SELECT * FROM TranPointEvent WHERE Id=@0", row.Id_OriginalEvent).FirstOrDefault();
				Require(org != null && org.EventKey.StartsWith(ManualKeyPrefix, StringComparison.Ordinal) && org.EventType != (int)EnumPointEventType.Cancel,
					"取消できるのは手動登録した付与・使用・調整・失効だけです。");
				Require(org!.Id_Customer == row.Id_Customer && row.PointDelta == -org.PointDelta, "取消は元の行と同じ顧客・逆符号のポイントで登録してください。");
				row.EventKey = CancelKeyPrefix + org.Id;
				Require(!KeyExists(row.EventKey), "この行は既に取消されています。");
				row.Jcalc = Common.SerializeObject(new { Rule = "ManualCancel", OriginalKey = org.EventKey });
				return;
			default:
				throw new ArgumentException("手動登録できる種別は 付与・使用・取消・調整・失効 です。");
		}
		Require(row.Id_OriginalEvent == 0, "取消以外は元イベントを指定できません。");
		// 再送で二重計上しないよう、画面が採番した EventKey をそのまま一意キーにする。未指定ならサーバで採番する
		if (string.IsNullOrEmpty(row.EventKey)) {
			row.EventKey = ManualKeyPrefix + Guid.NewGuid().ToString("N");
		}
		Require(row.EventKey.StartsWith(ManualKeyPrefix, StringComparison.Ordinal) && !row.EventKey.StartsWith(CancelKeyPrefix, StringComparison.Ordinal) && row.EventKey.Length <= 160,
			"手動登録の EventKey が不正です。");
		Require(!KeyExists(row.EventKey), "同じ登録が既に保存されています（再送）。一覧を再検索して確認してください。");
		row.Jcalc = Common.SerializeObject(new { Rule = "Manual" });
	}

	/// <summary>
	/// 追加後に残高を更新する。減算(使用・失効・負の調整)で残高が負になる場合は拒否する（呼出元のトランザクションごと戻る）。取消は訂正のため許可する
	/// </summary>
	/// <returns>更新した顧客数</returns>
	public int AfterInsert(TranPointEvent row) {
		var calc = new PointCalcDb(db);
		calc.ApplyBalance(new Dictionary<long, long> { [row.Id_Customer] = row.PointDelta });
		if (row.PointDelta < 0 && row.EventType != (int)EnumPointEventType.Cancel) {
			var balance = calc.Balance(row.Id_Customer);
			Require(balance >= 0, $"ポイント残高が不足しているため登録できません（登録後残高 {balance}）。");
		}
		return 1;
	}

	private bool KeyExists(string key) => db.FetchDialect<long>("SELECT COUNT(*) FROM TranPointEvent WHERE EventKey=@0", key).FirstOrDefault() > 0;

	private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
