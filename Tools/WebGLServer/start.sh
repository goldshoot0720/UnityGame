#!/bin/sh
# MoeGames web version: start a local web server and open the game (macOS / Linux).
# Usage: ./start.sh [port]   (default 8080). Press Ctrl+C to stop.
cd "$(dirname "$0")" || exit 1
PORT="${1:-8080}"
URL="http://localhost:$PORT/"

open_browser() {
  sleep 1
  if command -v open >/dev/null 2>&1; then open "$URL"
  elif command -v xdg-open >/dev/null 2>&1; then xdg-open "$URL"
  else echo "Open $URL in your browser."; fi
}

echo "MoeGames: $URL"
echo "Press Ctrl+C to stop the server."
open_browser &
if command -v python3 >/dev/null 2>&1; then exec python3 -m http.server "$PORT" --bind 127.0.0.1
elif command -v python >/dev/null 2>&1; then exec python -m SimpleHTTPServer "$PORT"
elif command -v php >/dev/null 2>&1; then exec php -S "127.0.0.1:$PORT"
elif command -v ruby >/dev/null 2>&1; then exec ruby -run -e httpd . -p "$PORT" -b 127.0.0.1
elif command -v busybox >/dev/null 2>&1; then exec busybox httpd -f -p "127.0.0.1:$PORT"
else
  echo "No python3 / php / ruby found. On macOS run: xcode-select --install"
  read -r _
fi
