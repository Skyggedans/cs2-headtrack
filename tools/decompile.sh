#!/usr/bin/env bash
# Decompile the game's assemblies for reading, into a directory OUTSIDE this
# repository. The output is the game's own code: never commit it, never quote
# it at length.
#
#   tools/decompile.sh OUTDIR [ASSEMBLY...]
#
# Without assemblies it decompiles the ones the camera work needs. The game's
# Managed directory is the one the build uses (see Directory.Build.props), or
# CS2_MANAGED_DIR; ilspycmd can be overridden with ILSPYCMD.
set -euo pipefail

out=${1:?usage: tools/decompile.sh OUTDIR [ASSEMBLY...]}
shift
repo=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
managed=${CS2_MANAGED_DIR:-$(dotnet msbuild "$repo/src/HeadTrack/HeadTrack.csproj" -getProperty:Cs2ManagedDir 2>/dev/null || true)}
if [ -z "$managed" ] || [ ! -f "$managed/Game.dll" ]; then
    echo "the game's Managed folder was not found; set Cs2GameDir (see the README) or CS2_MANAGED_DIR" >&2
    exit 1
fi
ilspy=${ILSPYCMD:-"$HOME/.dotnet/tools/ilspycmd"}
case "$(realpath -m "$out")/" in
    "$repo"/*)
        echo "refusing to decompile into the repository: $out" >&2
        exit 1
        ;;
esac

assemblies=("$@")
if [ ${#assemblies[@]} -eq 0 ]; then
    assemblies=(Game Cinemachine Colossal.IO.AssetDatabase Unity.RenderPipelines.HighDefinition.Runtime)
fi

for name in "${assemblies[@]}"; do
    echo "decompiling $name"
    "$ilspy" -p -o "$out/$name" "$managed/$name.dll"
done
