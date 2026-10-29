using CvWpfpos.ViewModels._06Uriage;
using System.Collections.ObjectModel;
using System.Windows;

namespace CvWpfpos.Views._06Uriage;

public partial class PosHoldListView : Helpers.BaseWindow
{
    public PosHoldListViewModel ViewModel => (PosHoldListViewModel)DataContext;

    public PosHoldListView(ObservableCollection<PosHoldEntry> holdList)
    {
        InitializeComponent();
        DataContext = new PosHoldListViewModel(holdList);
        ViewModel.RequestClose += (_, _) => DialogResult = true;
    }
}
