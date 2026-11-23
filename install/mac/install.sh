#!/bin/zsh
# Installs Setu for the logged-on user and starts it at login from now on.
# Usage: install.sh --binary <path to setu> [--version X.Y.Z]
# Safe to run again: it replaces an earlier install and keeps the token, config and permissions.
set -euo pipefail
source "${0:A:h}/common.sh"

parse_binary_args "$@"
require_mac
require_gui_session
ensure_signing_identity

staged="$APP.new"
build_bundle "$BINARY" "$staged" "$VERSION"
sign_bundle "$staged"

bootout
mkdir -p "${APP:h}"
rm -rf "$APP"
mv "$staged" "$APP"
say "Installed $APP (version $VERSION), signed with certificate $(<"$LEAF_FILE")."

write_plist
from=$(log_lines)
bootstrap
port=$(port_from_config)
wait_listening "$port" 30

say ""
say "Setu is installed and running on port $port."
say "Read the token from Linux:  ssh $(id -un)@<this mac> 'cat ~/Library/Application\\ Support/setu/token'"
if permissions_missing_since "$from"; then
  open_permission_panes
  say "Setu is missing a permission, so System Settings is now open on the Mac."
fi
say "If Setu is new on this Mac, at the Mac turn on Setu in System Settings › Privacy & Security ›"
say "Screen & System Audio Recording, Accessibility and Local Network, then run:"
say "  launchctl kickstart -k gui/\$(id -u)/$LABEL"
