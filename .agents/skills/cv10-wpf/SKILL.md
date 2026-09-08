---
name: cv10-wpf
description: >
  Design, implement, review, and refactor WPF screens for the
  Creative Vision 10 application using .NET 10, C#,
  CommunityToolkit.Mvvm, and MaterialDesignInXamlToolkit.
  Use this skill for XAML layout, View/ViewModel design,
  data binding, commands, dialogs, DataGrid screens,
  master-entry screens, search screens, and UI consistency.
---

# CV10 WPF Development

## Technology constraints

Always use:

- .NET 10
- WPF
- C#
- CommunityToolkit.Mvvm
- MaterialDesignInXamlToolkit

Do not migrate or generate code for:

- WinUI 3
- MAUI
- Avalonia
- Windows App SDK

unless explicitly requested.

## CV10 project baseline

Before WPF work, read `wpf-project-guide`. For a single View/ViewModel
addition or update, also follow `wpf-view-workflow`.

- Inspect `CvWpfclient/App.xaml` and its merged dictionaries before adding a
  resource, converter, color, or style.
- Reuse `UIColors.xaml`, `UICommon.xaml`, `UIMainWindow.xaml`, and
  `UIFormStyles.xaml` resources. Prefer existing keys such as `FormTextBox`,
  `FormComboBox`, `FormDatePicker`, `ToolCommandButton`, and
  `MenteDataGridColumnHeader` over local replacements.
- Use `DynamicResource` for theme-aware brushes. Do not scatter
  `SolidColorBrush` values when an existing resource key serves the purpose.
- CV10 uses MaterialDesignInXamlToolkit 5.3.2. Follow the existing
  `ColorZone`, `Card`, `HintAssist`, `DataGridAssist`, and outlined-control
  patterns; copy the nearest screen rather than introducing a new theme style.

## Window lifecycle and services

- Business and master-maintenance screens use `CvWpfclient.Helpers.BaseWindow`
  unless the nearest existing screen establishes another base class.
- `BaseWindow` already runs `InitCommand` after rendering, handles Escape and
  cancellation, applies minimum-size/display-bound handling, and cancels
  running commands on close. Do not add another `ContentRendered` handler to
  invoke `InitCommand`.
- Match the surrounding ViewModel's service access. Existing shared ViewModels
  commonly resolve gRPC services through `AppGlobal.GetGrpcService<T>()`; do
  not introduce constructor injection solely to replace that established path.
  Use ViewModel-owned direct gRPC clients only when the applicable CV10 pattern
  explicitly requires it.

## MVVM

Prefer:

- ObservableObject
- [ObservableProperty]
- [RelayCommand]
- AsyncRelayCommand
- the surrounding ViewModel's established service-access pattern

Avoid business logic in code-behind.

Do not use Click handlers when a ViewModel command is appropriate.

## Layout

Prefer Grid for major screen layout.

Use StackPanel only for simple one-dimensional groups.

Typical CV10 screen structure:

1. Header
2. Search or input conditions
3. Main content
4. DataGrid or detail editor
5. Command area

Prefer:

- Auto sizing for labels and commands
- * sizing for main content
- consistent margins and spacing
- resizable layouts

Avoid hard-coded Width and Height unless necessary.

## DataGrid

For business data-entry screens:

- preserve column alignment
- use explicit column widths where operationally useful
- right-align numeric values
- right-align quantities and monetary amounts
- support keyboard-oriented operation
- avoid excessive row height
- keep important identifiers visible

## Review

When modifying an existing View:

1. Inspect the existing XAML, ViewModel, `App.xaml`, and relevant shared
   resource dictionaries.
2. Preserve the nearest existing View/ViewModel, command, and DataContext
   conventions.
3. For XAML, verify XML structure, xmlns, resource keys, converters,
   bindings/ItemsSource, and lower/right-edge clipping or missing scrolling.
4. Check that BaseWindow lifecycle behavior and keyboard-oriented commands are
   not duplicated or bypassed.
5. Build `CvWpfclient/CvWpfclient.csproj` when source changes require it, then
   run `git diff --check`; use UatVm scenarios when they cover the changed flow.
