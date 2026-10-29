using CvWpfpos.ViewModels._06Uriage;
using System.Windows;

namespace CvWpfpos.Views._06Uriage;

public partial class PosReceiptListView : Helpers.BaseWindow
{
    public PosReceiptListViewModel ViewModel => (PosReceiptListViewModel)DataContext;

    public PosReceiptListView()
    {
        InitializeComponent();
        ViewModel.RequestClose += (_, _) => DialogResult = false;
    }
}
