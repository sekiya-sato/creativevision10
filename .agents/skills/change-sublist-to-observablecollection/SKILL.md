---
name: change-sublist-to-observablecollection
description: Converts List<T> sub-list properties in a master mente ViewModel to ObservableCollection<T> so that DataGrid row additions/deletions are properly notified to the View. Applies the ApplyFromCurrentEdit / SyncToCurrentEdit pattern used in ShukkaUriageInputViewModel.
---

# Change SubList to ObservableCollection

このスキルは、BaseMenteViewModel<T> を継承したマスターメンテ ViewModel で、CurrentEdit 内の `List<T>?` サブリストプロパティを ViewModel 側の `ObservableCollection<T>` に展開し、行の追加・削除がUIに正しく通知されるようにするパターンを提供します。WPF全体の共通規約は `wpf-project-guide`、画面単位の基本手順は `wpf-view-workflow` を前提とし、本スキルはサブリスト通知問題の解決に特化します。

## いつ使うか

- マスタメンテ画面のサブリスト（原価リスト、色サイズリスト、品質リスト、名称リストなど）で、行を追加・削除しても DataGrid に反映されない問題を修正するとき
- CurrentEdit のエンティティ型（Cvnet10Base 側）が `List<T>?` を持ち、Read-Only のため変更できないとき
- ShukkaUriageInputViewModel / JuchuInputViewModel と同様のパターンを適用したいとき

## パターン概要

### 1. ViewModel に ObservableCollection フィールドを追加

```csharp
[ObservableProperty]
ObservableCollection<SubItemType> editSubItems = [];
```

- `[ObservableProperty]` を付与し、View への変更通知を有効化
- フィールド名は `edit` + 元のプロパティ名（例: `editJgenka`）とし、生成されるプロパティ名は `EditJgenka` となる

### 2. OnCurrentEditChangedCore() で展開（Apply）

```csharp
protected override void OnCurrentEditChangedCore(T? oldValue, T newValue) {
    if (newValue == null) return;
    ApplySubListsFromCurrentEdit();
}

void ApplySubListsFromCurrentEdit() {
    EditSubItems = new ObservableCollection<SubItemType>(
        CurrentEdit.SubItems?.Select(Common.CloneObject) ?? []);
}
```

- CurrentEdit の List から **Clone** して ObservableCollection へ展開
- 直接参照せず Clone することで、キャンセル時に元データを汚さない

### 3. 保存前に書き戻し（Sync）

```csharp
protected override object CreateInsertParam() {
    SyncSubListsToCurrentEdit();
    return base.CreateInsertParam();
}

protected override object CreateUpdateParam() {
    SyncSubListsToCurrentEdit();
    return base.CreateUpdateParam();
}

void SyncSubListsToCurrentEdit() {
    CurrentEdit.SubItems = [.. EditSubItems];
}
```

- `CreateInsertParam()` / `CreateUpdateParam()` を override
- base を呼ぶ前に ObservableCollection → CurrentEdit.List へ書き戻す
- `[.. collection]` （コレクション式）で List<T> に変換

### 4. Add/Delete コマンドを ObservableCollection 操作に変更

```csharp
[RelayCommand]
void AddSubItem() {
    EditSubItems.Add(new SubItemType());
}

[RelayCommand]
void DeleteSubItem() {
    if (SelectedSubItem == null) return;
    EditSubItems.Remove(SelectedSubItem);
    SelectedSubItem = null;
}
```

- `CurrentEdit.SubItems` ではなく `EditSubItems`（ObservableCollection）を操作

### 5. View 側の Binding 修正

```xml
<!-- Before -->
<DataGrid ItemsSource="{Binding CurrentEdit.SubItems}" ... />

<!-- After -->
<DataGrid ItemsSource="{Binding EditSubItems}" ... />
```

## 参照ファイル

- パターン元: `Cvnet10Wpfclient/ViewModels/06Uriage/ShukkaUriageInputViewModel.cs`（ApplyDetailFromCurrent / SyncDetailToCurrent）
- パターン元: `Cvnet10Wpfclient/ViewModels/04Juchu/JuchuInputViewModel.cs`
- 基底クラス: `Cvnet10Wpfclient/Helpers/ViewModels/BaseMenteViewModel.cs`
- 適用例: `Cvnet10Wpfclient/ViewModels/01Master/MasterShohinMenteViewModel.cs`

## 注意事項

- Cvnet10Base のエンティティ型は Read-Only のため、`List<T>?` プロパティ自体を `ObservableCollection<T>` に変えることはできない
- BaseList など UI 専用プロパティがある場合は、Apply 時に設定し、Sync 時には含めないよう注意
- CollectionChanged / PropertyChanged ハンドラが必要な場合（合計再計算など）は ShukkaUriageInputViewModel のパターンを参考に追加

## 更新履歴

- **v0.1.0 (2026-03-24)**: MasterShohinMenteViewModel での適用を元に初版作成
