using System.Security.Cryptography;
using System.Text;
using CvAsset;
using CvBase;
using CvBase.Share;
using Microsoft.Extensions.Logging;

namespace CvDomainLogic;

/// <summary>
/// 物流連携（WMS）の処理本体。画面・gRPC に依存しない（自動実行からも同じメソッドを呼ぶ）。
/// <para>
/// 仕様は `Doc/spec/2026-10-05_WMS連携_旧AMS連携調査と仮実装仕様.md`。
/// ファイルはサーバ上の連携フォルダ（<see cref="LogisticsSettings.BaseFolder"/>）で読み書きする。
/// 送受信の記録は <see cref="TranLogisticsBatch"/>／<see cref="TranLogisticsLine"/>、
/// 実行履歴と同時実行防止は <see cref="ManualLockDb"/>（終了時に <see cref="SysHistAutoexec"/> へ記録）を使う。
/// </para>
/// </summary>
public partial class LogisticsLinkDb(ExDatabase db) {
	private readonly ExDatabase _db = db;
	private readonly ILogger<LogisticsLinkDb> _logger = new NLogExtender<LogisticsLinkDb>();

	/// <summary>マニュアル排他の一連処理名（<see cref="SysSequence.TableName"/>・実行履歴のタスク名）</summary>
	public const string LockProcessName = "物流連携";
	/// <summary>マニュアル排他の予想処理秒数</summary>
	private const long LockExpectedSeconds = 300;

	/// <summary>設定を読む</summary>
	public LogisticsSettings LoadSettings() =>
		LogisticsSettings.From(_db.Fetch<MasterConfig>("where Category = @0", MasterConfig.CategoryLogistics));

	/// <summary>設定と対象倉庫（倉庫マスタに存在するもの）を返す</summary>
	public LogisticsSettingsInfo QuerySettings() {
		var settings = LoadSettings();
		var soko = LoadTargetSoko(settings).Select(t => new LogisticsSokoInfo(t.Id, t.Code, t.Name)).ToArray();
		var reason = settings.UnusableReason;
		if (reason.Length == 0 && soko.Length == 0) {
			reason = "対象倉庫のコードが倉庫マスタ（得意先 TenType=0）にありません。";
		}
		return new LogisticsSettingsInfo(settings, reason, soko);
	}

	/// <summary>対象倉庫（得意先 TenType=0 で設定コードに一致するもの）</summary>
	internal List<MasterTokui> LoadTargetSoko(LogisticsSettings settings) {
		if (settings.TargetSokoCodes.Length == 0) {
			return [];
		}
		return _db.Fetch<MasterTokui>(
			$"where TenType = 0 and Code in ({string.Join(",", settings.TargetSokoCodes.Select((_, i) => "@" + i))}) order by Code",
			[.. settings.TargetSokoCodes]);
	}

	/// <summary>使用できない設定なら例外（画面で直せる内容なので <see cref="ArgumentException"/>）</summary>
	internal LogisticsSettings RequireUsableSettings() {
		var info = QuerySettings();
		if (info.UnusableReason.Length > 0) {
			throw new ArgumentException(info.UnusableReason);
		}
		return info.Settings;
	}

	/// <summary>
	/// マニュアル排他を取って処理を実行し、終了時に実行履歴を残す。排他が取れなければ <see cref="InvalidOperationException"/>。
	/// </summary>
	internal T RunLocked<T>(string stepName, int execType, Func<T> body, Func<T, int> count) {
		var lockDb = new ManualLockDb(_db);
		var begin = lockDb.TryBegin(LockProcessName, stepName, LockExpectedSeconds);
		if (!begin.IsAcquired || begin.Handle is null) {
			throw new InvalidOperationException($"他の処理（{begin.Blocker?.TableName} {begin.Blocker?.ColumnName}）が実行中です。終了後に実行してください。");
		}
		using var handle = begin.Handle;
		try {
			var result = body();
			lockDb.Complete(handle, 0, count(result), stepName, execType);
			return result;
		}
		catch (Exception ex) {
			lockDb.Complete(handle, -1, 0, $"{stepName} {ex.GetType().Name}: {ex.Message}", execType);
			throw;
		}
	}

