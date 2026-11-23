#!/bin/zsh
# Replaces the installed Setu with a new build and restarts it. The token, config and
# permissions carry over, because the new build is signed with the same certificate.
# Usage: update.sh --binary <path to setu> [--version X.Y.Z]
set -euo pipefail
source "${0:A:h}/common.sh"

parse_binary_args "$@"
require_mac
require_gui_session
[[ -d $APP && -f $PLIST ]] || fail "Setu is not installed here. Run install.sh first."
[[ -f $LEAF_FILE ]] || fail "$LEAF_FILE is missing, so the update cannot prove it keeps the permissions. Run install.sh instead."
ensure_signing_identity

staged="$APP.new"
build_bundle "$BINARY" "$staged" "$VERSION"
sign_bundle "$staged"

bootout
rm -rf "$APP"
mv "$staged" "$APP"
bootstrap
port=$(port_from_config)
wait_listening "$port" 30
say "Setu is updated to version $VERSION and running on port $port."
