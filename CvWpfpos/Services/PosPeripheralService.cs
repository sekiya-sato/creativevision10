using CvWpfpos.Devices;
using CvWpfpos.Models;

namespace CvWpfpos.Services;

public sealed class PosPeripheralService : IDisposable
{
    private readonly PosSettings settings;
    private EpsonDisplayController? display;
    private EpsonPosPrinter? printer;

    public PosPeripheralService(PosSettings settings) => this.settings = settings;

    public bool IsDisplayOpen => display?.IsOpen == true;
    public bool IsPrinterOpen => printer?.IsOpen == true;

    /// <summary>接続中のプリンタに設定されている用紙幅。未接続時は設定値。</summary>
    public PosPaperWidth PaperWidth => printer?.PaperWidth ?? PosPaperWidthExtensions.FromMillimeters(settings.PaperWidthMm);

    /// <summary>用紙幅をプリンタから取得できたか。false なら appsettings.json の PaperWidthMm を使っている。</summary>
    public bool IsPaperWidthDetected => printer?.IsPaperWidthDetected == true;

    public void ConnectDisplay()
    {
        display?.Dispose();
        display = new EpsonDisplayController(settings.DisplayPortName);
        display.Open();
    }

    public void ConnectPrinter()
    {
        printer?.Dispose();
        printer = new EpsonPosPrinter(settings.PrinterPortName, PosPaperWidthExtensions.FromMillimeters(settings.PaperWidthMm));
        printer.Open();
    }

    public Task UpdateDisplayAsync(string line1, string line2, CancellationToken cancellationToken) =>
        display?.IsOpen == true ? Task.Run(() => display.UpdateDisplay(line1, line2), cancellationToken) : Task.CompletedTask;

    /// <summary>お買上げレシートを印字する。</summary>
    public Task PrintAsync(ReceiptData receipt, CancellationToken cancellationToken)
    {
        if (printer?.IsOpen != true) throw new InvalidOperationException($"{settings.PosPrinterName} が接続されていません。");
        return Task.Run(() => printer.PrintReceipt(receipt), cancellationToken);
    }

    /// <summary>領収書を印字する。</summary>
    public Task PrintTaxInvoiceAsync(ReceiptData receipt, CancellationToken cancellationToken)
    {
        if (printer?.IsOpen != true) throw new InvalidOperationException($"{settings.PosPrinterName} が接続されていません。");
        return Task.Run(() => printer.PrintTaxInvoice(receipt), cancellationToken);
    }

    public void Dispose()
    {
        display?.Dispose();
        printer?.Dispose();
    }
}
