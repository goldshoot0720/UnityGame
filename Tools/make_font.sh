#!/bin/bash
# Rebuilds Assets/Resources/Fonts/MoeFont.ttf: Noto Sans TC Bold (SIL OFL) subset to every character
# used in Assets/Scripts. Run after adding new Chinese text. Needs: python3 -m pip install fonttools
set -e
cd "$(dirname "$0")/.."
TMP=$(mktemp -d)
curl -sfL -o "$TMP/var.ttf" "https://github.com/google/fonts/raw/main/ofl/notosanstc/NotoSansTC%5Bwght%5D.ttf"
python3 - "$TMP/chars.txt" <<'PY'
import glob, sys
chars = set()
for f in glob.glob('Assets/Scripts/**/*.cs', recursive=True):
    chars |= set(open(f, encoding='utf-8').read())
chars |= set(chr(c) for c in range(0x20, 0x7f)) | set('，。、！？：；「」『』（）《》〈〉…—～・＋－×÷％＄／')
open(sys.argv[1], 'w', encoding='utf-8').write(''.join(sorted(c for c in chars if c.isprintable())))
PY
fonttools varLib.instancer "$TMP/var.ttf" wght=700 -o "$TMP/bold.ttf" -q
pyftsubset "$TMP/bold.ttf" --text-file="$TMP/chars.txt" --output-file=Assets/Resources/Fonts/MoeFont.ttf --layout-features='*' --no-hinting
echo "MoeFont.ttf rebuilt ($(wc -c < Assets/Resources/Fonts/MoeFont.ttf) bytes)"
