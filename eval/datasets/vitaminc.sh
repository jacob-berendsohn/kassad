#!/usr/bin/env sh
# Downloads the test split of tals/vitaminc (Hugging Face, CC BY-SA 3.0; 55,197 claim/evidence pairs labeled SUPPORTS,
# REFUTES or NOT ENOUGH INFO) into eval/data/vitaminc/ for `kassad-eval run --dataset vitaminc`, the grounding set.
#
# The canonical file is JSON Lines, which the harness reads as it is, so this fetches test.jsonl itself rather than
# rows-API pages: the repository's current revision is read from its API record and used in the download URL, so the
# file and the recorded revision cannot disagree. The record (license) and a manifest are saved beside it. Needs only
# curl. Raw data is git-ignored (eval/data/) and never committed.
#
# Usage: eval/datasets/vitaminc.sh [target-dir]
set -eu

DATASET="tals/vitaminc"
FILE="test.jsonl"
NAME="vitaminc"
HERE="$(cd "$(dirname "$0")" && pwd)"
TARGET="${1:-$(cd "$HERE/.." && pwd)/data/$NAME}"

fetch() {
  # No --retry-delay: curl then backs off exponentially from 1 s (a 429 counts as transient).
  curl -fsSL --retry 8 --retry-max-time 600 "$1" -o "$2"
}

mkdir -p "$TARGET"

echo "$NAME: fetching repository record" >&2
fetch "https://huggingface.co/api/datasets/$DATASET" "$TARGET/dataset-info.json"
revision="$(grep -o '"sha":"[0-9a-f]*"' "$TARGET/dataset-info.json" | head -n 1 | cut -d '"' -f 4)"
if [ -z "$revision" ]; then
  echo "$NAME: the repository record has no sha, so the download cannot be pinned" >&2
  exit 1
fi

url="https://huggingface.co/datasets/$DATASET/resolve/$revision/$FILE"
echo "$NAME: fetching $FILE at revision $revision" >&2
fetch "$url" "$TARGET/$FILE"
rows="$(grep -c . "$TARGET/$FILE")"

downloaded_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
printf '{\n  "dataset": "%s",\n  "source": "https://huggingface.co/datasets/%s",\n  "revision": "%s",\n  "downloaded_at": "%s",\n  "rows": %s,\n  "splits": ["test"],\n  "files": ["%s"],\n  "url": "%s"\n}\n' \
  "$DATASET" "$DATASET" "$revision" "$downloaded_at" "$rows" "$FILE" "$url" > "$TARGET/manifest.json"

echo "$NAME: $rows rows in $TARGET (revision $revision)" >&2
