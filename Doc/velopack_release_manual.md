# Velopack Release Manual

## 目的

- `CvWpfclient` を ClickOnce 相当の更新方式から Velopack 配布へ移行した後の、版数更新と配布手順をまとめる。

## 前提

- 配布対象は `win-x64`
- `packId` は `CreativeVision10`
- アプリ版数は `CvWpfclient/appsettings.json` の `Application.Version` を正とする
- 更新フィードURLは `CvWpfclient/appsettings.Production.json` の `Update:FeedUrl` を使う
- `vpk` は事前にインストールしておく

## 事前準備

1. `CvWpfclient/appsettings.json` の `Application.Version` を更新する
2. 必要に応じて `CvWpfclient/appsettings.Production.json` の `Update:FeedUrl` を本番URLへ変更する
3. `vpk` が未インストールなら以下を実行する

```bat
dotnet tool install -g vpk --version 0.0.1298
```

## 配布手順

1. Windows のコマンドプロンプトでソリューションフォルダを開く
2. `publish-velopack.bat` を実行する

```bat
publish-velopack.bat
```

3. スクリプトは以下を順に実行する
- `appsettings.json` から `Application.Version` を取得
- `dotnet publish` を `win-x64` / self-contained で実行
- `vpk pack --packId CreativeVision10 --packVersion <Version>` を実行

## 生成物

- publish 出力: `CvWpfclient/bin/publish-velopack`
- Velopack の release 生成物: `vpk pack` の既定出力先

## 運用メモ

- `publish-velopack.bat` の先頭で自動でVersionのリビジョンを+1する (`CvWpfclient/publish-velopack.version.ps1`)
- `publish-velopack.bat` の末尾で `scp` コピー処理を実行
- アプリ起動時の版数と配布版数を揃えるため、版数は必ず `appsettings.json` の `Application.Version` を更新してから配布する
- 配布URLを変更した場合は、クライアント側 `appsettings.Production.json` も同時に更新する

## 動作確認

1. 現行版をインストールする
2. `Application.Version` を上げて再度 `publish-velopack.bat` を実行する
3. 配布先へ release 一式を配置する
4. クライアントの「システムアップデート」画面から更新確認を実行する
5. 更新適用後にアプリが再起動することを確認する
