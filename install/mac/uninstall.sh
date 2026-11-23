#!/bin/zsh
# Stops and removes Setu. Keeps the token, config, log and signing certificate unless
# --remove-data is given. Keeping the certificate is what lets a reinstall keep the permissions.
# Usage: uninstall.sh [--remove-data]
set -euo pipefail
source "${0:A:h}/common.sh"

remove_data=false
(( $# <= 1 )) || fail "too many arguments. Usage: uninstall.sh [--remove-data]"
case ${1:-} in
  "") ;;
  --remove-data) remove_data=true ;;
  *) fail "unknown option '$1'. Usage: uninstall.sh [--remove-data]" ;;
esac

bootout
rm -f "$PLIST"
rm -rf "$APP" "$APP.new"
say "Stopped Setu and removed $APP and its LaunchAgent."

if ! $remove_data; then
  say "Kept $DATA (token, config, log) and the signing certificate. Pass --remove-data to delete them too."
  exit 0
fi

remove_keychain_from_search_list
if [[ -f $KEYCHAIN ]]; then
  security delete-keychain "$KEYCHAIN"
  say "Deleted the signing keychain $KEYCHAIN."
fi
rm -rf "$DATA"
say "Deleted $DATA."

reset_ok=true
tccutil reset ScreenCapture "$LABEL" >/dev/null 2>&1 || reset_ok=false
tccutil reset Accessibility "$LABEL" >/dev/null 2>&1 || reset_ok=false
if $reset_ok; then
  say "Reset Setu's Screen Recording and Accessibility permissions."
else
  say "macOS did not let this script reset Setu's permissions. At the Mac, remove Setu from"
  say "System Settings › Privacy & Security › Screen & System Audio Recording and › Accessibility."
fi
