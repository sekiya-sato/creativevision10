# 音楽生成モデルの比較（Creative Vision 10 のイメージから作曲）

Creative Vision 10 のイメージだけを渡して、ComfyUI の音楽生成モデルに伴奏・歌声を生成させ、出来栄えを比べるツール。当初のプロモーションソング（`../promo-song/`、VOICEVOX 版）とは別のものとして扱う。

- 出力先: `Doc/promo/song3/`（`.gitignore` 対象。環境変数 `PROMO_SONG3_DIR` で変更可）
- 最終版と結果の説明: `Doc/promo/song3/README.md`
- 途中の音源・ワークフロー・数値: `Doc/promo/song3/work/`

## 対象モデル

| キー | モデル | ComfyUI での使い方 | 必要なファイル |
|---|---|---|---|
| `yue2` | YuE2 3B（int8） | 標準ノード（YuE2GenerateABC → YuE2GenerateMusic） | `checkpoints/yue2_3b_int8_convrot.safetensors`（3.7GB、HF Comfy-Org/YuE2） |
| `minimax` | MiniMax Music 3 | 標準ノード（MiniMaxMusic3TextEncode） | `diffusion_models/minimax_music3_dit_fp16`（4.6GB）、`text_encoders/minimax_music3_text_encoder_pruned_int8_convrot`（8.6GB）、`vae/minimax_music3_dav`（0.2GB）（HF Comfy-Org/MiniMax-Music-3） |
| `acexl` | ACE-Step 1.5 XL Base | 標準ノード（TextEncodeAceStepAudio1.5） | `acestep_v1.5_xl_base_bf16` ほか（導入済み） |
| `heartmula` | HeartMuLa-oss-3B | カスタムノード ComfyUI_FL-HeartMuLa（registry `comfyui_fl-heartmula` 2.0.4） | 初回実行時に `models/heartmula/` へ自動ダウンロード |

## 歌詞とスタイル

歌詞は `gen.py` の `LYRICS` に1つだけ持ち、モデルごとに構成タグの表記とスタイルの書き方を合わせて渡す（`work/prompts/<モデル>.txt` に実際に渡した内容を書き出す）。

| モデル | スタイルの書き方 | 構成タグ |
|---|---|---|
| YuE2 | 言語 → ジャンル → 声 → テンポ → 楽器 → 特徴 の1行（テンプレートの Prompt Guide） | `[Verse]` `[Chorus]` `[Bridge]` `[Outro]`。歌う区間だけ書く |
| MiniMax Music 3 | Global Metadata / Vocal Details / Arrangement の3部構成（公式の Caption 指針） | `[Intro]` `[Verse]` `[Pre-Chorus]` `[Chorus]` `[Bridge]` `[Outro]` |
| ACE-Step XL Base | 説明文。BPM・拍子・キー・言語は専用の入力 | タグに演奏の指示を添える（例 `[Chorus - full band, powerful]`） |
| HeartMuLa | カンマ区切りの短いタグ | `[Intro]` `[Verse]` `[Prechorus]` `[Chorus]` `[Bridge]` `[Outro]` |

歌詞の英字・数字は読みに置き換える（製品名は「クリエイティブ・ビジョン・テン」、AI は「エーアイ」）。

## 使い方

```text
python gen.py workflows                 各モデル×調整違い（VARIANTS）の API 形式ワークフロー → work/workflows/
python gen.py transcribe <音源...>       HeartTranscriptor で歌詞を書き起こすワークフロー（音源は ComfyUI の input に置く）
python gen.py analyze                   work/raw/<モデル>_<版>.flac の数値と、work/transcripts.json の歌詞一致率 → work/compare.md
python gen.py place <音源 出力名>...     -14 LUFS にそろえて song3/<出力名>.m4a へ置く
```

ComfyUI 側は comfy-mcp で `validate_workflow` → `run_workflow(wait=False)` → `job(action="wait")` の順に実行する。生成物は ComfyUI の output の `audio/cv10_song3/<名前>_00001.flac` から `work/raw/<名前>.flac` へ取得する。

歌詞の書き起こしは、ComfyUI の Python 環境で次を実行する（ComfyUI_FL-HeartMuLa の書き起こしノードは transformers 5 と合わず空文字を返すため）。

```text
<ComfyUI>\.venv\Scripts\python.exe transcribe.py [名前...]   → work/transcripts.json
```

## HeartMuLa を動かすための手当て（2026-10-31、ComfyUI 0.43.2 / transformers 5.20）

ComfyUI_FL-HeartMuLa 2.0.4 はそのままでは動かなかった。行った手当ては次のとおり（詳細は `Doc/promo/song3/README.md`）。

- torchao 0.18.0 → 0.17.0（torchtune 0.6.1 が `torchao.dtypes.nf4tensor` を参照する）
- vector-quantize-pytorch 1.32.0 → 1.26.0（HeartCodec の `initted` バッファの形が `[1]`）
- HeartCodec は `HeartMuLa/HeartCodec-oss-20260123` を `models/heartmula/HeartCodec-oss` へ置く（旧リポジトリは削除済み）
- `heartlib/pipelines/music_generation.py` で読み込み後に RoPE キャッシュを作り直す（元は `.orig`）
- HeartTranscriptor は `models/heartmula/HeartTranscriptor-oss/HeartTranscriptor-oss/` に置く
- 16GB の VRAM では fp16 だと溢れて終わらないため、本体を 4bit（`use_4bit`）で読み込む

## 評価の指標

- **歌詞一致:** HeartTranscriptor（歌詞向けの Whisper）で書き起こした文字列が、渡した歌詞の文字をどれだけ含むかを示す。ひらがな・カタカナの違いは同一視する。
- **製品名:** 書き起こしに「クリエイティブ」が出てきた回数。
- **音の数値:** 長さ、ラウドネス、ピーク、95% ロールオフ（高域の伸び）、8kHz 以上の割合、無音の割合（0.5 秒ごと、-50dB 未満）。
