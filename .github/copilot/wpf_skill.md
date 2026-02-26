---
name: wpf-design
description: >
  Cvnet10WpfclientプロジェクトにおけるWPF/XAMLの高品質UIデザインを生成するためのスキル定義。
  業務マスタ画面（BaseWindow）とダッシュボード画面（ZzMainMenuView）の2種類のウィンドウパターンを使い分け、
  MaterialDesignThemes とカスタム ResourceDictionary を組み合わせた「プロダクションレベルの業務UI」を実装する。
  汎用的なAI生成UIの陳腐さを避け、テーマ整合性と業務効率を両立したデザインを実現する。
---

このスキルは Cvnet10Wpfclient における WPF/XAML UI の設計・実装ガイドである。
ユーザーが「画面を作ってほしい」「XAMLを改善してほしい」「新しいコントロールが必要」などと要求したとき、
このスキルに従い、プロジェクトの既存デザインシステムと調和しながら高品質な実装を行う。

---

## 1. デザイン哲学

コードを書く前に、UIの**コンセプト**を明確にする。

- **目的**: この画面は誰が何のために使うか？業務マスタ管理か、ダッシュボード表示か、ログイン・設定か。
- **画面種別**: 業務マスタ画面（`BaseWindow`）とメニュー・特殊画面（`Window`）を明確に区別する。
- **トーン**: テーマ（ライト: ピンク×パープル / ダーク: ダークグレー）を尊重しつつ、
  `materialDesign:PackIcon` の選択・アクセントカラーの使い方で画面ごとの個性を出す。
- **CRITICAL**: ありきたりなAIデザイン（ベタ塗りグレー背景・均一マージンのレイアウト）は禁止。
  色・余白・アイコンの選択に意図を持って設計する。

---

## 2. テーマ・カラーシステム

### 2.1 ResourceDictionary 構成（`/Resources/`）

| ファイル | 役割 |
|---|---|
| `UIColors.xaml` | ライトテーマの色定義（ピンク×パープル系） |
| `UIColors.Dark.xaml` | ダークテーマの色定義（ダークグレー系） |
| `UIWindow.xaml` | ウィンドウ背景グラデーション・カードブラシ定義 |
| `UIBottun.xaml` | WindowChromeボタン・StatusChipスタイル（ZzMainMenu専用） |
| `UICommon.xaml` | 共通スタイル（TreeView・DataGrid列ヘッダー・TextBlock等） |

**CRITICAL**: 色は必ず `{DynamicResource キー名}` で参照する。ハードコーディング禁止。
ライト/ダーク切り替えに対応するため `{StaticResource}` より `{DynamicResource}` を優先する。

### 2.2 標準カラーキー一覧

```
背景系:
  primaryBackColor1        : ZzMainMenu 外枠背景色
  primaryBackColor2        : ZzMainMenu 内背景色
  AppCommonBackgroundBrush : 業務画面（BaseWindow）の汎用背景 ← 業務画面はこれを使う

ボーダー系:
  winBorderColor1/2/3 : ZzMainMenu グラデーションボーダー
  borderColor         : 汎用ボーダー色

テキスト系:
  TitleColor          : メインタイトル文字色
  SubTitleColor       : サブタイトル文字色
  TitleLabelColor     : ラベルタイトル文字色
  plainTextColor0     : 本文テキスト色

パネル系:
  panelColor          : サイドバー・カードパネル背景
  panelOverColor      : ホバー時パネル背景
  panelActiveColor    : アクティブ時パネル背景

アクセントカラー（color1〜color13）:
  color1  #E285DE  lilac
  color2  #784DFD  violet
  color3  #4ADAEC  cyan
  color4  #FB539B  pink (メインアクセント)
  color5  #7E82FC  periwinkle
  color6  #FFC047  amber
  color7  #EF6C96  rose
  color8  #78A3FC  cornflower
  color9  #07F3C0  mint (強調・成功)
  color10 #FBA1AA  blush
  color11 #4CD038  green (正常・稼働)
  color12 #0077B6  ocean blue  ← PackIconのForegroundに推奨
  color13 #023E8A  deep navy

ボタン系:
  buttonColor1 : プライマリボタン色（#6D2FFF = パープル）← 修正/削除/追加ボタンに推奨
```

### 2.3 グラデーションブラシ（UIWindow.xaml）

