#!/bin/bash

# Launch VS Code extension in pure host environment
# Run this from the HOST machine, not from inside devcontainer

EXT_PATH="/home/stachu/code/dark/vscode-extension"
USER_DATA_DIR="/tmp/vscode-extension-dev-$(date +%s)"

echo "Launching extension development host..."
echo "Extension path: $EXT_PATH"
echo "User data dir: $USER_DATA_DIR"

# Kill any existing code processes that might interfere
pkill -f "extensionDevelopmentPath" || true

# Launch VS Code with extension
code \
  --extensionDevelopmentPath="$EXT_PATH" \
  --disable-extensions \
  --user-data-dir="$USER_DATA_DIR" \
  --no-sandbox \
  --disable-workspace-trust \
  --verbose

echo "Extension development host launched!"