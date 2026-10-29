using CvWpfpos.Models;
using System.IO.Ports;
using System.Text;

namespace CvWpfpos.Devices;

/// <summary>
/// TM-m30II へ Bluetooth の仮想 COM ポート経由で ESC/POS コマンドを送信します。
/// </summary>
public sealed class EpsonPosPrinter : IDisposable
{
    private const int BaudRate = 115200;
    private static readonly byte[] InitializeCommand = { 0x1B, 0x40 };
    private static readonly byte[] SelectShiftJisCommand = { 0x1C, 0x43, 0x01 };
    // FS ( C Function 48, m=2: 文字列のエンコード方式を UTF-8 に設定する。
    private static readonly byte[] SelectUtf8EncodingCommand = { 0x1C, 0x28, 0x43, 0x02, 0x00, 0x30, 0x02 };
    private static readonly byte[] SelectJapaneseInternationalCharacterSetCommand = { 0x1B, 0x52, 0x08 }; // 国際文字セット: 日本
    private static readonly Encoding ShiftJisEncoding = CreateShiftJisEncoding();

    // GS ( E Function 1 / 2: ユーザー設定モードを開始／終了（終了時にソフトウェアリセット）する。
    private static readonly byte[] EnterUserSettingModeCommand = { 0x1D, 0x28, 0x45, 0x03, 0x00, 0x01, 0x49, 0x4E };
    private static readonly byte[] ExitUserSettingModeCommand = { 0x1D, 0x28, 0x45, 0x04, 0x00, 0x02, 0x4F, 0x55, 0x54 };
    private static readonly byte[] UserSettingModeNotice = { 0x37, 0x20, 0x00 };

    // GS ( E pL=2 pH=0 fn=6 a=3 : カスタム値「用紙幅」の設定値送信要求
    private static readonly byte[] QueryPaperWidthCommand = { 0x1D, 0x28, 0x45, 0x02, 0x00, 0x06, 0x03 };
    private const byte ResponseHeader = 0x37;
    private const byte ResponseIdentifier = 0x27;
    private const byte ResponseSeparator = 0x1F;
    private const byte ResponseTerminator = 0x00;

    private readonly SerialPort serialPort;
    private readonly Encoding shiftJis;
    private readonly PosPaperWidth fallbackPaperWidth;
    private bool disposed;