```xml
<LinearGradientBrush x:Key="InnerWindowBackground" .../>  <!-- ZzMainMenu内背景 -->
<LinearGradientBrush x:Key="HeaderBackgroundBrush" .../>  <!-- SearchTextBox ボタン背景に使用 -->
<SolidColorBrush x:Key="CardBackgroundBrush" Color="#1AFFFFFF"/>
<SolidColorBrush x:Key="CardBorderBrush"     Color="#30FFFFFF"/>
```

---

## 3. ウィンドウ種別と使い分け

このプロジェクトには **2種類** のウィンドウパターンがある。用途を混同しないこと。

| 種別 | 基底クラス | 対象画面 | 背景 |
|---|---|---|---|
| **業務・マスタ画面** | `helpers:BaseWindow` | MasterXxx / SysXxx 系 | `AppCommonBackgroundBrush` |
| **メニュー・特殊画面** | `Window` | ZzMainMenuView のみ | グラデーション（透明） |

### 3.1 業務画面（BaseWindow）の特徴

`helpers:BaseWindow` は `Window` を継承したカスタム基底クラスで、以下を自動提供する。

```csharp
// BaseWindow が自動で行うこと（XAML への記述不要）
WindowStartupLocation = CenterScreen  // 自動
UseLayoutRounding = true               // 自動
SnapsToDevicePixels = true             // 自動
// ContentRendered で InitCommand を自動実行（VMにInitCommandがあれば）
// ESCキーで Close() + Owner.Activate() を自動実行
// Closed時に WeakReferenceMessenger 登録解除
// Closing時に *CancelCommand を自動実行
```

**IMPORTANT**: `BaseWindow` は `InitCommand` を自動実行するため、XAML に `ContentRendered` トリガーを書く必要はない（書いても動くが二重になる）。

### 3.2 業務画面（BaseWindow）の標準 Window 宣言

```xml
<helpers:BaseWindow
    x:Class="Cvnet10Wpfclient.Views.XxxView"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:behaviors="http://schemas.microsoft.com/xaml/behaviors"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:helpers="clr-namespace:Cvnet10Wpfclient.Helpers"
    xmlns:vm="clr-namespace:Cvnet10Wpfclient.ViewModels"
    Title="{Binding Title}"
    Width="1044"
    Height="760"
    FontSize="18"
    Foreground="{DynamicResource MaterialDesignBody}"
    Background="{DynamicResource AppCommonBackgroundBrush}"
    TextOptions.TextFormattingMode="Display"
    mc:Ignorable="d">

    <helpers:BaseWindow.DataContext>
        <vm:XxxViewModel />
    </helpers:BaseWindow.DataContext>

    <helpers:BaseWindow.InputBindings>
        <KeyBinding Command="{Binding DoUpdateCommand}"    Gesture="F2" />
        <KeyBinding Command="{Binding DoDeleteCommand}"    Gesture="F3" />
        <KeyBinding Command="{Binding DoInsertCommand}"    Gesture="F4" />
        <KeyBinding Command="{Binding DoListCommand}"      Gesture="F5" />
        <KeyBinding Command="{Binding DoOutputJsonCommand}" Gesture="F6" />
    </helpers:BaseWindow.InputBindings>

    <!-- コンテンツ -->
</helpers:BaseWindow>
```

ポイント:
- `WindowStartupLocation` は BaseWindow が設定するため XAML への記載不要。
- `FontSize="18"` をウィンドウレベルで設定し、全子要素に継承させる。
- `TextOptions.TextFormattingMode="Display"` をウィンドウレベルで設定する。
- `Foreground="{DynamicResource MaterialDesignBody}"` をウィンドウレベルで設定する。

---

## 4. 業務画面の標準レイアウト（左一覧 ＋ 右詳細）

業務マスタ画面（一覧＋詳細編集）は **3行×3列 Grid** を標準とする。

### 4.1 標準グリッド構造

