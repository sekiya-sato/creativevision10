using CvBase;

namespace CvDomainLogic;

/// <summary>
/// 自動実行ジョブ（スケジューラ）の「実行する/しない」フラグと cron式を、
/// 既存テーブル <see cref="MasterConfig"/>（Category/Name/Val）に永続化するクラス。
/// <para>
/// DBスキーマは変更しない。キーは Category="Scheduler"、Name="Job.{jobKey}.Enabled" / "Job.{jobKey}.Cron" とし、
/// Val に文字列で値を保持する。
/// </para>
/// <para>
/// <b>MasterConfig にレコードが無い場合は「未設定」を表し、初期状態とみなす。</b>
/// この場合 <see cref="GetEnabled"/> / <see cref="GetCron"/> は <c>null</c> を返すので、
/// 呼び出し側はジョブ定義側の既定値（スケジューラサービス側の初期設定など）を使うこと。
/// </para>
/// </summary>
public class SchedulerJobConfigDb {
	readonly ExDatabase _db;

	public SchedulerJobConfigDb(ExDatabase db) {
		_db = db;
	}

	/// <summary><see cref="MasterConfig"/>.Category に使うカテゴリ名。</summary>
	public const string ConfigCategory = MasterConfig.CategoryScheduler;

	/// <summary>
	/// 指定ジョブの実行フラグを取得する。
	/// レコードが無ければ未設定として <c>null</c> を返す。
	/// </summary>
	/// <param name="jobKey">ジョブを識別するキー</param>
	/// <returns>true=実行する、false=実行しない、null=未設定</returns>
	public bool? GetEnabled(string jobKey) {
		var val = _db.FirstOrDefault<string>(
			$"SELECT Val FROM {nameof(MasterConfig)} WHERE Name = @0", EnabledKey(jobKey));
		if (string.IsNullOrWhiteSpace(val))
			return null;
		return val.Trim().ToLowerInvariant() switch {
			"1" or "true" or "on" => true,
			"0" or "false" or "off" => false,
			_ => null,
		};
	}

	/// <summary>
	/// 指定ジョブの cron式を取得する。
	/// レコードが無い、または空白のみの場合は <c>null</c> を返す。
	/// </summary>
	/// <param name="jobKey">ジョブを識別するキー</param>
	public string? GetCron(string jobKey) {
		var val = _db.FirstOrDefault<string>(
			$"SELECT Val FROM {nameof(MasterConfig)} WHERE Name = @0", CronKey(jobKey));
		return string.IsNullOrWhiteSpace(val) ? null : val;
	}

	/// <summary>
	/// 指定ジョブの実行フラグを設定する（upsert）。
	/// </summary>
	/// <param name="jobKey">ジョブを識別するキー</param>
	/// <param name="enabled">true=実行する、false=実行しない</param>
	public void SetEnabled(string jobKey, bool enabled) {
		Upsert(EnabledKey(jobKey), enabled ? "1" : "0", $"自動実行ジョブ {jobKey} の実行フラグ");
	}

	/// <summary>
	/// 指定ジョブの cron式を設定する（upsert）。
	/// </summary>
	/// <param name="jobKey">ジョブを識別するキー</param>
	/// <param name="cronExpression">設定する cron式</param>
	public void SetCron(string jobKey, string cronExpression) {
		Upsert(CronKey(jobKey), cronExpression, $"自動実行ジョブ {jobKey} の cron式");
	}

	/// <summary>Name列に使う実行フラグ用キーを組み立てる。</summary>
	static string EnabledKey(string jobKey) => $"{MasterConfig.NameSchedulerJobPrefix}{jobKey}{MasterConfig.NameSchedulerEnabledSuffix}";

	/// <summary>Name列に使う cron式用キーを組み立てる。</summary>
	static string CronKey(string jobKey) => $"{MasterConfig.NameSchedulerJobPrefix}{jobKey}{MasterConfig.NameSchedulerCronSuffix}";

	/// <summary>
	/// <see cref="MasterConfig"/> を Name で検索し、あれば Val/Vdu を更新、無ければ新規登録する。
	/// </summary>
	void Upsert(string name, string val, string memo) {
		var vdate = DateTime.Now.ToUniversalTime().Ticks;
		var existing = _db.FirstOrDefault<MasterConfig>(
			$"SELECT * FROM {nameof(MasterConfig)} WHERE Name = @0", name);
		if (existing != null) {
			existing.Val = val;
			existing.Vdu = vdate;
			_db.Update(existing, ["Val", "Vdu"]);
			return;
		}

		var newRow = new MasterConfig {
			Category = ConfigCategory,
			Name = name,
			Val = val,
			Memo = memo,
			Vdc = vdate,
			Vdu = vdate,
		};
		_db.Insert(newRow);
	}
}
