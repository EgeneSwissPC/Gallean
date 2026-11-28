#!/bin/bash
# sheet.sh <frames dir> <out.png> [crop w:h:x:y]  - every frame on grey, numbered, 6 per row, full size.
D=$1; OUT=$2; CROP=${3:-360:440:120:70}
FF=$(find ~/.hermes/tools/ffmpeg-9.0.1-linux-x64 -name ffmpeg -type f | head -1)
T=$(mktemp -d)
n=0
for f in "$D"/*.png; do
  i=$(basename "$f" .png)
  $FF -y -loglevel error -f lavfi -i color=c=0x8a8a8a:s=600x520 -i "$f" \
    -filter_complex "[0][1]overlay,crop=$CROP,drawtext=text='$i':x=6:y=6:fontsize=26:fontcolor=yellow" -frames:v 1 "$T/$(printf %03d $n).png"
  n=$((n+1))
done
cols=6; rows=$(( (n + cols - 1) / cols ))
$FF -y -loglevel error -framerate 1 -i "$T/%03d.png" -vf "tile=${cols}x${rows}:color=0x8a8a8a" -frames:v 1 "$OUT"
rm -rf "$T"
echo "$OUT ($n frames)"
