#!/usr/bin/env sh
# Downloads deepset/prompt-injections (Hugging Face, Apache-2.0; 546 train + 116 test prompts labeled 1 = injection,
# 0 = legitimate) into eval/data/deepset-prompt-injections/ for `kassad-eval run --dataset deepset`.
#
# The repository's canonical files are Parquet, which the harness cannot read without adding a package, so this
# fetches the same rows as JSON pages (100 rows each) from the datasets-server rows API, plus the repository's API
# record (revision, license) and a manifest of what was fetched. Needs only curl. Raw data is git-ignored (eval/data/)
# and never committed; the results file records the revision so a run can be traced back to the exact rows.
#
# Usage: eval/datasets/deepset-prompt-injections.sh [target-dir]
set -eu

DATASET="deepset/prompt-injections"
DATASET_ENCODED="deepset%2Fprompt-injections"
PAGE_SIZE=100
HERE="$(cd "$(dirname "$0")" && pwd)"
TARGET="${1:-$(cd "$HERE/.." && pwd)/data/deepset-prompt-injections}"

fetch() {
  curl -fsSL --retry 3 --retry-delay 2 "$1" -o "$2"
}

mkdir -p "$TARGET"
rm -f "$TARGET"/train-*.json "$TARGET"/test-*.json

echo "deepset-prompt-injections: fetching repository record" >&2
fetch "https://huggingface.co/api/datasets/$DATASET" "$TARGET/dataset-info.json"
revision="$(grep -o '"sha":"[0-9a-f]*"' "$TARGET/dataset-info.json" | head -n 1 | cut -d '"' -f 4)"

rows=0
for split in train test; do
  offset=0
  while :; do
    file="$TARGET/$split-$(printf '%05d' "$offset").json"
    fetch "https://datasets-server.huggingface.co/rows?dataset=$DATASET_ENCODED&config=default&split=$split&offset=$offset&length=$PAGE_SIZE" "$file"
    total="$(grep -o '"num_rows_total":[0-9]*' "$file" | head -n 1 | tr -dc '0-9')"
    if [ -z "$total" ]; then
      echo "deepset-prompt-injections: $file has no num_rows_total; the rows API answered something unexpected" >&2
      exit 1
    fi
    echo "deepset-prompt-injections: $split rows from $offset of $total" >&2
    offset=$((offset + PAGE_SIZE))
    if [ "$offset" -ge "$total" ]; then
      rows=$((rows + total))
      break
    fi
  done
done

downloaded_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
printf '{\n  "dataset": "%s",\n  "source": "https://huggingface.co/datasets/%s",\n  "revision": "%s",\n  "downloaded_at": "%s",\n  "rows": %s,\n  "splits": ["train", "test"],\n  "page_size": %s,\n  "api": "https://datasets-server.huggingface.co/rows"\n}\n' \
  "$DATASET" "$DATASET" "$revision" "$downloaded_at" "$rows" "$PAGE_SIZE" > "$TARGET/manifest.json"

echo "deepset-prompt-injections: $rows rows in $TARGET (revision ${revision:-unknown})" >&2
