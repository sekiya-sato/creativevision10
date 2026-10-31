using CommunityToolkit.Mvvm.ComponentModel;

namespace CvWpfpos.ViewModels._06Uriage;

public partial class PosCartLine : ObservableObject
{
    [ObservableProperty] public partial int LineNo { get; set; }
    [ObservableProperty] public partial string Barcode { get; set; } = string.Empty;
    [ObservableProperty] public partial long ProductId { get; set; }
    [ObservableProperty] public partial string ProductCode { get; set; } = string.Empty;
    [ObservableProperty] public partial long ColorId { get; set; }
    [ObservableProperty] public partial string ColorCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string ColorName { get; set; } = string.Empty;
    [ObservableProperty] public partial long SizeId { get; set; }
    [ObservableProperty] public partial string SizeCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string SizeName { get; set; } = string.Empty;
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Amount))] public partial int UnitPrice { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Amount))] public partial int Quantity { get; set; }
    /// <summary>明細区分（0:Pプロパー 1:Sセール）</summary>
    [ObservableProperty] public partial int Kubun { get; set; }
    /// <summary>明細担当者キー（0=伝票担当を引き継ぐ）</summary>
    [ObservableProperty] public partial long StaffId { get; set; }
    [ObservableProperty] public partial string StaffCode { get; set; } = string.Empty;
    [ObservableProperty] public partial string StaffName { get; set; } = string.Empty;
    /// <summary>消費税区分（MasterShohin.Id_Tax）。端末の税計算に使う。</summary>
    [ObservableProperty] public partial long TaxId { get; set; }
    public int Amount => checked(UnitPrice * Quantity);
}
