using CvBase;

namespace CvDomainLogic;

/// <summary>
/// 自動実行ジョブ（スケジューラ）の「実行する/しない」フラグ、cron式、メール送信フラグを、
/// 既存テーブル <see cref="MasterConfig"/>（Category/Name/Val）に永続化するクラス。
/// <para>
/// DBスキーマは変更しない。キーは Category="自動実行管理"、
/// Name="GenericSQLRegAutoExec"+TaskId先頭8桁（実行フラグ） / "GenericSQLRegAutoExecCron"+TaskId先頭8桁（cron式） /
/// "GenericSQLRegAutoExecIsSendMail"+TaskId先頭8桁（メール送信フラグ）とし、
/// Val に "1"（実行する）/ "0"（実行しない）などの文字列で値を保持する。
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
	public const string ConfigCategory = MasterConfig.CategoryAutoExec;

	/// <summary>
	/// 指定タスクの実行フラグを取得する。
	/// レコードが無ければ未設定として <c>null</c> を返す。
	/// </summary>
	/// <param name="taskId">タスクを識別するId</param>
	/// <returns>true=実行する、false=実行しない、null=未設定</returns>
	public bool? GetEnabled(Guid taskId) {
		var val = _db.FirstOrDefault<string>(
			$"SELECT Val FROM {nameof(MasterConfig)} WHERE Name = @0", EnabledKey(taskId));
		if (string.IsNullOrWhiteSpace(val))
			return null;
		return val.Trim().ToLowerInvariant() switch {
			"1" or "true" or "on" => true,
			"0" or "false" or "off" => false,
			_ => null,
		};
	}

	/// <summary>
	/// 指定タスクの cron式を取得する。
	/// レコードが無い、または空白のみの場合は <c>null</c> を返す。
	/// </summary>
	/// <param name="taskId">タスクを識別するId</param>
	public string? GetCron(Guid taskId) {
		var val = _db.FirstOrDefault<string>(
			$"SELECT Val FROM {nameof(MasterConfig)} WHERE Name = @0", CronKey(taskId));
		return string.IsNullOrWhiteSpace(val) ? null : val;
	}

	/// <summary>
	/// 指定タスクのメール送信フラグを取得する。
	/// レコードが無い、空白のみ、または値が不正なら <c>null</c> を返す。
	/// </summary>
	/// <param name="taskId">タスクを識別するId</param>
	/// <returns>true=送信する、false=送信しない、null=未設定または不正</returns>
	public bool? GetIsSendMail(Guid taskId) {
		var val = _db.FirstOrDefault<string>(
			$"SELECT Val FROM {nameof(MasterConfig)} WHERE Name = @0", IsSendMailKey(taskId));
		if (string.IsNullOrWhiteSpace(val))
			return null;
		return val.Trim().ToLowerInvariant() switch {
			"1" or "true" or "on" => true,
			"0" or "false" or "off" => false,
			_ => null,
		};
	}

	/// <summary>
	/// 指定タスクの実行フラグを設定する（upsert）。
	/// </summary>
	/// <param name="taskId">タスクを識別するId</param>
	/// <param name="enabled">true=実行する、false=実行しない</param>
	public void SetEnabled(Guid taskId, bool enabled) {
		Upsert(EnabledKey(taskId), enabled ? MasterConfig.ValAutoExecEnabled : MasterConfig.ValAutoExecDisabled, $"自動実行ジョブ {taskId} の実行フラグ");
	}

	/// <summary>
	/// 指定タスクの cron式を設定する（upsert）。
	/// </summary>
	/// <param name="taskId">タスクを識別するId</param>
	/// <param name="cronExpression">設定する cron式</param>
	public void SetCron(Guid taskId, string cronExpression) {
		Upsert(CronKey(taskId), cronExpression, $"自動実行ジョブ {taskId} の cron式");
	}

	/// <summary>
	/// 指定タスクのメール送信フラグを設定する（upsert）。
	/// </summary>
	/// <param name="taskId">タスクを識別するId</param>
	/// <param name="isSendMail">true=送信する、false=送信しない</param>
	public void SetIsSendMail(Guid taskId, bool isSendMail) {
		Upsert(IsSendMailKey(taskId), isSendMail ? MasterConfig.ValAutoExecEnabled : MasterConfig.ValAutoExecDisabled, $"自動実行ジョブ {taskId} の実行結果メール送信フラグ");
	}

	/// <summary>
	/// 指定タスクのメール送信フラグ行を削除する。行が無ければ何もしない。
	/// 動的追加タスクは削除するとTaskIdが再利用されないため、残った設定行を片付けるために使う。
	/// </summary>
	/// <param name="taskId">タスクを識別するId</param>
	public void RemoveIsSendMail(Guid taskId) {
		var existing = _db.FirstOrDefault<MasterConfig>(
			$"SELECT * FROM {nameof(MasterConfig)} WHERE Name = @0", IsSendMailKey(taskId));
		if (existing != null)
			_db.Delete(existing);
	}

	/// <summary>Name列に使う実行フラグ用キーを組み立てる。</summary>
	static string EnabledKey(Guid taskId) => $"{MasterConfig.NameAutoExecEnabledPrefix}{TaskIdPrefix(taskId)}";

	/// <summary>Name列に使う cron式用キーを組み立てる。</summary>
	static string CronKey(Guid taskId) => $"{MasterConfig.NameAutoExecCronPrefix}{TaskIdPrefix(taskId)}";

	/// <summary>Name列に使うメール送信フラグ用キーを組み立てる。</summary>
	static string IsSendMailKey(Guid taskId) => $"{MasterConfig.NameAutoExecIsSendMailPrefix}{TaskIdPrefix(taskId)}";

	/// <summary>TaskId(Guid)の先頭8桁を取り出す。</summary>
	static string TaskIdPrefix(Guid taskId) => taskId.ToString()[..8];

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
