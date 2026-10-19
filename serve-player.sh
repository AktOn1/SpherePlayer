#!/usr/bin/env bash
# Serves the player page at http://localhost:8794/player.html
cd "$(dirname "$(readlink -f "$0")")/player" && exec python3 -m http.server 8794 --bind 127.0.0.1