```xml
<Grid x:Name="grid0">
    <Grid Width="{Binding ActualWidth, ElementName=grid0, Mode=OneWay}"
          Height="{Binding ActualHeight, ElementName=grid0, Mode=OneWay}">
        <Grid.RowDefinitions>
            <RowDefinition Height="12*" />   <!-- ツールバー行 -->
            <RowDefinition Height="Auto" />  <!-- GridSplitter行 -->
            <RowDefinition Height="88*" />   <!-- メインコンテンツ行 -->
        </Grid.RowDefinitions>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="45*" />  <!-- 左：一覧エリア -->
            <ColumnDefinition Width="Auto" /> <!-- GridSplitter列 -->
            <ColumnDefinition Width="55*" />  <!-- 右：詳細エリア -->
        </Grid.ColumnDefinitions>

        <!-- Row0, Col0: 左ツールバー -->
        <Grid Grid.Row="0" Grid.Column="0"> ... </Grid>

        <!-- Row1: 水平 GridSplitter -->
        <GridSplitter Grid.Row="1" Grid.ColumnSpan="3"
                      Height="3" HorizontalAlignment="Stretch" VerticalAlignment="Center"
                      Background="{DynamicResource borderColor}" />

        <!-- Col1: 垂直 GridSplitter -->
        <GridSplitter Grid.RowSpan="3" Grid.Column="1"
                      Width="3" HorizontalAlignment="Center" VerticalAlignment="Stretch"
                      Background="{DynamicResource borderColor}" />

        <!-- Row2, Col0: 左一覧（DataGrid） -->
        <Grid Grid.Row="2" Grid.Column="0"> ... </Grid>

        <!-- Row0, Col2: 右ツールバー -->
        <Grid Grid.Row="0" Grid.Column="2"> ... </Grid>

        <!-- Row2, Col2: 右詳細フォーム -->
        <Grid Margin="10" Grid.Row="2" Grid.Column="2"> ... </Grid>
    </Grid>
</Grid>
```

### 4.2 左ツールバー（Row0, Col0）

```xml
<Grid Grid.Row="0" Grid.Column="0">
    <!-- 閉じる -->
    <Button Width="70" Height="30" Margin="10,10,0,0"
            HorizontalAlignment="Left" VerticalAlignment="Top"
            Background="{DynamicResource panelActiveColor}"
            Command="{Binding ExitCommand}" Content="閉じる" />
    <!-- 一覧取得 -->
    <Button Width="110" Height="30" Margin="120,10,0,0"
            HorizontalAlignment="Left" VerticalAlignment="Top"
            Command="{Binding DoListCommand}" Content="一覧取得 F5" />
    <!-- JSON出力 -->
    <Button Width="110" Height="30" Margin="240,10,0,0"
            HorizontalAlignment="Left" VerticalAlignment="Top"
            Command="{Binding DoOutputJsonCommand}" Content="JSON出力 F6" />
    <!-- 画面タイトル -->
    <Label Height="34" Margin="20,49,0,0" VerticalAlignment="Top"
           Content="{Binding Title, RelativeSource={RelativeSource FindAncestor, AncestorType=Window}}"
           FontSize="14" />
    <!-- アイコン（画面の種類を表す）-->
    <materialDesign:PackIcon Width="62" Height="70" Margin="370,10,0,0"
                              Foreground="{DynamicResource color12}"
                              Kind="AccountBox" />  <!-- Kindは画面ごとに変える -->
</Grid>
```

アイコン `Kind` の選択例:
- 社員: `AccountBox`
- ログイン: `AccountKey`
- 顧客: `AccountGroup`
- 名称: `FormatListBulletedSquare`
- 設定: `ApplicationSettings`
- 在庫: `PackageVariant`
- 売上: `ChartLine`

### 4.3 右ツールバー（Row0, Col2）

```xml
<Grid Grid.Row="0" Grid.Column="2">
    <!-- CRUD ボタン -->
    <Button Width="100" Height="30" Margin="10,10,0,0"
            HorizontalAlignment="Left" VerticalAlignment="Top"
            Background="{DynamicResource buttonColor1}"
            Command="{Binding DoUpdateCommand}" Content="修正 F2" />
    <Button Width="100" Height="30" Margin="130,10,0,0"
            HorizontalAlignment="Left" VerticalAlignment="Top"
            Background="{DynamicResource buttonColor1}"
            Command="{Binding DoDeleteCommand}" Content="削除 F3" />
    <Button Width="100" Height="30" Margin="250,10,0,0"
            HorizontalAlignment="Left" VerticalAlignment="Top"
            Background="{DynamicResource buttonColor1}"
            Command="{Binding DoInsertCommand}" Content="追加 F4" />
    <!-- 件数表示 -->
    <Label Height="40" Margin="383,10,0,0" VerticalAlignment="Top"
           Content="{Binding Count}" ContentStringFormat="{}件数={0:N0}" />
    <!-- メッセージ -->
    <TextBlock Height="34" Margin="24,55,0,0" VerticalAlignment="Top"
               FontSize="16" TextWrapping="Wrap"
               Foreground="{DynamicResource MaterialDesignBody}"
               Text="{Binding Message}" />
</Grid>
```

