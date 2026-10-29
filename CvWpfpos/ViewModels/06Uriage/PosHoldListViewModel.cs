using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CvWpfpos.ViewModels._06Uriage;

public partial class PosHoldListViewModel : ObservableObject
{
    [ObservableProperty] public partial ObservableCollection<PosHoldEntry> HoldList { get; set; }
    [ObservableProperty] public partial PosHoldEntry? SelectedEntry { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;

    public event EventHandler? RequestClose;

    public PosHoldListViewModel(ObservableCollection<PosHoldEntry> holdList)
    {
        HoldList = holdList;
    }

    [RelayCommand]
    private void Resume()
    {
        if (SelectedEntry == null) { StatusMessage = "保留データを選択してください。"; return; }
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
