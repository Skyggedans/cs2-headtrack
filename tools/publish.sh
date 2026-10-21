#!/usr/bin/env bash
# Publish the mod to Paradox Mods with the ModPublisher the game ships
# (Cities2_Data/Content/Game/.ModdingToolchain/ModPublisher), on Linux.
#
#   tools/publish.sh Publish      # the first upload; prints the new mod id
#   tools/publish.sh NewVersion   # new files (needs ModId in the configuration)
#   tools/publish.sh Update       # the page only: texts, thumbnail, access level
#
# What is sent is src/HeadTrack/Properties/PublishConfiguration.xml and a
# Release build of the mod. The publisher logs in with the game's own Paradox
# login, so log in once in the game first. It looks for that login under
# LocalAppData/../LocalLow; on Linux that is ~/.local/LocalLow, so for this one
# run XDG_DATA_HOME points it at the game's user-data folder through a link in
# a temporary folder. The publisher targets .NET 6 and runs on a newer runtime.
set -euo pipefail

command=${1:?usage: tools/publish.sh Publish|NewVersion|Update}
case "$command" in
    Publish | NewVersion | Update) ;;
    *) echo "unknown command: $command" >&2; exit 1 ;;
esac
repo=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
project="$repo/src/HeadTrack/HeadTrack.csproj"
game=$(dotnet msbuild "$project" -getProperty:Cs2GameDir)
userdata=$(dotnet msbuild "$project" -getProperty:Cs2UserDataDir)
publisher="$game/Cities2_Data/Content/Game/.ModdingToolchain/ModPublisher/ModPublisher.dll"
if [ ! -f "$publisher" ]; then
    echo "the game's ModPublisher was not found: $publisher" >&2
    exit 1
fi
if [ ! -f "$userdata/.pdxsdk/LastUserId.txt" ]; then
    echo "no Paradox login in $userdata/.pdxsdk: start the game and log in" >&2
    exit 1
fi

stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT
# Every command takes the content folder, even Update, which sends no files.
dotnet build "$project" -c Release -t:Deploy -p:Cs2ModsDir="$stage/mods" -v:quiet -nologo
mkdir -p "$stage/xdg/share" "$stage/xdg/LocalLow/Colossal Order"
ln -s "$userdata" "$stage/xdg/LocalLow/Colossal Order/Cities Skylines II"

cd "$repo/src/HeadTrack"
XDG_DATA_HOME="$stage/xdg/share" DOTNET_ROLL_FORWARD=Major \
    dotnet "$publisher" "$command" Properties/PublishConfiguration.xml -c "$stage/mods/HeadTrack" -v
