# プロモーションアニメ レンダラ（動画生成AI不使用・GPU シェーダ描画）

Creative Vision 10 のプロモーションアニメを、主人公の一枚絵（`Doc/promo/assets4anime/github-logo-gemini-*.png`）と repo 内の実データだけから、Python + GLSL のコードで1コマずつ描いて mp4 にするツール。
動画生成AI・画像生成AI・既成の映像素材は使わない。

2026-10-23 に次の3本を作成した（出力は `Doc/promo/anime/out/`、`.gitignore` 対象）。

| 台本 | 作品 | 内容 | 尺 |
|---|---|---|---|
| `op.py` | 明日へ飛べ（アニメOP風） | 主題歌の1番＋エンディングの一撃に拍同期。一枚絵のリグで歌の口パク・まばたき・髪/帽子/イヤリングの揺れ・表情差し替え。アニメ調の空と雲、魔法陣、実コミットの螺旋、CV.net→CV10 のバトン、200の灯籠、実画面の翼、3階層の扉、雲海の日の出 | 68秒 |
| `drama.py` | 魔女っ子エンジニアの開発日誌（ボイス付きショートアニメ） | 4コマ（`dev4koma-20260710.jpg`）を原作に、VOICEVOX の台詞と口パクで演じる。表情差分の背景がそのまま心情で変わる部屋になる。実コード・実画面・帳票・実件数のモンタージュ | 76秒 |
| `book.py` | まほうのしごと絵本（3D の飛び出す絵本） | 机の上の絵本がひらき、紙細工が立ち上がる。シャドウマップの影と被写界深度。主人公の紙細工は毎コマ描き直すので本の上でもまばたきして動く。主題歌の旋律のオルゴール編曲と語り | 60秒 |

## GPU シェーダによる高速描画

合成・変形・光の処理はすべて ModernGL（OpenGL 4.3）の GLSL シェーダで行う。内部は2倍の解像度（3840x2160）で描いて縮小し、1コマに3回描くモーションブラーを入れても、RTX 5080 で68秒の作品を約50秒（実時間より速い）で書き出せる。
CPU の numpy で描く `../promo-video`（v1 で約5.5分）と比べて桁違いに速いため、静止画で確認→全編書き出し→見直しを何度も回せる。

## 構成

| ファイル | 役割 |
|---|---|
| `engine.py` | GPU 合成エンジン。板（ホモグラフィ）の描画、独自シェーダの板、全画面パス、ブルーム・色調・粒状感・閃光の仕上げ、文字の画像化、粒子の一括描画、モーションブラーの蓄積。補間・ffmpeg・WAV 書き出しは `../promo-video/core.py` を使う |
| `prep.py` | 一枚絵からリグ素材を作る: 切り抜き（GrabCut＋帽子の補正）、背景の穴埋め、頭・帽子・リボン・髪・イヤリング・体・手の重み、目の上下端の表と目を消した肌、口の差分 |
| `rig.py` | リグ。1枚の GLSL で、重み付きの変形（頭の傾き・帽子とリボンの追従・髪の揺れ・イヤリングの振り子・呼吸・手の打鍵）、まばたき、口パク、表情の差し替え、縁取り・リムライト・シルエットを描く。髪などの二次運動はばねの積分 |
| `fx.py` | 演出シェーダ: 空のグラデーションと星、魔法陣、粒子（光・きらめき・雪・花びら・灯籠・ぼけ）、集中線、放射の背景、光条、実画面が溶けて現れる窓 |
| `clouds.py` | アニメ背景調の積雲。球の高さ場をなめらかに合成して法線を求め、塊ごとの陰影と縁の光を作る（色はシェーダで付ける） |
| `songsync.py` | 主題歌の譜割り（`../promo-song/song.py`）から歌詞の行・モーラの時刻・母音を取り出し、歌だけの音源の音量と合わせて口の形を返す |
| `voice.py` | VOICEVOX で台詞を合成し、モーラの時刻（口パク用）とともにキャッシュする |
| `pop3d.py` | 3D 描画: メッシュ、シャドウマップ（PCF）、紙の質感、木の机、被写界深度 |
| `film.py` | 書き出し（静止画・コンタクトシート・区間・本番）。台本は `setup(gl)` / `frame(gl, t)` / `audio()` / `DURATION` を持つ |
| `op.py` `drama.py` `book.py` | 台本（3作品）。冒頭コメントに秒割り |
| `lines2.py` | `drama.py` の台詞（表示する字幕と読み上げ文） |

素材と作業ファイルは `.gitignore` 対象の `Doc/promo/` に置く（`PROMO_DIR`・`ANIME_DIR` で変更可）。

```text
Doc/promo/assets4anime/  主人公の4表情・4コマ・開発構想PDF（入力）
Doc/promo/assets/        実画面 screens/、帳票 forms/、ロゴ logo_alpha.png（../promo-video/prepare_assets.py が作る）
Doc/promo/song/          主題歌の完成版・伴奏・歌だけの音源、VOICEVOX Engine（../promo-song）
Doc/promo/anime/work/    rig/（prep.py の出力）、clouds/、voice/（台詞のキャッシュ）
Doc/promo/anime/out/     op.mp4 drama.mp4 book.mp4、stills/（確認用静止画）
```

## 準備

