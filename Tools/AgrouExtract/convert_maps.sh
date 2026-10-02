#!/bin/bash
# Convertit les maps USD d'AgrouCache en FBX + JSON : ./convert_maps.sh [map...]
cd "$(dirname "$0")"
C="$(cygpath -m ../../AgrouCache)"
B="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
MAPS="$*"; [ -z "$MAPS" ] && MAPS="PlaceDuVillage Foret Cimetiere Valley Feerique Grotte MapIlePirate MapNoel AsianValley"
for x in $MAPS; do
  "$B" -b --factory-startup -P "$(cygpath -m map2fbx.py)" -- "$C/Agrou/Content/Maps/$x.usda" "$C/$x.fbx" "$C/$x.json" 2>&1 | grep -E "FOLIAGE|TRIS|rror:" | sed "s/^/$x /"
done
