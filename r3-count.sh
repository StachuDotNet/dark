#!/bin/bash
# r3-count.sh <label>: sessions (of the label's transcripts) where each round-3 situation showed
B=/home/stachu/code/dark/bench-loop.bench; L=$1
n=$(ls -d $B/results/$L/*/ 2>/dev/null | wc -l)
c() { printf "%-34s %s/%s\n" "$1" "$(grep -l -E -- "$2" $B/results/$L/*/agent-0.jsonl 2>/dev/null | wc -l)" "$n"; }
c "val type unavailable"        'is not available to the checker'
c "allow after name (note)"     'came after .* so the program got it'
c "instance denial"             'permission denied by instance policy'
c "one-run offered"             'For one run, leaving the policy'
c "1-decl save listed leftovers"  "Defined 1 declaration in /[A-Za-z.]*.{0,40}still in"
c "op hint (a + b etc)"         'Did you mean `a [-+*/] b`'
c "checked hint"                'or `Stdlib\.[A-Za-z0-9]+\.checked'
c "module delete refused"       'names neither an item kind nor a live item'
c "module deleted"              'Will delete [0-9]+ items? in'
c "empty path as policy"        'is protected host policy state'
c "random lambda id"            'Lambda in Source \([0-9]+\)'
c "eval --allow used"           'eval --allow'
c "eval flag parse error"       "allow\(a, b, c\)"
