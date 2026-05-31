# 動的SQLの未保証範囲

## 未保証対象
- `CvServer/Services/HandlerClass.cs`
  - `QueryListSqlParam.Sql` をサーバ側でそのまま `_db.Fetch(...)` に流している。
- `CvWpfclient/ViewModels/*`
  - `QueryListSqlParam` に詰めて送るSQLは、呼び出し側の条件組み立て次第で変動する。

## 意味すること
- checked-in 済みの SQL テンプレートは監査できるが、利用者入力や将来の ViewModel 変更による SQL 全パターンを静的に保証することはできない。
- 今回の監査結果は「現在のコード上で確認できた SQL 文字列と組み立てロジック」に限定される。

## 運用提案
- `QueryListSqlParam` を使うViewModel追加時は、SQLite 3.38 で禁止している構文をレビュー項目にする。
- `HandlerClass` のログ出力を活かし、実運用で実行されたSQLを追加監査できるようにする。
