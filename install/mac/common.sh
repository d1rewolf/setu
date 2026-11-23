# Shared helpers for install.sh, update.sh and uninstall.sh. Sourced, never run on its own.
# Every function here assumes `set -euo pipefail` from the caller.

LABEL=com.greatworx.setu
APP="$HOME/Applications/Setu.app"
EXE="$APP/Contents/MacOS/setu"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
DATA="$HOME/Library/Application Support/setu"
TOKEN_FILE="$DATA/token"
LOG_FILE="$DATA/setu.log"
LAUNCHD_LOG="$DATA/launchd.log"
KEYCHAIN="$HOME/Library/Keychains/setu-signing.keychain-db"
KEYCHAIN_PW_FILE="$DATA/signing-keychain-password"
LEAF_FILE="$DATA/signing-leaf-sha1"
CERT_CN="Setu Code Signing"
DOMAIN="gui/$(id -u)"
DEFAULT_PORT=47631

SESSION_HINT="Is John logged on at the Mac's desktop? Setu only runs in the logged-on user's session (after a restart, someone has to log in once because FileVault is on)."

say()  { print -r -- "$*"; }
fail() { print -r -- "Error: $*" >&2; exit 1; }

# Parses --binary <path> and --version <x> into BINARY and VERSION.
parse_binary_args() {
  BINARY="" VERSION="0.1.0"
  while (( $# )); do
    case $1 in
      --binary)  BINARY=${2:-}; shift 2 ;;
      --version) VERSION=${2:-}; shift 2 ;;
      *) fail "unknown option '$1'. Usage: $ZSH_ARGZERO --binary <path to setu> [--version X.Y.Z]" ;;
    esac
  done
  [[ -n $BINARY ]] || fail "--binary <path to setu> is required."
  [[ -f $BINARY ]] || fail "no file at $BINARY."
  [[ $VERSION =~ '^[0-9]+(\.[0-9]+){0,2}$' ]] || fail "--version must look like 1.2.3, not '$VERSION'."
  BINARY=${BINARY:A}
}

require_mac() {
  [[ $(uname -s) == Darwin && $(uname -m) == arm64 ]] || fail "this script is for macOS on Apple silicon."
  local major=${$(sw_vers -productVersion)%%.*}
  (( major >= 15 )) || fail "Setu needs macOS 15 or later; this Mac has $(sw_vers -productVersion)."
}

require_gui_session() {
  launchctl print "$DOMAIN" >/dev/null 2>&1 || fail "there is no desktop session for $(id -un). $SESSION_HINT"
}

port_from_config() {
  local cfg="$DATA/config.json" port
  if [[ -f $cfg ]] && port=$(plutil -extract port raw -o - "$cfg" 2>/dev/null); then
    print -r -- "$port"
  else
    print -r -- "$DEFAULT_PORT"
  fi
}

# Creates the signing keychain and the self-signed certificate if they don't exist yet,
# and records the certificate's SHA-1. The key never leaves this Mac.
ensure_signing_identity() {
  mkdir -p "$DATA"; chmod 700 "$DATA"
  if [[ ! -f $KEYCHAIN ]]; then
    say "Creating Setu's signing certificate (once per Mac)..."
    (umask 077; head -c 32 /dev/urandom | base64 | tr -d '/+=' > "$KEYCHAIN_PW_FILE")
    local pw=$(<"$KEYCHAIN_PW_FILE")
    security create-keychain -p "$pw" "$KEYCHAIN"
    security set-keychain-settings "$KEYCHAIN"   # no auto-lock
    security unlock-keychain -p "$pw" "$KEYCHAIN"
    add_keychain_to_search_list
    local tmp=$(mktemp -d)
    {
      local p12pw=$(head -c 16 /dev/urandom | base64 | tr -d '/+=')
      /usr/bin/openssl req -x509 -newkey rsa:2048 -nodes -days 7300 \
        -keyout "$tmp/key.pem" -out "$tmp/cert.pem" -subj "/CN=$CERT_CN" \
        -addext "extendedKeyUsage=critical,codeSigning" \
        -addext "keyUsage=critical,digitalSignature" \
        -addext "basicConstraints=critical,CA:false" 2>/dev/null
      /usr/bin/openssl pkcs12 -export -inkey "$tmp/key.pem" -in "$tmp/cert.pem" \
        -out "$tmp/id.p12" -passout "pass:$p12pw"
      security import "$tmp/id.p12" -k "$KEYCHAIN" -P "$p12pw" -T /usr/bin/codesign >/dev/null
      security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$pw" "$KEYCHAIN" >/dev/null
    } always { rm -rf "$tmp" }
  fi
  [[ -f $KEYCHAIN_PW_FILE ]] || fail "the signing keychain exists but its password file $KEYCHAIN_PW_FILE is missing."
  chmod 600 "$KEYCHAIN_PW_FILE"
  add_keychain_to_search_list
  unlock_signing
  local sha1=$(signing_sha1)
  [[ -n $sha1 ]] || fail "no '$CERT_CN' certificate in $KEYCHAIN."
  if [[ ! -f $LEAF_FILE ]]; then
    (umask 077; print -r -- "$sha1" > "$LEAF_FILE")
  elif [[ $(<"$LEAF_FILE") != "$sha1" ]]; then
    fail "the signing certificate changed (expected $(<"$LEAF_FILE"), found $sha1). Signing with it would silently drop Setu's Screen Recording and Accessibility permissions, so nothing was changed."
  fi
}

unlock_signing() { security unlock-keychain -p "$(<"$KEYCHAIN_PW_FILE")" "$KEYCHAIN"; }

