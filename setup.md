# インストールの手引 Installation Guide

目次 Table of Contents

- 最小Buildおよび実行
- 印刷機能を使用する
- CV.netの旧DBと接続し直接変換を行う
- 商品画像や社員画像を使う
- 天気情報や郵便番号などの公開APIを使用する
- サーバを本格運用する
- クライアントを配布形式にする

# 最小Buildおよび実行 (Minimal Build and Run)

	リポジトリのクローン (Clone the repository)
		gh repo clone sekiya-sato/creativevision10
	パラメータ調整 (Adjust parameters)
		CvPrints/CvPrints.csproj
			PropertyGroup:PrintEnable を false に変更 (In the PropertyGroup section, set PrintEnable to false.)
	サーバービルド＆実行 (Build and run the server)
		リポジトリフォルダへ移動 cd creativevision10
		サーバ実行 dotnet run --project CvServer/CvServer.csproj
	クライアントビルド＆実行 (Build and run the client)
		サーバ実行させたままで別ターミナルでリポジトリフォルダへ移動
		クライアント実行 dotnet run --project CvWpfclient/CvWpfclient.csproj

# 印刷機能を使用する (Accenture社のPrintStream Coreが必要)

	CvPrints/CvPrints.csproj
		PropertyGroup:PrintEnable を true に変更
	CvPrints/ に printstream.jar を配置して、サーバをリビルド＆実行

	PrintStream Core とは？
	Accenture社が提供している印刷・帳票関連ソリューション群の中核となる製品
	帳票レイアウトを専用の帳票設計ツールによって簡単に作成することが可能
	この機能を組み込んでいたのがDTP社のCV.net製品
	Creative Vision 10 は、CV.netのprintstream連携機能をより強化した形で組み込んでいる

# CV.netの旧DBと接続し直接変換を行う

	CvServer/appsettings.json を修正 (あるいは appsettings.Production.json など)
		"ConnectionStrings" セクション "oracle" にCV.netへの接続文字列を設定

# 商品画像や社員画像を使う

	CvServer/img 商品画像フォルダ (商品CD).jpg をおく
	CvServer/imgshain 社員画像フォルダ (社員CD).jpg をおく

# 天気情報や郵便番号などの公開APIを使用する

	CvServer/appsettings.json を修正 (あるいは appsettings.Production.json など)
	天気情報 : OpenWeatherMapのAPIキーを取得 https://openweathermap.org/
		"Application", "OpenWeatherApiKey" " にAPIキーを記述
	郵便番号から住所を検索 :郵便番号・デジタルアドレスのAPIキーを取得 https://guide-biz.da.pf.japanpost.jp/
		"JapanPostBiz", "ClientId" にClientIdを記述
		"JapanPostBiz", "SecretKey" にSecretKeyを記述



- 住所入力で郵便番号から住所を取得したい: 郵便番号・デジタルアドレスのAPIキーを取得 https://guide-biz.da.pf.japanpost.jp/

	"JapanPostBiz", "ClientId" にClientIdを記述
	"JapanPostBiz", "SecretKey" にSecretKeyを記述

# サーバを本格運用する

	tmux を使い、dotnet exec CvServer.dll& で実行
	nginx への組み込み、service化して登録、自動起動
	 dotnet exec CvServer.dll
	 ASPNETCORE_ENVIRONMENT=Production などを指定すると、環境ごとの設定が適用される 例: ASPNETCORE_ENVIRONMENT=Production dotnet exec CvServer.dll &
	 起動後、数10秒-1分程度でAPIが利用可能になる(DB処理、自動実行開始、初期化処理など)

# クライアントを配布形式にする



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





