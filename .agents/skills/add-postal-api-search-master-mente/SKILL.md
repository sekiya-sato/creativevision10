---
name: add-postal-api-search-master-mente
description: Adds a Japan Post postal-code API search button to master maintenance screens that have PostalCode and Address1-3 fields, including WPF View, ViewModel, DI usage, and verification steps.
---

# Add Postal API Search to Master Mente

このスキルは、`CvWpfclient` のマスターメンテ画面に日本郵便APIを使った `〒API検索` ボタンを追加し、郵便番号から `Address1` `Address2` `Address3` を自動設定するための手順をまとめたものです。

## いつ使うか

- `PostalCode` と `Address1` `Address2` `Address3` を持つマスターメンテ画面へ、同じ郵便番号検索UIを横展開するとき
- 既存の `IPostalAddressService` と `JapanPostBizTokenProvider` を使って住所自動入力を追加するとき
- `CvWpfclient` のView / ViewModel / appsettings / DI をまたいで修正するとき

## 前提

- `CvWpfclient/Services/PostalAddressService.cs` が存在し、`IPostalAddressService` が利用可能であること
- `CvWpfclient/Services/JapanPostBizTokenProvider.cs` が存在し、Authorization は `Bearer {token}` 固定で送ること
- `CvWpfclient/App.xaml.cs` で `IPostalAddressService` と `IJapanPostBizTokenProvider` が DI 登録されていること
- `CvWpfclient/appsettings.json` に `JapanPostBiz` セクションがあること

## 実装手順

### 1. ViewModel に検索コマンドを追加

- `CurrentEdit.PostalCode` を使う画面では、`[RelayCommand] async Task SearchPostalCode()` を追加する
- 画面ごとの重複を減らすため、`CvWpfclient/Helpers/PostalAddressSearchHelper.cs` の `SearchAndApplyAsync()` を使う
- 1件ヒット時だけ `PostalCode` と `Address1-3` を反映する

例:

```csharp
[RelayCommand]
async Task SearchPostalCode() {
    await PostalAddressSearchHelper.SearchAndApplyAsync(this, CurrentEdit.PostalCode ?? string.Empty, item => {
        CurrentEdit.PostalCode = item.PostalCode;
        CurrentEdit.Address1 = item.Address1;
        CurrentEdit.Address2 = item.Address2;
        CurrentEdit.Address3 = item.Address3;
    });
}
```

`Current.*` 直バインド画面では `Current` に対して同じ処理を行う。

### 2. View に `〒API検索` ボタンを追加

- `PostalCode` の行だけ内側 `Grid` に分ける
- 左に郵便番号TextBox、右に `〒API検索` ボタンを置く
- 既存フォーム列定義は壊さない

例:

```xml
<Grid Grid.Row="6" Grid.Column="1">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="170" />
        <ColumnDefinition Width="Auto" />
        <ColumnDefinition Width="*" />
    </Grid.ColumnDefinitions>
    <TextBox Grid.Column="0"
        Text="{Binding CurrentEdit.PostalCode, UpdateSourceTrigger=PropertyChanged}" />
    <Button Grid.Column="1"
        Command="{Binding SearchPostalCodeCommand}"
        Style="{StaticResource PostalSearchButton}">
        <StackPanel Orientation="Horizontal">
            <materialDesign:PackIcon Kind="Magnify" />
            <TextBlock Text="〒API検索" />
        </StackPanel>
    </Button>
</Grid>
```

### 3. ボタンスタイルを追加

- 既存 `FormTextBox` の近くに `PostalSearchButton` を定義する
- `MaterialDesignOutlinedButton` をベースにして、余白とPaddingだけを追加する

### 4. 対象画面の洗い出し

現時点の横展開対象:

- `MasterEndCustomerMenteView` / `MasterEndCustomerMenteViewModel`
- `MasterTokuiMenteView` / `MasterTokuiMenteViewModel`
- `MasterShiireMenteView` / `MasterShiireMenteViewModel`
- `MasterSysKanriMenteView` / `MasterSysKanriMenteViewModel`

## URL / 認証の注意

- 検索URLは `GET /api/v2/searchcode/{search_code}`
- 通常の7桁郵便番号検索では、まず `page` と `limit` の必須クエリを優先する
- Authorization は `Bearer {token}` 固定
- token API レスポンスの `token_type` を HTTP スキームへそのまま使わない

## 確認手順

1. 郵便番号7桁を入力して `〒API検索` ボタンを押す
2. 1件ヒット時に `Address1-3` が反映されることを確認する
3. 代替出力先で `CvWpfclient` をビルドする

```bash
/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj /p:OutDir=c:\gitroot\documents\new2022\cv10\artifacts\postalout\"
```

## 更新履歴

- **v0.1.0 (2026-04-11)**: 顧客 / 得意先 / 仕入先 / システム管理マスタへの横展開手順を初版作成
