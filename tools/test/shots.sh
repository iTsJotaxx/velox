#!/bin/sh
# Compila Velox en modo UITEST (sin elevación) y guarda capturas: tools/test/shots.sh <outdir> page1 page2+accion ...
# Acciones: expand:cat, risk:Id, pending:Id, search:texto, tab:N, todo. Tamaño: ejecutar el exe con --size=1060x680.
set -e
cd "$(dirname "$0")/../.."
OUT="$1"; shift
# Manifiesto sin elevación (tools/test/*.manifest no se versiona)
sed "s/requireAdministrator/asInvoker/" src/Velox/app.manifest > tools/test/uitest.manifest
"/c/Program Files/dotnet/dotnet.exe" build src/Velox/Velox.csproj -c Debug -nologo -v q \
  -p:DefineConstants=UITEST -p:ApplicationManifest="$(pwd -W)/tools/test/uitest.manifest" \
  -p:OutputPath="$(pwd -W)/tools/test/bin/" 2>&1 | grep -E "error|Warn|warn" | grep -v "warning MSB" | head -20 || true
mkdir -p "$OUT"
for p in "$@"; do
  page="${p%%+*}"; extra=""; [ "$p" != "$page" ] && extra="--action=${p#*+}"
  name=$(echo "$p" | tr ':+' '__')
  ./tools/test/bin/Velox.exe --page="$page" --shot="$(cd "$OUT" && pwd -W)/$name.png" --shot-delay=7 $extra
  echo "$name done"
done
