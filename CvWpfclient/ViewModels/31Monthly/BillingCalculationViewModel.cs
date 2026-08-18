using CodeShare;
using CvBase;

namespace CvWpfclient.ViewModels._31Monthly;

/// <summary>得意先締日単位で請求残を作成する。</summary>
public partial class BillingCalculationViewModel : BaseBillingCalculationViewModel {
	private sealed class PaysakiClosingMismatch {
		public string ChildCode { get; set; } = string.Empty;
		public string ParentCode { get; set; } = string.Empty;
	}

	protected override CvFlag TargetFlag => CvFlag.Msg056_SummaryUriSei;
	protected override string ActionName => "請求計算";
	protected override string TorihikiName => "得意先";
	protected override string MasterTableName => nameof(MasterTokui);

	protected override async Task<string> GetPreExecuteWarningAsync(string codeFrom, string codeTo, CancellationToken cancellationToken) {
		List<string> parameters = [SelectedShime.ToString(System.Globalization.CultureInfo.InvariantCulture)];
		var where = "WHERE c.Id_Paysaki <> 0 AND c.Shime1 = @0 AND p.Shime1 <> c.Shime1";
		if (codeFrom.Length > 0) {
			where += $" AND c.Code >= @{parameters.Count}";
			parameters.Add(codeFrom);
		}
		if (codeTo.Length > 0) {
			where += $" AND c.Code <= @{parameters.Count}";
			parameters.Add(codeTo);
		}
		var rows = await QuerySqlListAsync<PaysakiClosingMismatch>($@"
SELECT c.Code AS ChildCode, p.Code AS ParentCode
FROM MasterTokui AS c
INNER JOIN MasterTokui AS p ON p.Id = c.Id_Paysaki
{where}
ORDER BY c.Code", parameters, cancellationToken);
		if (rows.Count == 0) return string.Empty;
		var samples = string.Join("、", rows.Take(5).Select(x => $"{x.ChildCode}→{x.ParentCode}"));
		var suffix = rows.Count > 5 ? $" ほか{rows.Count - 5}件" : string.Empty;
		return $"請求先（親）と得意先の締日が異なるデータがあります: {samples}{suffix}\nマスタ変更および請求再計算が必要です。";
	}
}
