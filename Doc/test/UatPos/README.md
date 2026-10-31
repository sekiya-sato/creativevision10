# POS VM駆動UATハーネス（UatPos）

CvWpfpos（POS端末）の**実ViewModel**と `PosGrpcClient` を直接駆動し、専用DB・専用CvServerに対して
売上・再送・返品・商品検索・取消・日次精算を無人実行する。設計判断記録 §2.18（POSサーバI/Fの標準CRUD統合）の受入確認用。

## 1. 確認する内容

| 項目 | 確認 |
| --- | --- |
| 売上入力 | 税区分混在（標準・軽減・非課税）の画面表示額と、サーバ保存額・レシートの一致 |
| 再送 | 同じ端末取引IDの再送で既存売上が返り、二重計上しない |
| 返品 | 返品区分・CalcFlag・正値の税込合計 |
| 商品検索 | SKU選択で店舗上代・税区分・JANをサーバから取得 |
| 取消 | POS売上のみ取消可、二重取消・POS以外は拒否、再印字が会計時と一致 |
| 日次精算 | レジ別集計（他レジ・事務所入力の売上を含めない）、現金売上の釣銭控除、同じ営業日・レジの二重登録拒否、計算上の現金・差額が画面と一致 |
| 在庫 | 逐次更新の実在庫と、伝票からの独立集計を倉庫×商品×色×サイズで照合 |
| 迂回防止 | POS伝票の一括追加を拒否 |

取消ボタンは確認ダイアログ（MessageBox）を出すため、ダイアログ以降と同じ `PosGrpcClient.CancelSaleAsync` を直接呼ぶ。
プリンタ・客用表示機は接続しない（印字失敗のメッセージになる）。レシートは印字データから文字だけを取り出して保存する。

## 2. 実行

[使用ガイドライン](../../spec/使用ガイドライン.md)の「UAT前後の認証切替」で認証を解除し、CvServerを再ビルドしてから実行する。終了後は必ず認証を戻して再ビルドする。

```powershell
C:\gitroot\UT\vscmd.bat dotnet build Doc\test\UatPos\UatPos.csproj
.\Doc\test\UatPos\bin\Debug\net10.0-windows10.0.19041\UatPos.exe --manage-server --url http://127.0.0.1:5031
```

| オプション | 意味 |
| --- | --- |
| `--manage-server` | CvServerの起動と正規停止をハーネスが行う（UatVmの `CvServerProcess` を共用） |
| `--url <url>` | 専用CvServerのURL。既定 `http://127.0.0.1:5031` |
| `--sqlite <path>` | 専用DB。省略時は `out/uatpos-<日時>/uatpos.db` に空DBを新規作成する。開発DB・旧開発DBは拒否する |

シード（税率定義・店舗・担当・商品3件・SKU）は汎用CRUDで投入する。商品・店舗コードは実行時刻を付けるため、同じDBで再実行できる。
終了コードは 0 が全PASS、1 が FAIL、2 が引数誤り。

## 3. 証跡

`out/uatpos-<日時>/` に `result.md`（判定表）、`trace.log`、`receipt-sale.txt` / `receipt-return.txt`（レシート文字列）、専用DBが出る。`out/` はコミットしない。
