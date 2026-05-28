# qfm Print Flow Reference

## 現在の起点

- `CvServer/Services/PrintPdf.cs` は `PrintOperation` を受け取り、`data.txt` を作成して `CvPrints.PrintAdapter` を呼ぶ。
- `CvServer/appsettings.json` の既定値は `PrintBaseDir="."`、`PrintFormDir="form"`、`PrintDataDir="data"`、`PrintOutputDir="output"`。
- `printform/MasterMeishoMente.qfm` は現在リポジトリ上で確認できる qfm 実例。
- `CvWpfclient/Helpers/ViewModels/BaseMenteViewModel.cs` が `DoOutputPdfCommand` を持つ。
- `CvWpfclient/ViewModels/01Master/MasterMeishoMenteViewModel.cs` が現在の `FormFile` + `PrintBySqlParam` パターン。

## CSV と item mapping

サーバーは印刷前に Shift_JIS で `data.txt` を書き出す。

`QueryListSqlParam` の場合、`PrintPdf.printPre` が SQL を実行して `WriteDynamicCsv(writer)` を呼ぶ。SQL の選択列順を qfm の項目順として扱う。

帳票作成中は ViewModel の近くに次の対応メモを置くと確認しやすい。

```text
item1  = SQL column 1
item2  = SQL column 2
item3  = SQL column 3
...
```

qfm では次の 2 箇所を同期させる。

- `<datadesc><datarecord><item id="itemN">...`
- レイアウト要素側の `<data calctype="item" datasrc="itemN"/>`

未使用の `<item>` 定義は現行サンプルにもあるため警告扱いに留める。使用されている `datasrc` に対応する item 定義が無い場合は不具合として扱う。

## Encoding

qfm は次の XML 宣言を維持し、ファイル自体も Shift_JIS で保存する。

```xml
<?xml version="1.0" encoding="SHIFT_JIS"?>
```

PowerShell で読むと静的な日本語ラベルが文字化けする場合は、コードページ 932 を明示して読む。

## Layout checklist

- `printstream version="3.0"`
- `datadesc src="file"`
- `path datatype="csv" target="data.txt"`
- 横向きが必要な場合を除き `page orientation="portrait"`
- `page compatibility="3.0.0"`
- ヘッダー行: `recordtype="1"`
- 明細行: 通常の `record`
- 固定ラベル: `<data calctype="static">label</data>`
- CSV 項目: `<data calctype="item" datasrc="itemN"/>`
- 出力日時: `<data calctype="date"/>`
- ページ番号: `<data calctype="page"/>`

## ViewModel checklist

```csharp
protected override string? FormFile => "NewFormName.qfm";

protected override QueryListSqlParam? PrintBySqlParam {
	get {
		if (/* required filter missing */) {
			return null;
		}

		var sql = @"
select Column1, Column2, Column3
from SomeTable
where SomeKey=@0
";
		return new QueryListSqlParam(typeof(SomeEntity), sql, [someKey]);
	}
}
```

印刷ボタンだけを追加しない。ViewModel が qfm ファイル名と印刷データソースを返して初めて印刷機能として完結する。

## Review checklist

- qfm ファイルが `printform/` にあり、ViewModel の `FormFile` 名と完全一致しているか。
- 実行時配置で qfm がサーバーの `form` ディレクトリに届くか。
- すべての `datasrc="itemN"` が `datarecord` に存在するか。
- SQL の select 順と qfm の item 順が一致しているか。
- 必須フィルター未選択時に、無効 SQL を作らず `null` を返しているか。
- qfm を Shift_JIS として読んだとき、固定の日本語ラベルが読めるか。
