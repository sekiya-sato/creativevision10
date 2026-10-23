# 作品2の台詞（表示する字幕, 読み上げる文, 話者, 話し方）
# 読み上げ文は英字をカタカナにする（VOICEVOX は英字を正しく読まないため）
METAN, METAN_AMA, METAN_SASA, METAN_TSUN = 2, 0, 36, 6
SORA = 16

LINES = {
    'morning': ('今日もいい天気だし、開発作業がんばるぞ！', '今日もいい天気だし、開発作業、がんばるぞ！', METAN_AMA, dict(speed=1.05, pitch=0.02, intonation=1.35)),
    'found': ('MVVMツールキットで public partial が使えるの、今日知った！', 'エムブイブイエム ツールキットで、パブリック パーシャルが使えるの、今日知った！', METAN, dict(speed=1.12, intonation=1.3)),
    'found2': ('早速対応しよ！', 'さっそく、対応しよっ！', METAN_AMA, dict(speed=1.08, pitch=0.03, intonation=1.4)),
    'stress': ('ひぃ、ひぃ……いくら作業しても終わらないよぉー……', 'ひぃ、ひぃ。いくら作業しても、終わらないよぉ。', METAN_SASA, dict(speed=0.95, intonation=1.25, volume=1.6)),
    'idea': ('重要なBaseクラスは、やっと終わったから……', '重要な、ベースクラスは、やっと終わったから。', METAN, dict(speed=1.05, intonation=1.15)),
    'idea2': ('Wpfclient側は、AIにやってもらお！ これで楽ちん！', 'ダブリューピーエフ クライアントがわは、エーアイに、やってもらお！ これで、らくちん！', METAN_AMA, dict(speed=1.1, pitch=0.02, intonation=1.45)),
    'ai': ('了解しました。', 'りょうかいしました。', SORA, dict(speed=1.1, intonation=1.0)),
    'ai2': ('画面も帳票もテストも、まとめて作って検証しますね。', '画面も、帳票も、テストも、まとめて作って、検証しますね。', SORA, dict(speed=1.15, intonation=1.05)),
    'yatta': ('やったー！', 'やったぁー！', METAN_AMA, dict(speed=1.0, pitch=0.04, intonation=1.6)),
    'final1': ('オープンな技術基盤で進化する業務プラットフォーム、', 'オープンな技術基盤で、進化する、業務プラットフォーム。', METAN, dict(speed=1.08, intonation=1.25)),
    'final2': ('Creative Vision 10！', 'クリエイティブ、ビジョン、テン！', METAN_AMA, dict(speed=1.0, pitch=0.03, intonation=1.5)),
    'final_ai': ('2027年1月、リリース予定です。', 'にせんにじゅうななねん、いちがつ、リリース予定です。', SORA, dict(speed=1.2, intonation=1.05)),
    'final3': ('お楽しみに！', 'お楽しみにっ！', METAN_AMA, dict(speed=1.0, pitch=0.04, intonation=1.5)),
}

if __name__ == '__main__':
    import voice
    for k, (disp, text, spk, kw) in LINES.items():
        a, m = voice.say(text, spk, **kw)
        print(f'{k:10s} {len(a) / 48000:5.2f}s  moras={len(m)}')
