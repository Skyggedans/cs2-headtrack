#!/usr/bin/env bash
# Decompile the game's assemblies for reading, into a directory OUTSIDE this
# repository. The output is the game's own code: never commit it, never quote
# it at length.
#
#   tools/decompile.sh OUTDIR [ASSEMBLY...]
#
# Without assemblies it decompiles the ones the camera work needs. The game's
# Managed directory and ilspycmd can be overridden with CS2_MANAGED_DIR and
# ILSPYCMD.
set -euo pipefail

out=${1:?usage: tools/decompile.sh OUTDIR [ASSEMBLY...]}
shift
managed=${CS2_MANAGED_DIR:-"/mnt/D/Games/Cities Skylines II/Cities2_Data/Managed"}
ilspy=${ILSPYCMD:-"$HOME/.dotnet/tools/ilspycmd"}

repo=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
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
