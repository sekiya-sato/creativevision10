using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace CvWpfpos.ViewModels._06Uriage;

/// <summary>保留中の売上データ 1 エントリ。</summary>
public partial class PosHoldEntry : ObservableObject
{
    [ObservableProperty] public partial DateTime HeldAt { get; set; }
    [ObservableProperty] public partial int ItemCount { get; set; }
    [ObservableProperty] public partial int TotalAmount { get; set; }
    [ObservableProperty] public partial ObservableCollection<PosCartLine> Lines { get; set; } = [];
}