signing_sha1() { security find-certificate -c "$CERT_CN" -Z "$KEYCHAIN" 2>/dev/null | awk '/SHA-1/{print $NF}'; }

keychain_search_list() { security list-keychains -d user | sed -e 's/^ *"//' -e 's/"$//'; }

add_keychain_to_search_list() {
  local -a list=("${(@f)$(keychain_search_list)}")
  (( ${list[(Ie)$KEYCHAIN]} )) || security list-keychains -d user -s "${list[@]}" "$KEYCHAIN"
}

remove_keychain_from_search_list() {
  local -a list=("${(@f)$(keychain_search_list)}")
  list=("${(@)list:#$KEYCHAIN}")
  security list-keychains -d user -s "${list[@]}"
}

# build_bundle <binary> <dest.app> <version>
build_bundle() {
  local binary=$1 dest=$2 version=$3
  rm -rf "$dest"
  mkdir -p "$dest/Contents/MacOS"
  cp "$binary" "$dest/Contents/MacOS/setu"
  chmod 755 "$dest/Contents/MacOS/setu"
  cat > "$dest/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key><string>$LABEL</string>
  <key>CFBundleName</key><string>Setu</string>
  <key>CFBundleDisplayName</key><string>Setu</string>
  <key>CFBundleExecutable</key><string>setu</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSUIElement</key><true/>
  <key>LSMinimumSystemVersion</key><string>15.0</string>
  <key>NSLocalNetworkUsageDescription</key><string>Setu runs commands for agents that reach machines on your local network.</string>
  <key>NSAppleEventsUsageDescription</key><string>Setu runs scripts for agents that control other apps.</string>
</dict>
</plist>
EOF
}

# Signs the bundle by certificate hash and checks that its designated requirement names
# Setu's identifier and the saved certificate hash. A different requirement would drop the permissions.
sign_bundle() {
  local app=$1 sha1=$(<"$LEAF_FILE")
  unlock_signing
  codesign --force --sign "$sha1" --keychain "$KEYCHAIN" --identifier "$LABEL" --timestamp=none "$app" \
    || fail "codesign could not sign $app."
  local out
  if ! out=$(codesign --verify --strict "$app" 2>&1); then
    [[ $out == *CSSMERR_TP_NOT_TRUSTED* ]] || fail "the signature on $app does not verify: $out"
  fi
  local dr=$(codesign -d -r- "$app" 2>&1)
  [[ $dr == *"identifier \"$LABEL\""* && ${dr:l} == *${sha1:l}* ]] \
    || fail "the signed app's designated requirement does not name $LABEL and certificate $sha1, so the permissions would not carry over. It was: $dr"
}

write_plist() {
  mkdir -p "${PLIST:h}"
  cat > "$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>$LABEL</string>
  <key>Program</key><string>$EXE</string>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
  <key>ProcessType</key><string>Interactive</string>
  <key>LimitLoadToSessionType</key><string>Aqua</string>
  <key>StandardOutPath</key><string>$LAUNCHD_LOG</string>
  <key>StandardErrorPath</key><string>$LAUNCHD_LOG</string>
</dict>
</plist>
EOF
  plutil -lint "$PLIST" >/dev/null || fail "the LaunchAgent file $PLIST is not valid."
}

is_loaded() { launchctl print "$DOMAIN/$LABEL" >/dev/null 2>&1; }

bootstrap() {
  launchctl bootstrap "$DOMAIN" "$PLIST" || fail "launchctl could not start Setu. $SESSION_HINT"
}

# Stops Setu and waits until launchd has let go of it. Not being loaded is fine.
bootout() {
  is_loaded || return 0
  launchctl bootout "$DOMAIN/$LABEL" 2>/dev/null || true
  local i
  for i in {1..50}; do is_loaded || return 0; sleep 0.2; done
  fail "Setu did not stop within 10 seconds."
}

# wait_listening <port> <seconds>: waits for the token file and the port; on timeout prints the log tail.
wait_listening() {
  local port=$1 secs=$2 i
  say "Waiting for Setu to listen on 127.0.0.1:$port..."
  for (( i = 0; i < secs * 2; i++ )); do
    [[ -f $TOKEN_FILE ]] && /usr/bin/nc -z 127.0.0.1 "$port" 2>/dev/null && return 0
    sleep 0.5
  done
  say "Setu is not listening on port $port after $secs seconds. $SESSION_HINT"
  [[ -f $LOG_FILE ]] && { say "Last lines of $LOG_FILE:"; tail -n 15 "$LOG_FILE"; }
  [[ -s $LAUNCHD_LOG ]] && { say "Last lines of $LAUNCHD_LOG:"; tail -n 15 "$LAUNCHD_LOG"; }
  fail "Setu did not come up. See the lines above."
}

# Opens the two System Settings panes on the Mac's screen, for whoever is at the Mac.
open_permission_panes() {
  open "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture" || true
  open "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility" || true
}

# True if Setu's startup check (logged after <line count>) said a permission is missing.
permissions_missing_since() {
  local from=$1 i
  for i in {1..20}; do   # the check runs just after Setu starts listening
    tail -n +$(( from + 1 )) "$LOG_FILE" 2>/dev/null | grep -q "accessibility: " && break
    sleep 0.25
  done
  tail -n +$(( from + 1 )) "$LOG_FILE" 2>/dev/null | grep -qE "(screen recording|accessibility): not granted"
}

log_lines() { [[ -f $LOG_FILE ]] && wc -l < "$LOG_FILE" | tr -d ' ' || print 0; }
