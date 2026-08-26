#!/usr/bin/env bash
# Pubblica Ide.App come cartella Linux self-contained (nessun .NET richiesto sulla
# macchina di destinazione, solo le dipendenze di sistema di WebKitGTK - vedi README.md).
#
# NOTA: niente -p:PublishSingleFile. Il ComponentPluginLoader (modulo 14) cerca
# VbControls.dll/VbControls.Abstractions.dll come file fisici accanto all'eseguibile per
# referenziarli nella compilazione Roslyn dei componenti-plugin; in modalita' single-file
# quegli assembly verrebbero incorporati nell'eseguibile e non piu' presenti su disco,
# facendo fallire silenziosamente il caricamento dei plugin. Una cartella con i file
# sciolti (identica nella forma a bin/Debug/net8.0/ gia' usata in sviluppo) evita il problema.
set -euo pipefail
cd "$(dirname "$0")/.."

RID="linux-x64"
OUT="dist/$RID"

dotnet publish src/Ide.App/Ide.App.csproj \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -o "$OUT"

echo ""
echo "Pubblicato in $OUT/"
echo "Per avviare: $OUT/Ide.App"
echo "Prerequisiti di sistema sulla macchina di destinazione: WebKitGTK (vedi README.md)."
