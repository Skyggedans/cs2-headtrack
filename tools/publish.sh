#!/usr/bin/env bash
# Publish the mod to Paradox Mods with the ModPublisher the game ships
# (Cities2_Data/Content/Game/.ModdingToolchain/ModPublisher), on Linux.
#
#   tools/publish.sh Publish [--login]      # the first upload; prints the new mod id
#   tools/publish.sh NewVersion [--login]   # new files (needs ModId in the configuration)
#   tools/publish.sh Update [--login]       # the page only: texts, thumbnail, access level
#
# What is sent is src/HeadTrack/Properties/PublishConfiguration.xml and a
# Release build of the mod. The game must be closed.
#
# Without --login the publisher logs in with the game's own Paradox login (log
# in once in the game first). It looks for that login under
# LocalAppData/../LocalLow; on Linux that is ~/.local/LocalLow, so for this one
# run XDG_DATA_HOME points it at the game's user-data folder through a link in
# a temporary folder.
#
# With --login it asks for the Paradox account's email and password instead,
# hands them to the publisher in a file only this run can read, and keeps the
# publisher's session in the temporary folder, apart from the game's.
#
# The publisher targets .NET 6 and runs on a newer runtime.
set -euo pipefail

command=${1:?usage: tools/publish.sh Publish|NewVersion|Update [--login]}
case "$command" in
    Publish | NewVersion | Update) ;;
    *) echo "unknown command: $command" >&2; exit 1 ;;
esac
login=${2:-}
if [ -n "$login" ] && [ "$login" != --login ]; then
    echo "unknown option: $login" >&2
    exit 1
fi
if pgrep -x Cities2.exe >/dev/null; then
    echo "close the game first: the publisher and the game share the Paradox login files" >&2
    exit 1
fi
repo=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
project="$repo/src/HeadTrack/HeadTrack.csproj"
game=$(dotnet msbuild "$project" -getProperty:Cs2GameDir)
userdata=$(dotnet msbuild "$project" -getProperty:Cs2UserDataDir)
publisher="$game/Cities2_Data/Content/Game/.ModdingToolchain/ModPublisher/ModPublisher.dll"
if [ ! -f "$publisher" ]; then
    echo "the game's ModPublisher was not found: $publisher" >&2
    exit 1
fi
if [ -z "$login" ] && [ ! -f "$userdata/.pdxsdk/LastUserId.txt" ]; then
    echo "no Paradox login in $userdata/.pdxsdk: log in in the game, or use --login" >&2
    exit 1
fi

stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT
# Every command takes the content folder, even Update, which sends no files.
dotnet build "$project" -c Release -t:Deploy -p:Cs2ModsDir="$stage/mods" -v:quiet -nologo
mkdir -p "$stage/xdg/share" "$stage/xdg/LocalLow/Colossal Order"
access=()
if [ -n "$login" ]; then
    read -r -p "Paradox account email: " email
    read -r -s -p "Paradox account password: " password
    echo
    (umask 077 && printf '%s\n%s\n' "$email" "$password" > "$stage/login")
    unset password
    mkdir -p "$stage/xdg/LocalLow/Colossal Order/Cities Skylines II/.pdxsdk"
    access=(-l "$stage/login" --noAutoLogin)
else
    ln -s "$userdata" "$stage/xdg/LocalLow/Colossal Order/Cities Skylines II"
fi

cd "$repo/src/HeadTrack"
XDG_DATA_HOME="$stage/xdg/share" DOTNET_ROLL_FORWARD=Major \
    dotnet "$publisher" "$command" Properties/PublishConfiguration.xml -c "$stage/mods/HeadTrack" "${access[@]}" -v
