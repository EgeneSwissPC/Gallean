#!/bin/bash
# review.sh <render dir> <prefix>: composite shadow + unit per frame, one numbered sheet per side/anim.
R=$1; P=$2
FF=$(find ~/.hermes/tools/ffmpeg-9.0.1-linux-x64 -name ffmpeg -type f | head -1)
for s in attacker defender; do
  for a in idle attack hit; do
    mkdir -p "$R/comp/$s/$a"
    for f in "$R/$s/$a"/*.png; do
      n=$(basename "$f")
      sh="$R/$s/${a}_shadow/$n"
      if [ -f "$sh" ]; then
        $FF -y -loglevel error -i "$sh" -i "$f" -filter_complex "[0][1]overlay" "$R/comp/$s/$a/$n" &
      else
        cp "$f" "$R/comp/$s/$a/$n"
      fi
    done
    wait
    bash "$(dirname "$0")/sheet.sh" "$R/comp/$s/$a" "${P}_${s}_$a.png" 420:500:130:60
  done
done
