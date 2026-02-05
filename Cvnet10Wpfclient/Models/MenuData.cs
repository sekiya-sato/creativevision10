using CommunityToolkit.Mvvm.ComponentModel;
using Cvnet10Wpfclient.Views;
using System;
using System.Collections.ObjectModel;

namespace Cvnet10Wpfclient.Models;

public partial class MenuData : ObservableObject {
    [ObservableProperty]
    private string? header;

	[ObservableProperty]
	private bool isExpand;

	[ObservableProperty]
	private bool isSelected;

	[ObservableProperty]
	private ObservableCollection<MenuData>? subItems;

	[ObservableProperty]
	private string? addInfo;

	[ObservableProperty]
	private string? icon;

	[ObservableProperty]
	private bool isMainMenu;

	[ObservableProperty]
	private string? color;
    
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
                    IsDialog = false
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
					IsDialog = false
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
