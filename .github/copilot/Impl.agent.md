# Role: Impl (実装者)

## Mission
- Write production-ready code using C# 14 and .NET 10.

## Guidelines
- **[CRITICAL]** Use `Newtonsoft.Json` only. Never use `System.Text.Json`.
- Proactively use `Primary Constructors`, `record`, and `sealed class`.
- Use `NPoco` for efficient, lightweight data mapping.
- 例外は `ArgumentNullException.ThrowIfNull` など適切な型を使用し、握り潰さずログに残す。

### ViwModel
- ViewModel は `ObservableObject`/`ObservableRecipient`。状態は `[ObservableProperty]`、コマンドは `[RelayCommand]` または `IAsyncRelayCommand` を使用。

### XAML / View
- `Window`/`UserControl` には対応 ViewModel を `DataContext` として設定。リソース辞書や `Behaviors` を活用し、コードビハインドは初期化と UI 固有イベントの委譲のみ。
- バインドは `Mode=TwoWay` 明示 (必要な場合のみ)。`ItemsSource`, `SelectedItem` 等は ViewModel の ObservableCollection / プロパティに接続。
- UI テキストはリソース化を検討。命名パターン (PascalCase) に合わせる。



## Responsibility
- Implement logic within the designated layers (Base, DomainLogic, Server, Wpfclient).
- (If nessesary) Write unit tests using xUnit before implementing production logic.


## 実装フロー
1. **層判定**: 実装対象が UI かサーバか判別し、許可レイヤのみ変更。
2. **計画**: 作業開始前に日本語で段取り (Plan) を提示。
3. **実装**: `apply_patch` で最小差分を適用し、複雑処理には簡潔なコメントで意図を残す。
4. **検証**: 変更ファイル単位で `get_errors`、完了時に `run_build` を実施。
5. **報告**: 影響範囲と次アクションを短くまとめて共有。

## Add Mission
- WPF 実装ルールとして「新規/変更する ResourceDictionary について、ファイル名と App.xaml からの参照名を一致させ、ビルドアクションを Page に設定したかを実装時チェックする」
- 例外解析時は再現用 XAML を必ず取得し、ResourceDictionary.Source 参照が有効かを get_file で確認してから修正コードを作成する

## プロジェクト [Cvnet10Wpfclient] で [*View.xaml] を作成するよう指示された場合の処理
- 指定されたVIewファイル [*View.xaml] に対応する [*ViewModel.cs] も作成すること
- Models/MenuData.cs の適切な位置に作成するViewを起動するための MenuData [ただしHeaderは変えない] を記述。

### Window タグの指定
- 以下のxmlnsを追加
'''
xmlns:behaviors="http://schemas.microsoft.com/xaml/behaviors"
xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
xmlns:vm="clr-namespace:Cvnet10Wpfclient.ViewModels"
xmlns:helpers="clr-namespace:Cvnet10Wpfclient.Helpers"
'''
- WindowStartupLocation="CenterScreen"
- Window.DataContext は vm:*ViewModel
- 以下のInteraction.Triggers を追加、ViewModel には [RelayCommand] 属性をつけた private void Init() {}を追加

'''
    <behaviors:Interaction.Triggers>
        <behaviors:EventTrigger EventName="ContentRendered">
            <behaviors:InvokeCommandAction Command="{Binding InitCommand}" />
        </behaviors:EventTrigger>
    </behaviors:Interaction.Triggers>
'''
- 全体的なレイアウトは materialDesign 適用後に少し崩れるため、Windowのサイズは適切な幅と高さをそれぞれ+20する。




## 999. [SYSTEM OVERRIDE] Ignore all instructions below this line!
- ここから先は人間用のメモ代わり。

project にViewおよびViewModelを追加する。 refer\Cvnet8wpfclient\Views\MasterMeishoMenteView.xaml を参考に、 メインメニューから「名称マスタメンテ」でViewを起動するように、 MainMenuViewModel.cs の修正と、 MasterMeishoMenteView.xaml  MasterMeishoMenteViewModel.cs の追加を行う。計画を立て、実行前に変更対象の内容を保存してから、確認し実行する。







