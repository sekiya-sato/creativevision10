using CvAsset;
using CvBase;
using Microsoft.Extensions.Logging;

namespace CvDomainLogic;

/// <summary>
/// 期首残高（売掛・請求・買掛・支払）を Summary 各テーブルへ洗い替え登録する。
/// <para>
/// 対象日付の既存行を、指定された取引先ぶんだけ削除してから登録し直す。削除と登録は
/// 1トランザクション（Serializable）で行い、途中で失敗した場合は1件も残さない。
/// </para>
/// <para>
/// 期首年月日(<see cref="MasterSysman.FiscalStartDate"/>)より前の集計行は、売掛・買掛・請求・支払の
/// 再計算では凍結されて上書きされない（<c>SummaryDb</c> の各 Calc に期首ガードがある）。
/// このため期首残は必ず期首より前のキー日付で登録する必要があり、ここでも二重に検査する。
/// 仕様は `Doc/spec/2026-08-21_残高登録処理_詳細設計.md` を参照する。
/// </para>
/// </summary>
public class OpeningBalanceDb {
	private readonly ExDatabase _db;
	private readonly ILogger<OpeningBalanceDb> _logger;

	public OpeningBalanceDb(ExDatabase db) {
		_db = db;
		_logger = new NLogExtender<OpeningBalanceDb>();
	}

	/// <summary>
	/// 期首残高を洗い替え登録する。
	/// </summary>
	/// <exception cref="ArgumentException">テーブル名・キー日付・行内容が期首残高の条件を満たさない場合</exception>
	public OpeningBalanceImportResult Import(OpeningBalanceImportParam param) {
		ArgumentNullException.ThrowIfNull(param);

		// テーブル名はSQLへ連結するため、必ず許可リストと突合してから使う
		var spec = OpeningBalanceCsv.FindSpecByTableName(param.TableName)
			?? throw new ArgumentException($"期首残高の対象テーブルではありません: {param.TableName}", nameof(param));

		var keyDate = (param.KeyDate ?? string.Empty).Trim();
		if (keyDate.Length != spec.KeyLength || !keyDate.All(char.IsAsciiDigit)) {
			throw new ArgumentException(
				$"{spec.KeyLabel}は{spec.KeyLength}桁の数字で指定してください: '{keyDate}'", nameof(param));
		}

		var fiscalStartDate = GetFiscalStartDate();
		if (fiscalStartDate == OpeningBalanceCsv.UnsetFiscalStartDate) {
			throw new ArgumentException("期首日が未設定です。システム管理マスタで期首年月日を設定してください。", nameof(param));
		}
		if (!OpeningBalanceCsv.IsBeforeFiscalStart(keyDate, fiscalStartDate, spec)) {
			throw new ArgumentException(
				$"{spec.KeyLabel} {OpeningBalanceCsv.FormatDate(keyDate)} は期首({OpeningBalanceCsv.FormatDate(fiscalStartDate)})以降です。" +
				"期首残高は期首より前のキー日付でしか登録できません。", nameof(param));
		}

		var ownerIds = (param.OwnerIds ?? []).Where(x => x > 0).Distinct().ToArray();
		if (ownerIds.Length == 0) {
			throw new ArgumentException($"洗い替え対象の{spec.OwnerLabel}が指定されていません。", nameof(param));
		}

		return spec.Kind switch {
			EnumOpeningBalanceKind.UriKake => Replace<SummaryUriKake>(spec, keyDate, ownerIds, param.ItemsJson),
			EnumOpeningBalanceKind.UriSei => Replace<SummaryUriSei>(spec, keyDate, ownerIds, param.ItemsJson),
			EnumOpeningBalanceKind.KaiKake => Replace<SummaryKaiKake>(spec, keyDate, ownerIds, param.ItemsJson),
			_ => Replace<SummaryKaiShi>(spec, keyDate, ownerIds, param.ItemsJson),
		};
	}

	private OpeningBalanceImportResult Replace<T>(
		OpeningBalanceKindSpec spec, string keyDate, long[] ownerIds, string? itemsJson) where T : BaseDbClass {
		var items = Common.DeserializeObject<List<T>>(string.IsNullOrWhiteSpace(itemsJson) ? "[]" : itemsJson) ?? [];

		// 画面が組んだ行と洗い替え範囲が食い違っていないことを確認する。
		// ここが崩れると期首残が別の年月・別の取引先へ入り、繰越の起点がずれる。
		var allowed = ownerIds.ToHashSet();
		foreach (var item in items) {
			var rowKey = GetKeyValue(item);
			if (rowKey != keyDate) {
				throw new ArgumentException(
					$"{spec.KeyLabel}が一致しない行があります: 行='{rowKey}' 指定='{keyDate}'", nameof(itemsJson));
			}
			var ownerId = GetOwnerId(item);
			if (!allowed.Contains(ownerId)) {
				throw new ArgumentException(
					$"洗い替え対象外の{spec.OwnerLabel}(Id={ownerId})が含まれています。", nameof(itemsJson));
			}
			item.Id = 0;
		}

		var deleteSql = $"DELETE FROM {spec.TableName} WHERE {spec.KeyColumn} = @0 AND {spec.OwnerColumn} IN (@1)";
		var transactionStarted = false;
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			transactionStarted = true;
			var deleted = _db.Execute(deleteSql, keyDate, ownerIds);
			foreach (var item in items) {
				// 監査値はサーバー側で採番する(クライアントの値は信用しない)
				var vdate = Common.GetVdate();
				item.Vdc = vdate;
				item.Vdu = vdate;
				_db.Insert(item);
			}
			_db.CompleteTransaction();
			transactionStarted = false;
			_logger.LogInformation(
				"期首残高登録 {Table} {KeyLabel}={KeyDate} 対象{OwnerLabel}={OwnerCount}件 削除={Deleted}件 登録={Inserted}件",
				spec.TableName, spec.KeyLabel, keyDate, spec.OwnerLabel, ownerIds.Length, deleted, items.Count);
			return new OpeningBalanceImportResult(deleted, items.Count);
		}
		catch {
			if (transactionStarted) {
				_db.AbortTransaction();
			}
			throw;
		}
	}

	/// <summary>
	/// 期首年月日(yyyyMMdd)を <see cref="MasterSysman"/> から取得する。未設定時は "19010101"。
	/// <para>MasterSysman 未作成(移行前・一部の単体テスト)でも例外にしない。</para>
	/// </summary>
	private string GetFiscalStartDate() {
		var tableExists = _db.FirstOrDefault<string>(
			"SELECT name FROM sqlite_master WHERE type='table' AND name='MasterSysman'");
		if (string.IsNullOrEmpty(tableExists)) {
			return OpeningBalanceCsv.UnsetFiscalStartDate;
		}
		var value = _db.FirstOrDefault<string>("SELECT FiscalStartDate FROM MasterSysman ORDER BY Id LIMIT 1");
		return string.IsNullOrWhiteSpace(value) ? OpeningBalanceCsv.UnsetFiscalStartDate : value;
	}

	private static string GetKeyValue(BaseDbClass row) => row switch {
		SummaryUriKake x => x.DenMonth,
		SummaryUriSei x => x.DenDay,
		SummaryKaiKake x => x.DenMonth,
		SummaryKaiShi x => x.DenDay,
		_ => string.Empty,
	};

	private static long GetOwnerId(BaseDbClass row) => row switch {
		SummaryUriKake x => x.Id_Tokui,
		SummaryUriSei x => x.Id_Tokui,
		SummaryKaiKake x => x.Id_Shiire,
		SummaryKaiShi x => x.Id_Shiire,
		_ => 0,
	};
}