- Windows、OpenGL 4.3 以上の GPU（RTX 5080 で確認）。Python 3.14 で確認。
- `pip install -r requirements.txt`（numpy / pillow / scipy / imageio-ffmpeg / moderngl / opencv-python）。ffmpeg は imageio-ffmpeg 同梱のものを使う。pdftoppm（poppler）を PATH に置く（`op.py` が開発構想 PDF から CV.net の画面を切り出す）。
- フォント: Windows 標準の Noto Sans JP（可変）、游明朝、Segoe UI、Segoe UI Symbol、Cascadia Mono。
- 主題歌の音源（`../promo-song` の手順で作る）と、台詞用の VOICEVOX Engine（同 README の準備）。エンジンが起動していなければ `voice.py` が自動で起動する。

```text
python prep.py            リグ素材（初回のみ。切り抜きをやり直すときは --recut）
```

## 使い方

コマンドはこのフォルダで実行する。日本語を出力するため PowerShell / Git Bash では `PYTHONIOENCODING=utf-8` を付ける。

```text
python film.py op --stills 5.5,33,57          指定秒の静止画
python film.py op --sheet 0,68,24             等間隔の静止画をシートに
python film.py op --range 38 46               区間だけ mp4（音声付き）
python film.py op                             本番 → Doc/promo/anime/out/op.mp4
python film.py drama
python film.py book
```

書き出しは 1920x1080・30fps・H.264（CRF16、`-tune animation`）、音声 AAC 256k。

## 仕組みの要点

- **一枚絵を動かす**: 4つの表情画像はキャラの位置がそろっているため、顔の領域だけ・全体を、重みで混ぜて表情を変える。変形は描画先の画素から元画像の座標を逆にたどる（頭は首を軸に回転、帽子・リボン・イヤリングは遅れて追従、髪は下ほど大きく波打つ）。背景は穴埋めした板の上に切り抜きを重ねるので、動いても背景が引きずられない。
- **まばたき**: 目の領域（まつげ〜下まぶた）を画像から測り、列ごとの上端・下端を4次式で近似した表を作る。閉じるときは目を消した肌で上から覆い、元のまつげの帯を下ろしながら細くする。閉じきると弧を描いた閉じ目になる。
- **口パク**: ハッピー顔の開いた口を切り出し、開き（縦）と幅（横）を母音ごとに拡縮して、口を消した顔に重ねる。歌は譜割りの母音と歌だけの音源の音量、台詞は VOICEVOX のモーラ長と台詞の音量から決める。
- **主人公の紙細工**（book）: リグの絵を毎コマ 1600px のオフスクリーンに描き、型抜きの白い縁ごと3Dの板に貼る。
- **オルゴール**（book）: 主題歌の Bメロ・サビの旋律とコードを 112BPM に直し、FM のベル音で合成してリバーブをかける。

## 内容の根拠（2026-10-23、HEAD 15142a1b 時点）

| 数値・表記 | 根拠 |
|---|---|
| 1,807 コミット、初回 2025-11-25「初期リポジトリ」 | `git rev-list --count HEAD`、`git log --reverse`（`op.py` は実行時に git log から読む） |
| 199 画面（View） | `CvWpfclient/Views` の業務フォルダ（`Sub` を除く）の `*View.xaml` の数 |
| 130 帳票 | `printform/*.qfm` の数 |
| 1,219 テスト | `Tests` 内の `[Fact]` `[TestMethod]` `[Test]` の数 |
| 延べ200社 | README の CV.net 導入実績 |
| 2027年1月 リリース予定 | README |
| public partial のコード | `CvWpfclient/Helpers/ViewModels/BaseKinInputViewModel.cs` 38〜42行 |
| CV.net の画面・技術 | `Doc/promo/assets4anime/開発構想CV10.pdf` 4・7ページ |

数値を更新するときは数え直してから台本の定数を変える（`drama.py` の `stats`、`book.py` は文言のみ）。

## クレジット（公開時に必須）

| 作品 | 表記 |
|---|---|
| op | VOICEVOX:四国めたん、VOICEVOX:波音リツ、VOICEVOX:小夜/SAYO、VOICEVOX:九州そら（主題歌） |
| drama | VOICEVOX:四国めたん、VOICEVOX:九州そら（台詞）。BGM は主題歌の伴奏 |
| book | VOICEVOX:九州そら（語り） |

各声の利用規約は `Doc/promo/song/README.md` 1章を参照し、公開前に最新版を再確認する。

## 目視確認で直した点（再発防止）

- ACES 等のトーンマップはイラストの色を白っぽく変える。0.8 までは素通しにし、それより上だけを丸める。ブルームの閾値は 1.1（イラストの白は光らせない）。
- 光条（god rays）は明部全体を拾うと画面が白飛びする。光源からの距離で重みを絞る。
- 朝焼けの淡い空の上の白文字は読めない。文字の背後に暗い光を敷くか、濃い空にする。
- 3D カメラの右方向は `cross(up, forward)`。逆にすると板に貼った文字が鏡文字になる。
- 表裏で絵を変える紙は、三角形の巻き方向ではなく、法線が視点を向くかで表裏を決める（右ページだけ真っ暗になった）。
- FBO のテクスチャは下の行から並ぶ。オフスクリーンに描いた絵を板に貼るときは上下を反転して描く。
- GLSL では `half` は予約語。
- フォントにない記号は豆腐になる（Segoe UI Bold に ✓ ✦ はない）。記号は Segoe UI Symbol（`sym`）で描き、静止画で確認する。
- VOICEVOX の語りは読点ごとの間が長い。`pauseLengthScale`（`voice.say(..., pause=)`）と話速で尺に合わせる。
- 元絵に描かれた効果音の文字（TAP! 等）は、その画像を全体に使うと出る。ハイテンション顔は顔の領域だけに使う。
- 部屋の背景の板は 2048px の正方形なので、倍率 0.94 未満では画面の横が余る。
