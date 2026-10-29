namespace CvWpfpos.Devices;

/// <summary>レシート用紙の幅。TM-m30II は 58mm / 80mm の 2 種類に対応する。</summary>
public enum PosPaperWidth
{
    Mm58 = 58,
    Mm80 = 80,
}

public static class PosPaperWidthExtensions
{
    /// <summary>Font A（12×24 ドット）で 1 行に印字できる半角桁数。全角文字は 2 桁分を占める。</summary>
    /// <remarks>印字可能幅は 58mm 用紙で 360 ドット、80mm 用紙で 576 ドット。</remarks>
    public static int Columns(this PosPaperWidth width) => width == PosPaperWidth.Mm80 ? 48 : 30;

    /// <summary>バーコードのモジュール幅。用紙幅からはみ出さない最大値を選ぶ。</summary>
    public static byte BarcodeModuleWidth(this PosPaperWidth width) => width == PosPaperWidth.Mm80 ? (byte)3 : (byte)2;

    /// <summary>TM-m30II の GS ( E Function 5（a=3）で用紙幅を指定するカスタム値。</summary>
    public static byte CustomizedValue(this PosPaperWidth width) => width == PosPaperWidth.Mm80 ? (byte)6 : (byte)2;

    public static PosPaperWidth FromMillimeters(int millimeters) => millimeters >= 80 ? PosPaperWidth.Mm80 : PosPaperWidth.Mm58;
}
