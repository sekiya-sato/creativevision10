using CvWpfpos.Helpers;
using CvWpfpos.ViewModels._06Uriage;
using System.Windows;

namespace CvWpfpos.Views._06Uriage;

/// <summary>
/// 売上入力画面。ログイン成功直後に App から表示されるメインウィンドウ。
/// </summary>
public partial class PosUriageInputView : BaseWindow
{
    public PosUriageInputView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        if (DataContext is PosUriageInputViewModel viewModel) viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PosUriageInputViewModel oldViewModel) oldViewModel.CloseRequested -= OnCloseRequested;
        if (e.NewValue is PosUriageInputViewModel newViewModel) newViewModel.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
