using CommunityToolkit.Mvvm.ComponentModel;
using Cvnet10Wpfclient.Views;
using System;
using System.Collections.ObjectModel;

namespace Cvnet10Wpfclient.Models;

public class MenuData : ObservableObject {
    private string? header;
    public string? Header {
        get => header;
        set => SetProperty(ref header, value);
    }

    private bool isExpand;
    public bool IsExpand {
        get => isExpand;
        set => SetProperty(ref isExpand, value);
    }

    private bool isSelected;
    public bool IsSelected {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    private ObservableCollection<MenuData>? subItems;
    public ObservableCollection<MenuData>? SubItems {
        get => subItems;
        set => SetProperty(ref subItems, value);
    }

    private string? addInfo;
    public string? AddInfo {
        get => addInfo;
        set => SetProperty(ref addInfo, value);
    }

    private string? icon;
    public string? Icon {
        get => icon;
        set => SetProperty(ref icon, value);
    }

    private bool isMainMenu;
    public bool IsMainMenu {
        get => isMainMenu;
        set => SetProperty(ref isMainMenu, value);
    }

    private string? color;
    public string? Color {
        get => color;
        set => SetProperty(ref color, value);
    }

    public Type? ViewType { get; set; }

    public bool IsDialog { get; set; } = true;

    public int InitParam { get; set; }

    public static ObservableCollection<MenuData> CreateDefault() => new([
        new MenuData {
            Header = "■ 開始処理 / 終了処理",
            Color = "MediumPurple",
            IsMainMenu = true,
            IsExpand = true,
            SubItems = new ObservableCollection<MenuData> {
                new MenuData {
                    Header = "開始処理：接続テスト",
                    AddInfo = "gRPC",
                    ViewType = typeof(Test20260203View),
                    IsDialog = true
                },
                new MenuData {
                    Header = "開始処理：ログイン",
                    AddInfo = "gRPC",
					ViewType = typeof(LoginView),
					IsDialog = true
				},
                new MenuData {
                    Header = "終了処理：データアップロード",
                    AddInfo = "準備中"
                }
            }
        },
		new MenuData {
			Header = "■ Master",
			Color = "CornflowerBlue",
			IsMainMenu = true,
			SubItems = new ObservableCollection<MenuData> {
				new MenuData {
					Header = "名称マスタメンテ",
					AddInfo = "Generic name setup",
					ViewType = typeof(MasterMeishoMenteView),
					IsDialog = true
				},
				new MenuData { Header = "得意先マスタメンテ", AddInfo = "準備中" },
				new MenuData { Header = "社員LOGINマスタ", AddInfo = "準備中" }
			}
		},
        new MenuData {
            Header = "■ 管理メニュー",
            Color = "SteelBlue",
            IsMainMenu = true,
            SubItems = new ObservableCollection<MenuData> {
                new MenuData { Header = "システム管理マスタ", AddInfo = "準備中" },
                new MenuData { Header = "処理履歴情報", AddInfo = "準備中" },
                new MenuData { Header = "LOGIN履歴情報", AddInfo = "準備中" }
            }
        }
    ]);
}
