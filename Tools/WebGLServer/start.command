#!/bin/sh
# macOS: double-click in Finder to start the MoeGames web version (opens Terminal).
exec "$(dirname "$0")/start.sh" "$@"
