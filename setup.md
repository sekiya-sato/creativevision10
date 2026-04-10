# インストールの手引

# サーバインストール

- リポジトリのクローン 

	gh repo clone sekiya-sato/creativevision10

- CvServer/appsettings.json の調整
	<pre>
	印刷機能を使用する場合
		appsettings.json "PrintServer" セクション "UsePrint":true に設定
		CvPrints/CvPrints.csproj: <PrintEnable>true</PrintEnable>
		CvPrints/ に printstream.jar を配置、Build時IKVMのnugetパッケージが入っていることを確認
	印刷機能を使用しない場合
		appsettings.json "PrintServer" セクション "UsePrint":false に設定
		CvPrints/CvPrints.csproj: <PrintEnable>false</PrintEnable>
	DBConvert処理を使用する場合
		appsettings.json "ConnectionStrings" セクション "oracle" に接続文字列を設定
	Sqliteファイルを別の名前のdbに変更する場合
		appsettings.json "ConnectionStrings" セクション "sqlite" にベースフォルダからみたdbパスを設定
	</pre>

- ビルド(Windows/Linux環境)

	dotnet build "CvServer/CvServer.csproj"

- 実行

	 dotnet exec CvServer.dll

- 簡易実行環境

	tmux を使い、dotnet exec CvServer.dll& で実行

- 本番実行環境

	nginx への組み込み、service化して登録、自動起動


# クライアントインストール

- リポジトリのクローン 

	gh repo clone sekiya-sato/creativevision10

- CvWpfclient/appsettings.json の調整
	<pre>
	"ConnectionStrings", "Url": サーバのURLを記述
	"Application", "Version": バージョン番号を記述
	"Application", "OpenWeatherApiKey": openweathermapのAPIキーを記述 https://openweathermap.org/
	"Application", "WeatherRegion": openweathermapの地域を記述
	"Application", "FitPosition": メニューのみの場合のWindow位置 Left/Right と Top/Bottom の組み合わせを指定
	</pre>

- ビルド(Windows環境)

	dotnet publish "CvWpfclient/CvWpfclient.csproj" -c Release -r win-x64 --self-contained true
	
	Linux環境の場合: dotnet publish "CvWpfclient/CvWpfclient.csproj" -c Release -r win-x64 --self-contained true /p:EnableWindowsTargeting=true

- Velopackによる配布ファイル作成 (dotnet tool install -g vpk で事前にインストール)
	<pre>
	VS2026の開発者コマンドプロンプトから、publish-velopack.bat を実行
	事前に、appsettings.Production.json を作成しておく
	"Version" は publish-velopack.bat 実行時にリビジョン(パッチ番号)が+1される (major.minor.patch)
	major.minorのほうは手動で変更する、リビジョンを0にしたければ-1を設定しておく
	"appsettings.Production.json"
	</pre>
```
{
	"Update": {
		"FeedUrl": "https://....  クライアントソフトのダウンロード先 配布先URL",
		"Channel": "stable"
	},
	"Application": {
		"Version": "1.0.1"
	}

}
```

- Velopackで作成されたファイル+index.html をすべて配布先URLへ配置

	bash ~/bin/publish.sh  : WSL2にpublish.shを作成し、scpやftpで配布先URLへコピーする