    public EpsonPosPrinter(string portName, PosPaperWidth fallbackPaperWidth = PosPaperWidth.Mm58)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);

        shiftJis = ShiftJisEncoding;
        this.fallbackPaperWidth = fallbackPaperWidth;
        PaperWidth = fallbackPaperWidth;
        serialPort = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            WriteTimeout = 5_000,
            ReadTimeout = 2_000,
            DtrEnable = false,
            RtsEnable = false
        };
    }

    public bool IsOpen => !disposed && serialPort.IsOpen;

    /// <summary>プリンタに設定されている用紙幅。Open 時に問い合わせて確定する。</summary>
    public PosPaperWidth PaperWidth { get; private set; }

    /// <summary>用紙幅をプリンタから取得できたか。false の場合は設定値（既定）を使っている。</summary>
    public bool IsPaperWidthDetected { get; private set; }

    /// <summary>Bluetooth 仮想 COM ポートを開き、プリンターを初期化して用紙幅を取得します。</summary>
    public void Open()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (serialPort.IsOpen)
        {
            return;
        }

        serialPort.Open();
        ConfigurePaperWidth();
        Write(InitializeCommand);
        Write(SelectUtf8EncodingCommand);
        Write(SelectJapaneseInternationalCharacterSetCommand);
    }

    /// <summary>お買上げレシートを印字してカットします。</summary>
    public void PrintReceipt(ReceiptData receipt)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(receipt);
        Write(ConvertDocumentToUtf8(ReceiptDocumentBuilder.BuildSalesReceipt(receipt, PaperWidth, shiftJis, DateTime.Now)));
    }

    /// <summary>領収書を印字してカットします。</summary>
    public void PrintTaxInvoice(ReceiptData receipt)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(receipt);
        Write(ConvertDocumentToUtf8(ReceiptDocumentBuilder.BuildTaxInvoice(receipt, PaperWidth, shiftJis, DateTime.Now)));
    }

    /// <summary>設定済みの用紙幅を照会し、appsettings.json の指定と異なる場合だけプリンタ設定を更新する。</summary>
    private void ConfigurePaperWidth()
    {
        PaperWidth = fallbackPaperWidth;
        IsPaperWidthDetected = false;
        try
        {
            serialPort.DiscardInBuffer();
            Write(QueryPaperWidthCommand);
            if (ParsePaperWidthResponse(ReadResponse()) is not { } detected) return;

            IsPaperWidthDetected = true;
            if (detected == fallbackPaperWidth)
            {
                PaperWidth = detected;
                return;
            }

            SetPaperWidth(fallbackPaperWidth);
            PaperWidth = fallbackPaperWidth;
        }
        catch (TimeoutException)
        {
            // 応答なしの場合は不必要な不揮発メモリ書き込みを避け、設定値をレイアウトの既定値として使う。
        }
    }

    /// <summary>
    /// GS ( E Function 5（a=3）で用紙幅を設定する。設定は不揮発メモリへ書き込まれるため、実際の設定値と異なる場合だけ呼び出す。
    /// </summary>
    private void SetPaperWidth(PosPaperWidth paperWidth)
    {
        serialPort.DiscardInBuffer();
        Write(EnterUserSettingModeCommand);
        if (!ReadResponse().SequenceEqual(UserSettingModeNotice))
        {
            throw new InvalidOperationException("TM-m30II がユーザー設定モードへの移行を通知しませんでした。");
        }

        Write(BuildSetPaperWidthCommand(paperWidth));
        Write(ExitUserSettingModeCommand);

        // Function 2 はソフトウェアリセットを実行する。リセット完了前の初期化コマンド送信を避ける。
        Thread.Sleep(500);
    }

    /// <summary>NUL 終端まで応答を読み取る。</summary>
    private byte[] ReadResponse()
    {
        var response = new List<byte>(16);
        while (response.Count < 32)
        {
            var value = serialPort.ReadByte();
            if (value < 0) break;
            response.Add((byte)value);
            if (value == ResponseTerminator) break;
        }
        return [.. response];
    }

    internal static PosPaperWidth? ParsePaperWidthResponse(byte[]? response)
    {
        if (response == null) return null;

        // ヘッダ(37H) 識別子(27H) を探し、区切り(1FH)から NUL までを設定値として読む
        for (var i = 0; i + 3 < response.Length; i++)
        {
            if (response[i] != ResponseHeader || response[i + 1] != ResponseIdentifier) continue;

            var separator = Array.IndexOf(response, ResponseSeparator, i);
            if (separator < 0) return null;

            var terminator = Array.IndexOf(response, ResponseTerminator, separator);
            if (terminator < 0) terminator = response.Length;

            var value = Encoding.ASCII.GetString(response, separator + 1, terminator - separator - 1).Trim();
            return value switch
            {
                "2" => PosPaperWidth.Mm58,
                "6" => PosPaperWidth.Mm80,
                _ => null,
            };
        }
        return null;
    }

    internal static byte[] BuildSetPaperWidthCommand(PosPaperWidth paperWidth) =>
    [0x1D, 0x28, 0x45, 0x04, 0x00, 0x05, 0x03, paperWidth.CustomizedValue(), 0x00];

    /// <summary>帳票先頭のShift_JIS選択をUTF-8選択に置き換え、印字文字をUTF-8へ変換する。</summary>
    internal static byte[] ConvertDocumentToUtf8(byte[] document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var shiftJisOffset = InitializeCommand.Length;
        if (!document.AsSpan().StartsWith(InitializeCommand) ||
            !document.AsSpan(shiftJisOffset).StartsWith(SelectShiftJisCommand))
        {
            throw new InvalidOperationException("帳票データの先頭に想定した Shift_JIS 初期化コマンドがありません。");
        }

        // 帳票ビルダーの桁計算は全角を2桁として扱うCP932のバイト数に依存する。
        // そのため組み立て後に文字データだけUTF-8へ変換し、既存のレイアウトを維持する。
        var utf8Document = new List<byte>(document.Length + 64);
        utf8Document.AddRange(InitializeCommand);
        utf8Document.AddRange(SelectUtf8EncodingCommand);

        for (var index = shiftJisOffset + SelectShiftJisCommand.Length; index < document.Length;)
        {
            var value = document[index];
            if (value < 0x80)
            {
                utf8Document.Add(value);
                index++;
                continue;
            }

            var byteCount = IsShiftJisLeadByte(value) && index + 1 < document.Length ? 2 : 1;
            utf8Document.AddRange(Encoding.UTF8.GetBytes(ShiftJisEncoding.GetString(document, index, byteCount)));
            index += byteCount;
        }

        return [.. utf8Document];
    }

    private static bool IsShiftJisLeadByte(byte value) =>
        value is >= 0x81 and <= 0x9F or >= 0xE0 and <= 0xFC;

    private static Encoding CreateShiftJisEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    private void EnsureOpen()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!serialPort.IsOpen)
        {
            throw new InvalidOperationException("シリアルポートが開かれていません。");
        }
    }

    private void Write(byte[] buffer) => serialPort.Write(buffer, 0, buffer.Length);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        serialPort.Dispose();
        disposed = true;
    }
}
