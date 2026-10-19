#!/usr/bin/env bash
# Installs the media-key bridge as a systemd user service (starts at login, runs idle until the game plugin is up).
set -e
here="$(cd "$(dirname "$0")" && pwd)"
unit="$HOME/.config/systemd/user/sphere-player-mpris.service"
mkdir -p "$(dirname "$unit")"
cat > "$unit" <<UNIT
[Unit]
Description=Sphere Player media-key bridge (MPRIS)

[Service]
ExecStart=$here/sphere-player-mpris
Restart=on-failure
RestartSec=5

[Install]
WantedBy=default.target
UNIT
systemctl --user daemon-reload
systemctl --user enable --now sphere-player-mpris.service
systemctl --user --no-pager status sphere-player-mpris.service | head -5
