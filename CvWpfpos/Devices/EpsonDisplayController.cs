using System.IO.Ports;
using System.Text;

namespace CvWpfpos.Devices;

/// <summary>
/// EPSON DM-D30 にESC/POS互換のシリアルコマンドを送信します。
/// </summary>
public sealed class EpsonDisplayController : IDisposable
{
    public const int DisplayLineByteLength = 20;
    private const int BaudRate = 19200;
    private static readonly byte[] InitializeDisplayCommand = { 0x1B, 0x40 };

    // US ( G, pL=2, pH=0, fn=97, m=1: Shift_JISコード体系を選択します。
    private static readonly byte[] SelectShiftJisCharacterCodeSystemCommand = { 0x1F, 0x28, 0x47, 0x02, 0x00, 0x61, 0x01 };

    private readonly SerialPort serialPort;
    private readonly Encoding shiftJis;
    private bool disposed;

    public EpsonDisplayController(string portName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        shiftJis = Encoding.GetEncoding(932);

        serialPort = new SerialPort(portName, BaudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            WriteTimeout = 2_000
        };
    }

    public bool IsOpen => !disposed && serialPort.IsOpen;

    /// <summary>ポートを開き、表示器を初期化します (ESC @)。</summary>
    public void Open()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (serialPort.IsOpen)
        {
            return;
        }

        serialPort.Open();
        Write(InitializeDisplayCommand); // ESC @
        Write(SelectShiftJisCharacterCodeSystemCommand);
    }

    /// <summary>画面を消去します (FF)。</summary>
    public void ClearDisplay()
    {
        EnsureOpen();
        Write(new byte[] { 0x0C }); // Form Feed
    }

    /// <summary>2行x20バイトの会計表示を送信します。</summary>
    public void UpdateDisplay(string? line1Text, string? line2Text)
    {
        EnsureOpen();
        ClearDisplay();
        Write(FormatLine(line1Text));
        Write(FormatLine(line2Text));
    }

    /// <summary>
    /// Shift_JISで20バイトに揃えます。全角文字を途中で分断しません。
    /// </summary>
    private byte[] FormatLine(string? value)
    {
        var result = new List<byte>(DisplayLineByteLength);

        foreach (var character in value ?? string.Empty)
        {
            var bytes = shiftJis.GetBytes(character.ToString());
            if (result.Count + bytes.Length > DisplayLineByteLength)
            {
                break;
            }

            result.AddRange(bytes);
        }

        while (result.Count < DisplayLineByteLength)
        {
            result.Add(0x20);
        }

        return result.ToArray();
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
