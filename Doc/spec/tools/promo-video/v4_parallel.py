# 動画4 平行法版: v4 の左右を入れ替え（左の枠に左目用の画像）、ガイド文を平行法にする → v4_parallel.mp4
import os
os.environ['V4_STEREO'] = 'parallel'
from v4 import *  # noqa: E402,F401,F403