### 4.4 左一覧エリア（DataGrid標準）

```xml
<Grid Grid.Row="2" Grid.Column="0">
    <DataGrid x:Name="XxxGrid"
              helpers:DataGridScrollAssistant.AutoScrollToSelectedItem="True"
              d:ItemsSource="{d:SampleData ItemCount=5}"
              AutoGenerateColumns="False"
              FrozenColumnCount="2"
              IsReadOnly="True"
              VirtualizingPanel.IsVirtualizing="True"
              VirtualizingPanel.VirtualizationMode="Recycling"
              EnableRowVirtualization="True"
              IsSynchronizedWithCurrentItem="True"
              Style="{StaticResource MaterialDesignDataGrid}"
              HorizontalGridLinesBrush="{DynamicResource borderColor}"
              GridLinesVisibility="Horizontal"
              ItemsSource="{Binding ListData}"
              SelectedItem="{Binding Current}">
        <DataGrid.Columns>
            <DataGridTextColumn Header="コード" Binding="{Binding Code}" Width="100" />
            <DataGridTextColumn Header="名前"   Binding="{Binding Name}" Width="200" />
            <!-- 追加カラム -->
        </DataGrid.Columns>
        <DataGrid.ColumnHeaderStyle>
            <Style TargetType="DataGridColumnHeader"
                   BasedOn="{StaticResource MaterialDesignDataGridColumnHeader}">
                <Setter Property="Background" Value="{DynamicResource color13}" />
                <Setter Property="Foreground" Value="White" />
                <Setter Property="BorderThickness" Value="0.5" />
            </Style>
        </DataGrid.ColumnHeaderStyle>
    </DataGrid>
</Grid>
```

**DataGrid 必須設定**:
- `Style="{StaticResource MaterialDesignDataGrid}"` を必ず指定
- `BasedOn="{StaticResource MaterialDesignDataGridColumnHeader}"` でヘッダーを継承
- `AutoScrollToSelectedItem="True"` で選択行を自動スクロール
- `VirtualizingPanel.IsVirtualizing="True"` + `Recycling` で大量データに対応

### 4.5 右詳細フォーム（Label + TextBox のグリッド）

詳細フォームは **`Height="Auto"` 行の Grid** で組む（マージンによる絶対位置指定は禁止）。

```xml
<Grid Margin="10" Grid.Row="2" Grid.Column="2">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="30*" />  <!-- ラベル列 -->
        <ColumnDefinition Width="70*" />  <!-- 入力列 -->
    </Grid.ColumnDefinitions>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="Auto" />
        <!-- 項目数分追加 -->
    </Grid.RowDefinitions>

    <Label Grid.Row="0" Content="コード" Height="36" Margin="15,10,0,0" VerticalAlignment="Top" />
    <TextBox Grid.Row="0" Grid.Column="1" Height="36" Margin="10,10,10,0" VerticalAlignment="Top"
             Foreground="{DynamicResource MaterialDesignBody}"
             Text="{Binding CurrentEdit.Code}" TextWrapping="Wrap" />

    <Label Grid.Row="1" Content="名前" Height="36" Margin="15,10,0,0" VerticalAlignment="Top" />
    <TextBox Grid.Row="1" Grid.Column="1" Height="36" Margin="10,10,10,0" VerticalAlignment="Top"
             Foreground="{DynamicResource MaterialDesignBody}"
             Text="{Binding CurrentEdit.Name}" TextWrapping="Wrap" />
</Grid>
```

**フォーム設計の原則**:
- 行高さ: `Height="36"` (通常) / `Height="60"` (複数行テキスト)
- マージン: `Margin="10,10,10,0"` (TextBox) / `Margin="15,10,0,0"` (Label)
- `TextBox` には必ず `Foreground="{DynamicResource MaterialDesignBody}"`
- 読み取り専用フィールド: `IsReadOnly="True"` を付与

---

## 5. カスタムコントロール

### 5.1 SearchTextBox（検索付きテキストボックス）

`helpers:SearchTextBox` は入力テキストボックス＋検索ボタンを組み合わせたカスタム UserControl。
参照ダイアログを開くIDフィールドに使用する。

