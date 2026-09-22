#!/usr/bin/env sh
# Downloads lmsys/toxic-chat (Hugging Face, CC BY-NC 4.0), config `toxicchat0124` (the maintainers' current version):
# 5,082 train and 5,083 test user prompts with a toxicity label (1 = toxic) and a jailbreaking flag, into
# eval/data/toxicchat-0124/ for `kassad-eval run --dataset toxicchat`. The harness evaluates user_input against
# toxicity; model_output and openai_moderation come along in the pages but are not used.
#
# Same mechanics as deepset-prompt-injections.sh: the canonical files are CSV, so this fetches the rows as JSON pages
# (100 rows each, 102 pages) from the datasets-server rows API, plus the repository's API record (revision, license) and
# a manifest of what was fetched. Needs only curl. Raw data is git-ignored (eval/data/) and never committed, which the
# non-commercial license also requires of anything but derived numbers; the results file records the revision so a run
# can be traced back to the exact rows.
#
# Usage: eval/datasets/toxicchat.sh [target-dir]
set -eu

DATASET="lmsys/toxic-chat"
DATASET_ENCODED="lmsys%2Ftoxic-chat"
CONFIG="toxicchat0124"
SPLITS="train test"
PAGE_SIZE=100
# Seconds between pages. The datasets-server answered 429 (no Retry-After) after about 55 pages fetched back to back
# on 2026-09-22; two seconds apart, the 102 pages stay under its limit and take about four minutes.
PAUSE=2
NAME="toxicchat-0124"
HERE="$(cd "$(dirname "$0")" && pwd)"
TARGET="${1:-$(cd "$HERE/.." && pwd)/data/$NAME}"

fetch() {
  # No --retry-delay: curl then backs off exponentially from 1 s (429 counts as transient), up to ten minutes in all.
  curl -fsSL --retry 8 --retry-max-time 600 "$1" -o "$2"
}

mkdir -p "$TARGET"
for split in $SPLITS; do
  rm -f "$TARGET/$split"-*.json
done

echo "$NAME: fetching repository record" >&2
fetch "https://huggingface.co/api/datasets/$DATASET" "$TARGET/dataset-info.json"
revision="$(grep -o '"sha":"[0-9a-f]*"' "$TARGET/dataset-info.json" | head -n 1 | cut -d '"' -f 4)"

rows=0
for split in $SPLITS; do
  offset=0
  while :; do
    file="$TARGET/$split-$(printf '%05d' "$offset").json"
    fetch "https://datasets-server.huggingface.co/rows?dataset=$DATASET_ENCODED&config=$CONFIG&split=$split&offset=$offset&length=$PAGE_SIZE" "$file"
    total="$(grep -o '"num_rows_total":[0-9]*' "$file" | head -n 1 | tr -dc '0-9')"
    if [ -z "$total" ]; then
      echo "$NAME: $file has no num_rows_total; the rows API answered something unexpected" >&2
      exit 1
    fi
    echo "$NAME: $split rows from $offset of $total" >&2
    offset=$((offset + PAGE_SIZE))
    if [ "$offset" -ge "$total" ]; then
      rows=$((rows + total))
      break
    fi
    sleep "$PAUSE"
  done
done

downloaded_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
splits_json="$(printf '"%s", ' $SPLITS | sed 's/, $//')"
printf '{\n  "dataset": "%s",\n  "config": "%s",\n  "source": "https://huggingface.co/datasets/%s",\n  "revision": "%s",\n  "downloaded_at": "%s",\n  "rows": %s,\n  "splits": [%s],\n  "page_size": %s,\n  "api": "https://datasets-server.huggingface.co/rows"\n}\n' \
  "$DATASET" "$CONFIG" "$DATASET" "$revision" "$downloaded_at" "$rows" "$splits_json" "$PAGE_SIZE" > "$TARGET/manifest.json"

echo "$NAME: $rows rows in $TARGET (revision ${revision:-unknown})" >&2
