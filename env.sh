# Shared environment for the Soulstone Archipelago toolchain.
# The sandbox only permits writes inside the project, so all dotnet/NuGet
# state is redirected into .tools/.
AP_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export AP_ROOT
export TEMP="$AP_ROOT/.tools/tmp"
export TMP="$AP_ROOT/.tools/tmp"
export APPDATA="$AP_ROOT/.tools/appdata/roaming"
export LOCALAPPDATA="$AP_ROOT/.tools/appdata/local"
export DOTNET_CLI_HOME="$AP_ROOT/.tools/home"
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="$AP_ROOT/.tools/nuget"
export DOTNET="$AP_ROOT/.tools/dotnet/dotnet.exe"
export GAME_DIR="D:/SteamLibrary/steamapps/common/Soulstone Survivors"
export INTEROP_DIR="$GAME_DIR/BepInEx/interop"
mkdir -p "$TEMP" "$APPDATA" "$LOCALAPPDATA" "$DOTNET_CLI_HOME" "$NUGET_PACKAGES"