```xml
<helpers:SearchTextBox
    Grid.Row="5" Grid.Column="1"
    Width="135"
    Height="36"
    Margin="10,10,0,0"
    HorizontalAlignment="Left"
    VerticalAlignment="Top"
    Text="{Binding CurrentEdit.Id_Tenpo, UpdateSourceTrigger=PropertyChanged}"
    BorderThickness="0,0,0,1"
    TextHorizontalContentAlignment="Left"
    Command="{Binding DoSelectTenpoCommand}">
    <helpers:SearchTextBox.Style>
        <Style TargetType="{x:Type helpers:SearchTextBox}">
            <Setter Property="Foreground"
                    Value="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Window}}" />
            <Setter Property="Background"
                    Value="{Binding Background, RelativeSource={RelativeSource AncestorType=Window}}" />
            <Setter Property="ButtonBackground"
                    Value="{DynamicResource HeaderBackgroundBrush}" />
        </Style>
    </helpers:SearchTextBox.Style>
</helpers:SearchTextBox>
```

- `Command`: ボタンクリックまたはEnterキーで呼ばれるコマンド（参照ダイアログ開く等）
- `Foreground` / `Background` は親 Window から継承させてテーマ対応
- `ButtonBackground="{DynamicResource HeaderBackgroundBrush}"` は固定

### 5.2 DatePicker（MaterialDesign）

```xml
<DatePicker
    VerticalAlignment="Center"
    materialDesign:HintAssist.Hint="日付"
    FontSize="15"
    IsTodayHighlighted="False"
    Style="{StaticResource MaterialDesignFloatingHintDatePicker}"
    SelectedDate="{Binding Current.SomeDate,
        Converter={StaticResource DateYmd8Converter},
        Mode=TwoWay,
        UpdateSourceTrigger=PropertyChanged}" />
```

### 5.3 ComboBox（MaterialDesign）

```xml
<ComboBox
    FontSize="14"
    Height="30"
    Margin="10,0,10,0"
    VerticalAlignment="Center"
    ItemsSource="{Binding SomeItems}"
    SelectedItem="{Binding Current.SomeEnum, Mode=TwoWay}">
    <ComboBox.ItemTemplate>
        <DataTemplate>
            <TextBlock Foreground="{DynamicResource MaterialDesignBody}"
                       Text="{Binding Converter={StaticResource EnumShimeDisplayConverter}}" />
        </DataTemplate>
    </ComboBox.ItemTemplate>
</ComboBox>
```

### 5.4 DashboardCardView（Views/Controls/）

```xml
<controls:DashboardCardView Width="220"
    Background="{DynamicResource panelColor}"
    Title="Online Users"
    Value="128"/>
```

汎用カスタムコンポーネントは `Views/Controls/` に UserControl として配置する。

---

## 6. タイポグラフィ

### 6.1 標準テキストスタイル（UICommon.xaml）

| スタイルキー | 用途 | FontSize | FontWeight |
|---|---|---|---|
| `SectionTitleText` | セクション見出し | 24 | SemiBold |
| `MetricLabel` | メトリクスラベル | 14 | SemiBold (Opacity 0.85) |

### 6.2 業務画面のテキストガイドライン

- **ウィンドウ基本フォント**: `FontSize="18"` をウィンドウ属性に設定し全継承
- **本文**: `FontSize="16〜18"`, `Foreground="{DynamicResource MaterialDesignBody}"`
- **ラベル**: `FontSize` は親から継承（個別指定不要）
- **注釈・補足**: `FontSize="14" Opacity="0.7"`
- **件数・ID表示**: `ContentStringFormat="{}件数={0:N0}"` などの書式を使用
- **MultiBinding 日時表示**: `StringFormat="{} {0} - {1}"` で複数値を結合

- **CRITICAL**: `TextBox` / `TextBlock` には必ず `Foreground="{DynamicResource MaterialDesignBody}"` を付与する。
  （MaterialDesignThemes との統合のため。Window レベルで設定していても TextBox には個別付与が必要）

---

## 7. インタラクション・エフェクト

### 7.1 ホバーエフェクト（Trigger）

```xml
<ControlTemplate.Triggers>
  <Trigger Property="IsMouseOver" Value="True">
    <Setter TargetName="border" Property="Background" Value="#33FFFFFF"/>
  </Trigger>
  <Trigger Property="IsSelected" Value="True">
    <Setter TargetName="border" Property="Background" Value="#33FFFFFF"/>
  </Trigger>
</ControlTemplate.Triggers>
```

