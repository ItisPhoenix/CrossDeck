#!/usr/bin/env bash
# Warns when an Edit/Write adds a comment block 3+ lines long.
# CrossDeck project rule: comments stay 1-2 lines max.
input=$(cat)
text=$(echo "$input" | jq -r '.tool_input.new_string // .tool_input.content // empty')
[ -z "$text" ] && exit 0

run=$(echo "$text" | awk '
  /^[[:space:]]*(\/\/|#|\*|\/\*)/ { c++; if (c > max) max = c; next }
  { c = 0 }
  END { print max + 0 }
')

if [ "$run" -ge 3 ]; then
  echo "{\"systemMessage\": \"CrossDeck rule: comments must be 1-2 lines max (found a $run-line block) - trim it.\"}"
fi
