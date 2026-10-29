using CvWpfpos.Helpers;
using CvWpfpos.ViewModels._00System;
using System.ComponentModel;
using System.Windows;

namespace CvWpfpos.Views._00System;

/// <summary>
/// ログイン画面。認証に成功したら DialogResult=true で閉じ、App が売上入力画面へ遷移する。
/// </summary>
public partial class LoginView : BaseWindow
{
    public LoginView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        if (DataContext is LoginViewModel viewModel) viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LoginViewModel oldViewModel) oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is LoginViewModel newViewModel) newViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not LoginViewModel viewModel) return;

        switch (e.PropertyName)
        {
            case nameof(LoginViewModel.IsAuthenticated) when viewModel.IsAuthenticated:
                SetDialogResult(true);
                break;
            case nameof(LoginViewModel.IsCancelled) when viewModel.IsCancelled:
                SetDialogResult(false);
                break;
        }
    }

    private void SetDialogResult(bool result)
    {
        // ShowDialog 以外で表示された場合に備えて安全に設定する
        try { DialogResult = result; }
        catch (InvalidOperationException) { Close(); }
    }
}
