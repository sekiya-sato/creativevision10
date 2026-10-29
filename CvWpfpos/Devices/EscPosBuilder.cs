using System.Text;

namespace CvWpfpos.Devices;

public enum EscPosAlign { Left = 0, Center = 1, Right = 2 }

/// <summary>
/// ESC/POS のコマンド列を組み立てるビルダ。
/// 桁数（用紙幅）を保持し、Shift_JIS のバイト数を 1 桁 = 半角 1 文字として桁揃えを行う
/// （全角文字は 2 桁分を占める）。倍角中は有効桁数が半分になる点も考慮する。
/// </summary>
public sealed class EscPosBuilder
{
    private static readonly byte[] InitializeCommand = [0x1B, 0x40];
    private static readonly byte[] SelectShiftJisCommand = [0x1C, 0x43, 0x01];
    private static readonly byte[] SelectJapaneseInternationalCharacterSetCommand = [0x1B, 0x52, 0x08];
    private static readonly byte[] FullCutCommand = [0x1D, 0x56, 0x00];

    private readonly List<byte> buffer = [];
    private readonly Encoding shiftJis;
    private int widthScale = 1;

    public EscPosBuilder(PosPaperWidth paperWidth, Encoding shiftJis)
    {
        PaperWidth = paperWidth;
        this.shiftJis = shiftJis;
    }

    public PosPaperWidth PaperWidth { get; }

    /// <summary>用紙幅いっぱいの桁数。倍角中は半分になる。</summary>
    public int Columns => Math.Max(1, PaperWidth.Columns() / widthScale);

    public EscPosBuilder Raw(params byte[] command) { buffer.AddRange(command); return this; }

    /// <summary>プリンタを初期化し、Shift_JIS と日本の国際文字セットを選択する。</summary>
    public EscPosBuilder Initialize() => Raw(InitializeCommand)
        .Raw(SelectShiftJisCommand)
        .Raw(SelectJapaneseInternationalCharacterSetCommand);

    public EscPosBuilder Align(EscPosAlign align) => Raw(0x1B, 0x61, (byte)align);

    public EscPosBuilder Emphasis(bool on) => Raw(0x1B, 0x45, (byte)(on ? 1 : 0));

    /// <summary>下線。0 で解除、1〜2 でドット数。</summary>
    public EscPosBuilder Underline(int dots) => Raw(0x1B, 0x2D, (byte)Math.Clamp(dots, 0, 2));

    /// <summary>文字の縦横倍率（1〜8）。GS ! n。</summary>
    public EscPosBuilder Scale(int width, int height)
    {
        width = Math.Clamp(width, 1, 8);
        height = Math.Clamp(height, 1, 8);
        widthScale = width;
        return Raw(0x1D, 0x21, (byte)(((width - 1) << 4) | (height - 1)));
    }

    public EscPosBuilder NormalSize() => Scale(1, 1);

    public EscPosBuilder Feed(int lines = 1)
    {
        for (var i = 0; i < lines; i++) buffer.Add(0x0A);
        return this;
    }

    /// <summary>1 行印字する。Shift_JIS体系では漢字を含む2バイト文字をプリンタが自動処理する。</summary>
    public EscPosBuilder Line(string? text = null)
    {
        buffer.AddRange(shiftJis.GetBytes(text ?? string.Empty));
        buffer.Add(0x0A);
        return this;
    }

    /// <summary>左右に振り分けて 1 行印字する。入りきらない場合は左側を切り詰める。</summary>
    public EscPosBuilder LineLeftRight(string left, string right)
    {
        var rightWidth = Width(right);
        var leftLimit = Math.Max(0, Columns - rightWidth);
        var trimmedLeft = Truncate(left, leftLimit);
        return Line(trimmedLeft + new string(' ', Math.Max(0, leftLimit - Width(trimmedLeft))) + right);
    }

    /// <summary>右寄せで 1 行印字する。</summary>
    public EscPosBuilder LineRight(string text) => Line(PadLeft(Truncate(text, Columns), Columns));

    /// <summary>数量と金額を明細行として印字する（数量はおよそ中央、金額は右寄せ）。</summary>
    public EscPosBuilder LineQuantityAmount(string quantity, string amount)
    {
        var amountField = PadLeft(amount, Columns / 2);
        var quantityField = PadLeft(quantity, Columns - Width(amountField));
        return Line(quantityField + amountField);
    }

    /// <summary>区切り線を用紙幅いっぱいに印字する。</summary>
    public EscPosBuilder Separator(char character = '-') => Line(new string(character, Columns));

    /// <summary>桁数で折り返しながら印字する。住所など長い文字列に使う。折り返し後の行にも indent を付ける。</summary>
    public EscPosBuilder LineWrapped(string? text, string indent = "")
    {
        if (string.IsNullOrWhiteSpace(text)) return this;

        var limit = Math.Max(1, Columns - Width(indent));
        var remaining = text.Trim();
        while (remaining.Length > 0)
        {
            var take = 0;
            var width = 0;
            while (take < remaining.Length)
            {
                var next = Width(remaining[take].ToString());
                if (width + next > limit) break;
                width += next;
                take++;
            }
            if (take == 0) take = 1;
            Line(indent + remaining[..take]);
            remaining = remaining[take..];
        }
        return this;
    }

    /// <summary>CODE39 のバーコードを HRI（読み取り文字）付きで印字する。開始／終了の * はプリンタが付加する。</summary>
    public EscPosBuilder Code39(string data, byte heightDots = 60)
    {
        Raw(0x1D, 0x68, heightDots);                      // GS h : 高さ
        Raw(0x1D, 0x77, PaperWidth.BarcodeModuleWidth()); // GS w : モジュール幅
        Raw(0x1D, 0x48, 0x02);                            // GS H : HRI をバーコードの下に印字
        Raw(0x1D, 0x66, 0x00);                            // GS f : HRI のフォント A
        Raw(0x1D, 0x6B, 0x04);                            // GS k m=4 : CODE39（NUL 終端）
        buffer.AddRange(Encoding.ASCII.GetBytes(data));
        buffer.Add(0x00);
        return this;
    }

    public EscPosBuilder Cut() => Feed(3).Raw(FullCutCommand);

    public byte[] ToArray() => [.. buffer];

    private int Width(string text) => shiftJis.GetByteCount(text);

    private string PadLeft(string text, int columns) => new string(' ', Math.Max(0, columns - Width(text))) + text;

    private string Truncate(string text, int columns)
    {
        if (Width(text) <= columns) return text;

        var width = 0;
        var take = 0;
        while (take < text.Length)
        {
            var next = Width(text[take].ToString());
            if (width + next > columns) break;
            width += next;
            take++;
        }
        return text[..take];
    }
}