半透明ホワイトオーバーレイ（`#22〜#44FFFFFF`）を基本とする。

### 7.2 DataTrigger パターン

```xml
<Style.Triggers>
    <DataTrigger Binding="{Binding SomeFlag}" Value="True">
        <Setter Property="Visibility" Value="Visible"/>
    </DataTrigger>
    <DataTrigger Binding="{Binding SelectedMenu.IsDialog}" Value="True">
        <Setter Property="Text" Value="ダイアログ画面" />
    </DataTrigger>
</Style.Triggers>
```

### 7.3 DropShadowEffect

強調要素（ダッシュボードタイトル等）に使用する。業務画面での多用は避ける。

```xml
<TextBlock.Effect>
    <DropShadowEffect Color="Black" Direction="315"
                      ShadowDepth="1.5" Opacity="1" BlurRadius="4"/>
</TextBlock.Effect>
```

### 7.4 Storyboard アニメーション

業務画面では過度なアニメーションは避ける。フェードインを使う場合:

```xml
<Border.Triggers>
    <EventTrigger RoutedEvent="Loaded">
        <BeginStoryboard>
            <Storyboard>
                <DoubleAnimation Storyboard.TargetProperty="Opacity"
                                 From="0" To="1" Duration="0:0:0.3"/>
            </Storyboard>
        </BeginStoryboard>
    </EventTrigger>
</Border.Triggers>
```

---

## 8. MaterialDesignThemes 統合

`App.xaml` で以下のように設定されている。

```xml
<materialDesign:BundledTheme BaseTheme="Light"
                             PrimaryColor="DeepPurple"
                             SecondaryColor="Lime"/>
<ResourceDictionary Source="pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml"/>
```

- MaterialDesign のコントロールスタイルを積極的に使用する:
  - DataGrid: `Style="{StaticResource MaterialDesignDataGrid}"`
  - DataGridColumnHeader: `BasedOn="{StaticResource MaterialDesignDataGridColumnHeader}"`
  - DatePicker: `Style="{StaticResource MaterialDesignFloatingHintDatePicker}"`
  - TextBox内部: `Style="{StaticResource MaterialDesignTextBox}"` (SearchTextBox内部)
  - Button(フラット): `Style="{StaticResource MaterialDesignFlatButton}"`
- MaterialDesign のキー（`MaterialDesignBody` 等）は `{DynamicResource}` で参照する。
- `materialDesign:PackIcon` を積極的に使用してアイコンを表示する。
- `materialDesign:HintAssist.Hint` を DatePicker 等に使用する。

---

## 9. MVVM パターン（CommunityToolkit.Mvvm）

### 9.1 ViewModel 基本構造

```csharp
// C# 14 Primary Constructor を使用
public partial class XxxViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "XXX管理";
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private int _count;
    [ObservableProperty] private XxxModel? _current;
    [ObservableProperty] private XxxModel? _currentEdit;
    [ObservableProperty] private ObservableCollection<XxxModel> _listData = [];

    [RelayCommand]
    private async Task InitAsync() { ... }   // BaseWindow が自動実行

    [RelayCommand]
    private async Task DoListAsync() { ... }  // F5

    [RelayCommand]
    private async Task DoUpdateAsync() { ... } // F2

    [RelayCommand]
    private void Exit() => Application.Current.Windows
        .OfType<Window>().FirstOrDefault(w => w.DataContext == this)?.Close();
}
```

### 9.2 DataContext 設定

```xml
<helpers:BaseWindow.DataContext>
    <vm:XxxViewModel />
</helpers:BaseWindow.DataContext>
```

### 9.3 KeyBinding 標準

業務マスタ画面は以下のキーバインディングを標準とする:

```xml
<helpers:BaseWindow.InputBindings>
    <KeyBinding Command="{Binding DoUpdateCommand}"     Gesture="F2" />  <!-- 修正 -->
    <KeyBinding Command="{Binding DoDeleteCommand}"     Gesture="F3" />  <!-- 削除 -->
    <KeyBinding Command="{Binding DoInsertCommand}"     Gesture="F4" />  <!-- 追加 -->
    <KeyBinding Command="{Binding DoListCommand}"       Gesture="F5" />  <!-- 一覧取得 -->
    <KeyBinding Command="{Binding DoOutputJsonCommand}" Gesture="F6" />  <!-- JSON出力 -->
    <!-- ESCキーによるClose()はBaseWindowが自動処理 -->
</helpers:BaseWindow.InputBindings>
```

