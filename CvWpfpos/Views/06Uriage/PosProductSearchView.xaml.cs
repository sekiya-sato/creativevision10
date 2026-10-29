using CvWpfpos.ViewModels._06Uriage;
using System.Windows;

namespace CvWpfpos.Views._06Uriage;

public partial class PosProductSearchView : Helpers.BaseWindow
{
    public PosProductSearchViewModel ViewModel => (PosProductSearchViewModel)DataContext;

    public PosProductSearchView()
    {
        InitializeComponent();
        ViewModel.RequestClose += (_, _) => DialogResult = true;
    }
}
