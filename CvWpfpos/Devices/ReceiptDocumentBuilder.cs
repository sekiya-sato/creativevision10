using CvWpfpos.Models;
using System.Globalization;
using System.Text;

namespace CvWpfpos.Devices;

/// <summary>
/// <see cref="ReceiptData"/> から、お買上げレシートと領収書の ESC/POS コマンド列を組み立てる。
/// 桁数は用紙幅（58mm=30桁 / 80mm=48桁）から決まり、区切り線・右寄せは常に幅いっぱいを使う。
/// </summary>
public static class ReceiptDocumentBuilder
{
    private static readonly CultureInfo Japanese = CultureInfo.GetCultureInfo("ja-JP");

    /// <summary>お買上げレシート。</summary>
    public static byte[] BuildSalesReceipt(ReceiptData receipt, PosPaperWidth paperWidth, Encoding shiftJis, DateTime printedAt)
    {
        var builder = new EscPosBuilder(paperWidth, shiftJis).Initialize();

        AppendStoreHeader(builder, receipt);

        builder.Align(EscPosAlign.Left)
            .Line($"販売日:{receipt.SoldAt.ToString("yy年MM月dd日(ddd) HH:mm", Japanese)}")
            .Line($"販売員:{receipt.StaffCode}")
            .Feed();

        var headerText = receipt.IsReturn ? "返品レシート" : "お買上げ";
        builder.Align(EscPosAlign.Center).Emphasis(true).Scale(2, 2)
            .Line(headerText)
            .NormalSize().Emphasis(false)
            .Feed();

        builder.Align(EscPosAlign.Left);
        foreach (var line in receipt.Lines)
        {
            builder.LineWrapped($"{line.ProductCode} {line.ColorSize}".TrimEnd());
            builder.LineWrapped(line.ProductName);
            if (!string.IsNullOrWhiteSpace(line.Barcode)) builder.Line($"P {line.Barcode}");
            builder.LineQuantityAmount($"{line.Quantity:N0}点", $"¥{line.Amount:N0}");
        }

        builder.Separator()
            .LineLeftRight($"(税抜小計)  {receipt.TotalQuantity:N0}点", $"¥{receipt.SubTotal:N0}")
            .Separator()
            .LineLeftRight($"({receipt.TaxRatePercent}%対象)", $"¥{receipt.SubTotal:N0}")
            .Feed();

        builder.Emphasis(true)
            .LineLeftRight($"お買上合計 {receipt.TotalQuantity:N0}点", $"¥{receipt.TotalAmount:N0}")
            .Emphasis(false)
            .LineLeftRight("(内消費税)", $"¥{receipt.TaxAmount:N0}")
            .Feed();

        if (receipt.CashAmount > 0) builder.LineLeftRight("  現金", $"¥{receipt.CashAmount:N0}");
        if (receipt.CardAmount > 0) builder.LineLeftRight("  カード", $"¥{receipt.CardAmount:N0}");
        if (receipt.OtherAmount > 0) builder.LineLeftRight("  その他", $"¥{receipt.OtherAmount:N0}");
        builder.LineLeftRight("  お釣り", $"¥{receipt.ChangeAmount:N0}").Feed();

        builder.Align(EscPosAlign.Center)
            .Line($"伝票番号: {receipt.SaleId:N0}")
            .Code39($"R{receipt.SaleId:D6}")
            .Feed();

        AppendFooter(builder, receipt, printedAt);
        return builder.Cut().ToArray();
    }

    /// <summary>領収書。「領収書」ボタンから必要なときだけ印字する。</summary>
    public static byte[] BuildTaxInvoice(ReceiptData receipt, PosPaperWidth paperWidth, Encoding shiftJis, DateTime printedAt)
    {
        var builder = new EscPosBuilder(paperWidth, shiftJis).Initialize();

        builder.Align(EscPosAlign.Center).Emphasis(true).Scale(2, 2)
            .Line("領 収 書")
            .NormalSize().Emphasis(false)
            .Feed(2);

        builder.Align(EscPosAlign.Left)
            .LineRight("様  ")
            .Separator('_')
            .Feed();

        // 倍角中は桁数が半分になるため、金額のラベルは余白なしにして 7 桁の金額まで収まるようにする
        builder.Emphasis(true).Scale(2, 2)
            .LineLeftRight("金額", $"¥{receipt.TotalAmount:N0}-")
            .NormalSize().Emphasis(false)
            .Separator('_')
            .Feed();

        builder.Line(" 但し" + new string('_', Math.Max(4, builder.Columns - 11)) + "として")
            .Feed();

        builder.Line($" {receipt.SoldAt.ToString("yyyy年MM月dd日", Japanese)} レシートNo{receipt.SaleId:N0}")
            .Line(" 上記正に領収いたしました")
            .Line($" (うち消費税額 ¥{receipt.TaxAmount:N0})")
            .Feed();

        builder.Line($" {receipt.Store.Name}");
        builder.LineWrapped(receipt.Store.Address, " ");
        if (!string.IsNullOrWhiteSpace(receipt.Store.Phone)) builder.Line($" TEL:{receipt.Store.Phone}");
        builder.Feed();

        AppendFooter(builder, receipt, printedAt);
        return builder.Cut().ToArray();
    }

    private static void AppendStoreHeader(EscPosBuilder builder, ReceiptData receipt)
    {
        builder.Align(EscPosAlign.Center).Line(receipt.Store.Name);
        builder.LineWrapped(receipt.Store.Address);
        if (!string.IsNullOrWhiteSpace(receipt.Store.Phone)) builder.Line(receipt.Store.Phone);
        builder.Feed();
    }

    private static void AppendFooter(EscPosBuilder builder, ReceiptData receipt, DateTime printedAt)
    {
        builder.Align(EscPosAlign.Left)
            .Line($"{printedAt.ToString("yy年MM月dd日HH:mm", Japanese)}({receipt.AppVersion})");
    }
}
