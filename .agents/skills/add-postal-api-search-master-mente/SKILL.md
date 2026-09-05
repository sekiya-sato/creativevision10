---
name: add-postal-api-search-master-mente
description: Adds a Japan Post postal-code API search button to master maintenance screens that have PostalCode and Address1-3 fields, including WPF View, ViewModel, DI usage, and verification steps.
---

# Add Postal API Search to Master Mente

このスキルは、`CvWpfclient` のマスターメンテ画面に `〒API検索` ボタンを追加し、郵便番号から `Address1` `Address2` `Address3` を設定するための手順をまとめたものです。クライアントは既存の gRPC 住所検索を使い、日本郵便 API の認証・再試行はサーバ側に閉じる。

## いつ使うか

- `PostalCode` と `Address1` `Address2` `Address3` を持つマスターメンテ画面へ、同じ郵便番号検索UIを横展開するとき
- 既存の `PostalAddressSearchHelper` と `IPostalAddressService` を使って住所入力を追加するとき
- View / ViewModel の最小変更で既存の住所検索フローを横展開するとき

## 現行経路の確認

実装前に `CvWpfclient/Helpers/PostalAddressSearchHelper.cs`、`CodeShare/IPostalAddress.cs`、`CvServer/Services/SearchByPostalCodeService.cs`、`CvServer/Program.cs` を確認する。現行では Helper が `IPostalAddressService.SearchByPostalCodeAsync` を gRPC で呼び、サーバの `SearchByPostalCodeService` が外部 API とトークンを管理し、`Program.cs` がサービスを公開する。過去のクライアント DI・appsettings・トークン実装を前提に横展開しない。

## 実装手順

### 1. ViewModel に検索コマンドを追加

- `CurrentEdit.PostalCode` を使う画面では、`[RelayCommand] async Task SearchPostalCode()` を追加する
- 画面ごとの重複を減らすため、`CvWpfclient/Helpers/PostalAddressSearchHelper.cs` の `SearchAndApplyAsync()` を使う
- Helper は 3〜7 桁を正規化し、1件なら即時、複数件なら選択ダイアログを経て `applyAddress` を呼ぶ。画面側で検索・例外・複数候補の処理を重複実装しない

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

## 確認手順

1. 3〜7桁（ハイフン・全角数字を含む入力も含める）の郵便番号で `〒API検索` を押す
2. 1件では `Address1-3`、複数件では選択した住所が反映されることを確認する
3. gRPC 到達確認が必要なら `SearchByPostalCodeService` のエラー種別を確認する。外部 API の認証・URL・トークンを View 側に追加しない
4. `CvWpfclient` をビルドする

```powershell
C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"
```

## 更新履歴

- **v0.1.0 (2026-04-11)**: 顧客 / 得意先 / 仕入先 / システム管理マスタへの横展開手順を初版作成
