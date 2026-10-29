using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfpos.Services;
using System.Collections.ObjectModel;

namespace CvWpfpos.ViewModels._06Uriage;

public partial class PosStaffSelectViewModel : ObservableObject
{
    private readonly PosGrpcClient client;

    [ObservableProperty] public partial ObservableCollection<MasterShain> StaffList { get; set; } = [];
    [ObservableProperty] public partial MasterShain? SelectedStaff { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsBusy { get; set; }

    public event EventHandler? RequestClose;

    public PosStaffSelectViewModel() : this(AppGlobal.Client) { }

    public PosStaffSelectViewModel(PosGrpcClient client)
    {
        this.client = client;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var list = await client.QueryListAsync<MasterShain>(null, "Id", null, 200, CancellationToken.None);
            foreach (var item in list) StaffList.Add(item);
        }
        catch (Exception ex) { StatusMessage = $"読み込みエラー: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Ok()
    {
        if (SelectedStaff == null) { StatusMessage = "担当者を選択してください。"; return; }
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
