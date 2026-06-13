using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._01Master;

/// <summary>
/// システム管理マスターメンテ ViewModel（単一レコード、XAMLはCurrent.*に直接バインド）
/// </summary>
public partial class MasterSysKanriMenteViewModel : Helpers.BaseMenteViewModel<MasterSysman> {

	[ObservableProperty]
	string title = "システム管理マスターメンテ画面";

	[ObservableProperty]
	string? desc0;

	// MasterSysman は単一レコードのため、ListOrderは不要だが、初期値がCodeのため上書きする必要がある
	protected override string? ListOrder => "Id";
	protected override string? FormFile => "MasterSysKanriMente.qfm";
	protected override PrintByCsvParam? PrintByCsvParam => Current.Id > 0 ? new(BuildPrintCsvData()) : null;

	public IReadOnlyList<EnumShime> ShimeBiItems { get; } = Enum.GetValues<EnumShime>();

	string BuildPrintCsvData() {
		var tax1 = GetTaxEntry(0);
		var tax2 = GetTaxEntry(1);
		var tax3 = GetTaxEntry(2);

		string[] fields = [
			Current.Id.ToString(CultureInfo.InvariantCulture),
			NormalizePrintText(Current.Name),
			NormalizePrintText(Current.PostalCode),
			NormalizePrintText(Current.Address1),
			NormalizePrintText(Current.Address2),
			NormalizePrintText(Current.Address3),
			NormalizePrintText(Current.Tel),
			NormalizePrintText(Current.Mail),
			NormalizePrintText(Current.BankAccount1),
			NormalizePrintText(Current.BankAccount2),
			NormalizePrintText(Current.BankAccount3),
			FormatYmdText(Current.FiscalStartDate),
			FormatShimeBiText(Current.ShimeBi),
			Current.ModifyDaysEx.ToString(CultureInfo.InvariantCulture),
			Current.ModifyDaysPre.ToString(CultureInfo.InvariantCulture),
			FormatTaxValue(tax1?.TaxRate),
			FormatYmdText(tax1?.DateFrom),
			FormatTaxValue(tax1?.TaxNewRate),
			FormatTaxValue(tax2?.TaxRate),
			FormatYmdText(tax2?.DateFrom),
			FormatTaxValue(tax2?.TaxNewRate),
			FormatTaxValue(tax3?.TaxRate),
			FormatYmdText(tax3?.DateFrom),
			FormatTaxValue(tax3?.TaxNewRate),
			NormalizePrintText(Current.Hp),
			NormalizePrintText(Current.TaxRegistrationNumber),
			FormatDateTimeText(Current.VdateC),
			FormatDateTimeText(Current.VdateU),
		];

		return string.Join(",", fields.Select(EscapeCsvField)) + "\r\n";
	}

	MasterSysTax? GetTaxEntry(int index) {
		var taxes = Current.Jsub;
		return taxes is not null && index >= 0 && index < taxes.Count ? taxes[index] : null;
	}

	static string NormalizePrintText(string? value) => value ?? string.Empty;

	static string FormatTaxValue(int? value) =>
		value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;

	static string FormatYmdText(string? value) {
		if (string.IsNullOrWhiteSpace(value)) {
			return string.Empty;
		}

		return DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
			? date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
			: value;
	}

	static string FormatDateTimeText(DateTime value) =>
		value == default
			? string.Empty
			: value.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);

	static string FormatShimeBiText(int shimeBi) => shimeBi switch {
		(int)EnumShime.DayLast => "末日",
		>= 1 and <= 31 => $"{shimeBi:00}日",
		_ => string.Empty,
	};

	static string EscapeCsvField(string? value) {
		var text = value ?? string.Empty;
		if (text.Contains('"')) {
			text = text.Replace("\"", "\"\"");
		}

		return text.IndexOfAny([',', '"', '\r', '\n']) >= 0
			? $"\"{text}\""
			: text;
	}

	[RelayCommand]
	async Task Init() => await DoList(CancellationToken.None);

	protected override void AfterList(System.Collections.IList list) {
		if (list.Count > 0) {
			var timespan = DateTime.Now - StartTime;
			Desc0 = $"開始{StartTime} 取得、画面展開{timespan.ToStrSpan()}";
		}
	}

	// XAMLがCurrent.*に直接バインドしているため、CurrentEditではなくCurrentを送信
	protected override object CreateUpdateParam() =>
		new UpdateParam(Tabletype, Common.SerializeObject(Current));

	protected override bool CanDelete() => false;

	[RelayCommand]
	async Task SearchPostalCode() => await PostalAddressSearchHelper.SearchAndApplyAsync(this, Current.PostalCode ?? string.Empty, item => {
		var currentAddress1 = Current.Address1;
		var currentAddress2 = Current.Address2;
		var currentAddress3 = Current.Address3;
		Current.PostalCode = item.PostalCode;
		Current.Address1 = item.Address1;
		Current.Address2 = item.Address2;
		Current.Address3 = PostalAddressSearchHelper.MergeAddress3(currentAddress1, currentAddress2, currentAddress3, item);
	});
}
