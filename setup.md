# インストールガイド

## 目次

- [最小構成でビルドして実行する](#最小構成でビルドして実行する)
- [印刷機能を使用する](#印刷機能を使用する)
- [CV.net の旧 DB に接続して直接変換する](#cvnet-の旧-db-に接続して直接変換する)
- [商品画像や社員画像を使用する](#商品画像や社員画像を使用する)
- [天気情報や郵便番号などの公開 API を使用する](#天気情報や郵便番号などの公開-api-を使用する)
- [サーバを本格運用する](#サーバを本格運用する)
- [クライアントを配布形式にして自動更新に対応する](#クライアントを配布形式にして自動更新に対応する)
- [開発者ガイド](#開発者ガイド)

## 最小構成でビルドして実行する

1. リポジトリをクローンします。

```bash
gh repo clone sekiya-sato/creativevision10
cd creativevision10
```

2. 必要に応じて印刷機能を無効化します。

- `CvPrints/CvPrints.csproj` を開き、`PropertyGroup` 内の `PrintEnable` を `false` に変更します。

3. サーバをビルドして実行します。

```bash
dotnet run --project CvServer/CvServer.csproj
```

4. サーバを起動したまま、別ターミナルでクライアントを実行します。

```bash
cd creativevision10
dotnet run --project CvWpfclient/CvWpfclient.csproj
```

- 初期 DB を使用する場合、ログイン ID とパスワードは DB 作成日です。
- 形式は `yyyyMMdd` です。
- 例: サーバ起動日が `20270130` の場合、ログイン ID とパスワードはどちらも `20270130` です。

## 印刷機能を使用する

この機能を利用するには、Accenture 社の `PrintStream Core` が必要です。

1. `CvPrints/CvPrints.csproj` を開き、`PropertyGroup` 内の `PrintEnable` を `true` に変更します。
2. `CvPrints/` 配下に `printstream.jar` を配置します。
3. サーバを再ビルドして実行します。

### PrintStream Core とは

- Accenture 社が提供する、印刷・帳票ソリューション群の中核製品です。
- 専用の帳票設計ツールを使って、帳票レイアウトを比較的容易に作成できます。
- DTP 社の `CV.net` 製品では、この機能が組み込まれていました。
- `Creative Vision 10` では、`CV.net` の PrintStream 連携機能をさらに強化した形で組み込んでいます。

## CV.net の旧 DB に接続して直接変換する

- `CvServer/appsettings.json` を修正します。
- 必要に応じて `appsettings.Production.json` などの環境別設定ファイルを使用してください。
- `ConnectionStrings` セクションの `oracle` に、`CV.net` への接続文字列を設定します。

## 商品画像や社員画像を使用する

- 商品画像は `CvServer/img/` に配置します。ファイル名は `(商品CD).jpg` です。
- 社員画像は `CvServer/imgshain/` に配置します。ファイル名は `(社員CD).jpg` です。

## 天気情報や郵便番号などの公開 API を使用する

- `CvServer/appsettings.json` を修正します。
- 必要に応じて `appsettings.Production.json` などの環境別設定ファイルを使用してください。

### 天気情報

- OpenWeatherMap の API キーを取得します: <https://openweathermap.org/>
- `Application` セクションの `OpenWeatherApiKey` に API キーを設定します。

### 郵便番号から住所を検索する

- 郵便番号・デジタルアドレス API のキーを取得します: <https://guide-biz.da.pf.japanpost.jp/>
- `JapanPostBiz` セクションの `ClientId` に `ClientId` を設定します。
- `JapanPostBiz` セクションの `SecretKey` に `SecretKey` を設定します。

## サーバを本格運用する

Ubuntu 24.04 LTS + nginx で構成する場合の一例です。

- `/etc/nginx/sites-enabled/default` を編集し、`http2` を有効化します。
- `location /` から `CvServer` の gRPC ポートへ転送するよう設定します。
- 簡易的に起動する場合は `tmux` を使い、`dotnet exec CvServer.dll` で実行します。
- 本格運用する場合は service 化して登録し、自動起動に対応させます。

## クライアントを配布形式にして自動更新に対応する

### Velopack による配布ファイル作成

1. `vpk` をインストールします。

```bash
dotnet tool install -g vpk
```

2. `VS2026` の開発者コマンドプロンプトから `publish-velopack.bat` を実行します。
3. `Version` は `publish-velopack.bat` 実行時にリビジョン（パッチ番号）が `+1` されます。
4. `major.minor` は手動で変更します。
5. リビジョンを `0` にしたい場合は、事前に `-1` を設定しておきます。
6. `CvWpfclient/appsettings.json` を修正します。
7. 必要に応じて `appsettings.Production.json` などの環境別設定ファイルも修正します。
8. `Update:FeedUrl` に、クライアント配布先の URL を設定します。
9. Velopack で作成されたファイルと `index.html` を、すべて配布先 URL へ配置します。
10. 必要に応じて、WSL2 上の `publish.sh` から `scp` や `ftp` を使って配布先へコピーします。

```bash
bash ~/bin/publish.sh
```

## 開発者ガイド

- 2026年6月時点では `Visual Studio 2026 Community` を推奨しています。
- AI コーディングツールとして `Codex` と `OpenCode` を使用しています。
- コードベースの調査や関係性の把握には `graphify` を使用します。
