namespace CvWpfpos.Models;

/// <summary>レシート／領収書に印字する店舗情報。appsettings.json から供給する。</summary>
public sealed record ReceiptStore(string Name, string Address, string Phone);

/// <summary>レシート明細 1 行。金額はすべて税抜。</summary>
public sealed record ReceiptLine(string ProductCode, string ProductName, string ColorSize, string Barcode, int Quantity, int UnitPrice, int Amount);

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
    int TaxRatePercent,
    int TaxAmount,
    int TotalAmount,
    int CashAmount,
    int CardAmount,
    int OtherAmount,
    int ChangeAmount,
    string AppVersion,
    bool IsReturn = false);
