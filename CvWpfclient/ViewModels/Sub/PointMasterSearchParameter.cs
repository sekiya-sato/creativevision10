namespace CvWpfclient.ViewModels.Sub;

/// <summary>ポイントマスタ（ベース・ランク・ボーナス）一覧の取得条件</summary>
public sealed record class PointMasterSearchParameter {
	/// <summary>親ベース条件で「すべて」を表す値</summary>
	public const long AllBase = -1;
	/// <summary>有効状態で「すべて」を表す値</summary>
	public const int AllEnabled = -1;

	public string? DisplayName { get; set; }
	/// <summary>コード行のラベル（制度コード / ボーナスコード）</summary>
	public string CodeLabel { get; set; } = "コード";
	/// <summary>コード・適用日・有効状態の行を表示するか（ランクは版条件を親で持つため非表示）</summary>
	public bool IsVersionedVisible { get; set; } = true;
	/// <summary>親ベース行を表示するか</summary>
	public bool IsBaseVisible { get; set; }

	/// <summary>コード（前方一致）</summary>
	public string? Code { get; set; }
	/// <summary>この日を適用期間に含む版だけを対象にする yyyyMMdd</summary>
	public string? TargetDay { get; set; }
	/// <summary>有効状態 -1=すべて 0=無効 1=有効</summary>
	public int EnabledState { get; set; } = AllEnabled;
	/// <summary>親ベースId -1=すべて 0=移行設定待ち</summary>
	public long Id_PointBase { get; set; } = AllBase;
	public int? MaxCount { get; set; }
}
