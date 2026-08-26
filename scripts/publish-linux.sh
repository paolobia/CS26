#!/usr/bin/env bash
# Pubblica Ide.App come cartella Linux self-contained (nessun .NET richiesto sulla
# macchina di destinazione, solo le dipendenze di sistema di WebKitGTK - vedi README.md).
#
# NOTA: niente -p:PublishSingleFile qui (a differenza di scripts/package-release.sh, usato
# per gli zip di release). Questo script serve per una publish locale rapida stile
# bin/Debug/net8.0/ (cartella con i file sciolti) - piu' comodo per iterare/debuggare sulla
# build Release senza gestire l'estrazione delle librerie native che il single-file richiede.
# VbControls/VbControls.Abstractions non sono piu' DLL a se stanti dalla v0.2.1 (sono
# sorgenti incluse in Ide.Designer); l'unico file che ComponentPluginLoader cerca ancora
# come fisico accanto all'eseguibile e' Microsoft.JSInterop.dll (vedi Ide.App.csproj,
# target KeepPluginAssembliesOutOfSingleFile).
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
