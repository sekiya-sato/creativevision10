# インストールの手引

# サーバインストール

- リポジトリのクローン 
	gh repo clone sekiya-sato/creativevision10

- Cvnet10Server/appsettings.json の調整
	印刷機能を使用する場合
		appsettings.json "PrintServer" セクション "UsePrint":true に設定
		Cvnet10Prints/Cvnet10Prints.csproj: <PrintEnable>true</PrintEnable>
		Cvnet10Prints/ に printstream.jar を配置、Build時IKVMのnugetパッケージが入っていることを確認
	印刷機能を使用しない場合
		appsettings.json "PrintServer" セクション "UsePrint":false に設定
		Cvnet10Prints/Cvnet10Prints.csproj: <PrintEnable>false</PrintEnable>
	DBConvert処理を使用する場合
		appsettings.json "ConnectionStrings" セクション "oracle" に接続文字列を設定
	Sqliteファイルを別の名前のdbに変更する場合
		appsettings.json "ConnectionStrings" セクション "sqlite" にベースフォルダからみたdbパスを設定

- ビルド(Windows/Linux環境)
	dotnet build "Cvnet10Server/Cvnet10Server.csproj"

- 実行
	 dotnet exec Cvnet10Server.dll

- 簡易実行環境
	tmux を使い、dotnet exec Cvnet10Server.dll& で実行

- 本番実行環境
	nginx への組み込み、service化して登録、自動起動


# クライアントインストール

- リポジトリのクローン 
	gh repo clone sekiya-sato/creativevision10

- Cvnet10Wpfclient/appsettings.json の調整
	appsettings.json "ConnectionStrings" セクション "Url" に、サーバのURLを記述
	appsettings.json "Application" セクション "Version" にバージョン番号を記述

- ビルド(Windows環境)
	dotnet publish "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" -c Release -r win-x64 --self-contained true

- Velopackによる配布ファイル作成 (dotnet tool install -g vpk で事前にインストール)
	vpk pack --packId CreativeVision10 --packVersion %APP_VERSION% --packDir "%PUBLISH_DIR%" --mainExe CreativeVision10.exe
	事前に、appsettings.Production.json を作成しておく
```
{
	"Update": {
		"FeedUrl": "https://....  クライアントソフトのダウンロード先 配布URL",
		"Channel": "stable"
	},
	"Application": {
		"Version": "1.0.1"
	}

}
```

- Velopackで作成されたファイルをすべて配布URLへ配置、ダウンロード用 index.html を配置