	/// <summary>文字コード</summary>
	internal static Encoding GetEncoding(LogisticsSettings settings) {
		if (string.Equals(settings.EncodingName, "utf-8", StringComparison.OrdinalIgnoreCase)) {
			return new UTF8Encoding(false);
		}
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		return Encoding.GetEncoding("shift_jis");
	}

	/// <summary>連携フォルダ配下のフォルダパス（無ければ作る）</summary>
	internal static string GetFolder(LogisticsSettings settings, string sub) {
		var path = Path.Combine(settings.BaseFolder, sub);
		Directory.CreateDirectory(path);
		return path;
	}

	/// <summary>送信ファイル名 {連携先}_{種別}_{yyyyMMddHHmmss}_{バッチId}.csv</summary>
	internal static string BuildFileName(LogisticsSettings settings, string kind, long batchId, DateTime now) =>
		$"{settings.LinkCode}_{kind}_{now:yyyyMMddHHmmss}_{batchId}.csv";

	/// <summary>
	/// 送信ファイルを配置する。work へ書いてから send_bak へ複写し、send へ移動する（同名の上書きはしない）。
	/// </summary>
	internal static void PlaceFile(LogisticsSettings settings, string fileName, string text) {
		var work = Path.Combine(GetFolder(settings, LogisticsSettings.WorkDir), fileName);
		var send = Path.Combine(GetFolder(settings, LogisticsSettings.SendDir), fileName);
		var backup = Path.Combine(GetFolder(settings, LogisticsSettings.SendBackupDir), fileName);
		if (File.Exists(send)) {
			throw new IOException($"送信フォルダに同名のファイルがあります: {fileName}");
		}
		File.WriteAllText(work, text, GetEncoding(settings));
		File.Copy(work, backup, overwrite: true);
		File.Move(work, send);
	}

	/// <summary>原文のSHA-256（16進小文字）</summary>
	internal static string ComputeHash(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

	/// <summary>送信バッチを作成中で登録する</summary>
	internal TranLogisticsBatch InsertSendBatch(LogisticsSettings settings, string kind, int rowCount, long idShain, int execType) {
		var vdate = Common.GetVdate();
		var batch = new TranLogisticsBatch {
			LinkCode = settings.LinkCode,
			Direction = (int)EnumLogisticsDirection.Send,
			DataKind = kind,
			Status = (int)EnumLogisticsSendStatus.Creating,
			ExecType = execType,
			RowCount = rowCount,
			Id_Shain = idShain,
			Vdc = vdate,
			Vdu = vdate,
		};
		_db.Insert(batch);
		return batch;
	}

	/// <summary>バッチの状態を更新する</summary>
	internal void UpdateBatchStatus(TranLogisticsBatch batch, int status, string memo) {
		batch.Status = status;
		batch.Memo = memo.Length > 1000 ? memo[..1000] : memo;
		batch.Vdu = Common.GetVdate();
		_db.Update(batch);
	}

	/// <summary>
	/// 送信ファイルを作って配置し、バッチを配置済み（失敗時は配置失敗）にする。
	/// </summary>
	/// <returns>配置できたら true</returns>
	internal bool PlaceBatchFile(LogisticsSettings settings, TranLogisticsBatch batch, string text) {
		try {
			PlaceFile(settings, batch.FileName, text);
			UpdateBatchStatus(batch, (int)EnumLogisticsSendStatus.Placed, string.Empty);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
			_logger.LogWarning(ex, "物流連携 ファイル配置失敗 Batch={BatchId} File={FileName}", batch.Id, batch.FileName);
			UpdateBatchStatus(batch, (int)EnumLogisticsSendStatus.PlaceFailed, $"ファイル配置失敗: {ex.GetType().Name}");
			return false;
		}
	}

	/// <summary>前回成功した同種別の送信バッチの作成時刻（無ければ0）</summary>
	internal long LastPlacedVdc(string kind) =>
		_db.Fetch<TranLogisticsBatch>(
			"where Direction = @0 and DataKind = @1 and Status = @2 order by Id desc",
			(int)EnumLogisticsDirection.Send, kind, (int)EnumLogisticsSendStatus.Placed).FirstOrDefault()?.Vdc ?? 0;
}
