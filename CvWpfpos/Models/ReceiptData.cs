using CvBase;
using CvWpfpos.Services;

namespace CvWpfpos.Models;

/// <summary>レシート／領収書に印字する店舗情報。appsettings.json から供給する。</summary>
public sealed record ReceiptStore(string Name, string Address, string Phone);

/// <summary>レシート明細 1 行。金額はすべて税抜。</summary>
public sealed record ReceiptLine(string ProductCode, string ProductName, string ColorSize, string Barcode, int Quantity, int UnitPrice, int Amount);

/// <summary>税率ごとの対象額（税抜）と消費税額。Label は「10%対象」「非課税」など。</summary>
public sealed record ReceiptTaxLine(string Label, long Taxable, long Tax);

/// <summary>
/// レシート／領収書の印字データ。
/// 上代を税抜として扱う外税方式。SubTotal（税抜小計）＋ TaxAmount（消費税）＝ TotalAmount（お買上合計）。
/// </summary>
public sealed record ReceiptData(
    long SaleId,
    DateTime SoldAt,
    ReceiptStore Store,
    string StaffCode,
    IReadOnlyList<ReceiptLine> Lines,
    int TotalQuantity,
    int SubTotal,
    IReadOnlyList<ReceiptTaxLine> TaxLines,
    int TaxAmount,
    int TotalAmount,
    int CashAmount,
    int CardAmount,
    int OtherAmount,
    int ChangeAmount,
    string AppVersion,
    bool IsReturn = false)
{
    /// <summary>
    /// サーバに保存された売上伝票からレシートを作る。金額・税はサーバが確定した値をそのまま使う。
    /// </summary>
    public static ReceiptData FromSale(Tran01Tenuri sale, PosSettings settings, string appVersion)
    {
        var meisai = sale.Jmeisai ?? [];
        var lines = meisai.Select(m => new ReceiptLine(m.Code_Shohin, m.Mei_Shohin, FormatColorSize(m), m.JanCode, m.Su, m.Tanka, (int)m.Kingaku)).ToList();
        var payment = sale.JposPayment ?? new PosPaymentDetail();
        var staffCode = string.IsNullOrWhiteSpace(sale.VShain?.Cd) ? settings.ResolvedStaffCode : sale.VShain.Cd;
        var soldAt = sale.Vdc > 0 ? new DateTime(sale.Vdc, DateTimeKind.Utc).ToLocalTime() : DateTime.Now;
        return new ReceiptData(
            sale.Id,
            soldAt,
            new ReceiptStore(settings.StoreName, settings.StoreAddress, settings.StorePhone),
            staffCode,
            lines,
            (int)sale.SuTotal,
            (int)sale.KingakuTotal,
            BuildTaxLines(sale, meisai),
            (int)(sale.Tax1 + sale.Tax2 + sale.Tax3),
            (int)sale.Total,
            payment.CashAmount,
            payment.CardAmount,
            payment.OtherAmount,
            payment.ChangeAmount,
            appVersion,
            sale.Kubun is >= 20 and <= 29);
    }

    /// <summary>
    /// 税区分1〜3の対象額・税額（伝票ヘッダ）を、明細に保存された適用税率で見出しにする。税区分なしの明細は非課税として出す。
    /// </summary>
    private static List<ReceiptTaxLine> BuildTaxLines(Tran01Tenuri sale, IReadOnlyCollection<Tran99Meisai> meisai)
    {
        var result = new List<ReceiptTaxLine>();
        (long TaxId, long Taxable, long Tax)[] groups = [(1, sale.TaxableAmount1, sale.Tax1), (2, sale.TaxableAmount2, sale.Tax2), (3, sale.TaxableAmount3, sale.Tax3)];
        foreach (var (taxId, taxable, tax) in groups)
        {
            if (taxable == 0) continue;
            var rate = meisai.FirstOrDefault(m => m.Id_Tax == taxId)?.TaxRate ?? 0;
            result.Add(new ReceiptTaxLine($"{rate}%対象", taxable, tax));
        }
        var nonTaxable = meisai.Where(m => m.Id_Tax is < 1 or > 3).Sum(m => Math.Abs(m.Kingaku));
        if (nonTaxable != 0) result.Add(new ReceiptTaxLine("非課税", nonTaxable, 0));
        return result;
    }

    /// <summary>「10-シロ 00-サンプル」形式のカラー・サイズ表記。</summary>
    private static string FormatColorSize(Tran99Meisai m) =>
        string.Join(' ', new[] { JoinCodeName(m.Code_Col, m.Mei_Col), JoinCodeName(m.Code_Siz, m.Mei_Siz) }.Where(text => text.Length > 0));

    private static string JoinCodeName(string code, string name) =>
        (code.Length, name.Length) switch { (0, 0) => string.Empty, (0, _) => name, (_, 0) => code, _ => $"{code}-{name}" };
}