### 9.4 MultiBinding 表示パターン

```xml
<!-- 登録日/修正日 -->
<TextBlock>
    <TextBlock.Text>
        <MultiBinding StringFormat="{}登録日={0} &#x0a;修正日={1}">
            <Binding Path="CurrentEdit.VdateC" />
            <Binding Path="CurrentEdit.VdateU" />
        </MultiBinding>
    </TextBlock.Text>
</TextBlock>

<!-- 関連エンティティ表示（読取専用） -->
<TextBox IsReadOnly="True" Foreground="{DynamicResource MaterialDesignBody}">
    <TextBox.Text>
        <MultiBinding StringFormat="{}[{0}] {1}">
            <Binding Path="CurrentEdit.VTenpo.Cd" />
            <Binding Path="CurrentEdit.VTenpo.Mei" />
        </MultiBinding>
    </TextBox.Text>
</TextBox>
```

---

## 10. アンチパターン（禁止事項）

| 禁止 | 正しい代替 |
|---|---|
| `Background="Gray"` 等の色のハードコーディング | `{DynamicResource panelActiveColor}` 等を使う |
| `Background="Purple"` のハードコーディング | `{DynamicResource buttonColor1}` を使う |
| `Foreground="Blue"` のハードコーディング | `{DynamicResource color12}` を使う |
| `Background="Black"` のハードコーディング | `{DynamicResource color13}` を使う |
| 詳細フォームの絶対マージン配置（`Margin="10,90,0,0"` 等） | `Grid.Row` + `Height="Auto"` で行ベース配置 |
| `{StaticResource}` を色キーに使用 | `{DynamicResource}` を使う（テーマ切替対応） |
| `Window` を業務画面の基底に使う | `helpers:BaseWindow` を使う |
| `ContentRendered` トリガーで InitCommand を呼ぶ | BaseWindow が自動処理するため不要 |
| `MaterialDesignDataGrid` スタイルを使わない DataGrid | 必ず `Style="{StaticResource MaterialDesignDataGrid}"` を指定 |
| `TextBox`/`TextBlock` に `Foreground` を付けない | `Foreground="{DynamicResource MaterialDesignBody}"` を必ず付与 |
| すべてのレイアウトを `StackPanel` で組む | `Grid` を使いレスポンシブに対応する |
| `.sln` ファイルの使用・生成 | プロジェクトは `.slnx` を使用 |

---

## 11. 実装チェックリスト

### 業務マスタ画面（BaseWindow）新規作成時

- [ ] **基底クラス**: `helpers:BaseWindow` を使用しているか
- [ ] **Window属性**: `FontSize="18"`, `Foreground="{DynamicResource MaterialDesignBody}"`, `Background="{DynamicResource AppCommonBackgroundBrush}"`, `TextOptions.TextFormattingMode="Display"` が設定されているか
- [ ] **DataContext**: `<helpers:BaseWindow.DataContext><vm:XxxViewModel /></helpers:BaseWindow.DataContext>` で設定されているか
- [ ] **KeyBinding**: F2〜F6 が `<helpers:BaseWindow.InputBindings>` に設定されているか
- [ ] **グリッド構造**: 3行×3列（12* / Auto / 88* ✕ 45* / Auto / 55*）になっているか
- [ ] **GridSplitter**: 水平・垂直 GridSplitter に `Background="{DynamicResource borderColor}"` が設定されているか
- [ ] **DataGrid**: `Style="{StaticResource MaterialDesignDataGrid}"` と `BasedOn="{StaticResource MaterialDesignDataGridColumnHeader}"` が設定されているか
- [ ] **詳細フォーム**: マージン絶対配置ではなく `Grid.Row` + `Height="Auto"` で組まれているか
- [ ] **TextBox**: 全 TextBox に `Foreground="{DynamicResource MaterialDesignBody}"` が付与されているか
- [ ] **ボタン色**: 修正/削除/追加は `{DynamicResource buttonColor1}`、閉じるは `{DynamicResource panelActiveColor}` になっているか
- [ ] **PackIcon**: `Foreground="{DynamicResource color12}"` が設定されているか
- [ ] **SearchTextBox**: Style に `Foreground`/`Background` 親継承と `ButtonBackground="{DynamicResource HeaderBackgroundBrush}"` が設定されているか
