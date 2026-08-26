# Pubblica Ide.App come cartella Windows self-contained (nessun .NET richiesto sulla
# macchina di destinazione, solo il runtime WebView2 - vedi README.md, di norma gia'
# preinstallato su Windows 10 21H2+/11).
#
# NOTA: niente -p:PublishSingleFile. Il ComponentPluginLoader (modulo 14) cerca
# VbControls.dll/VbControls.Abstractions.dll come file fisici accanto all'eseguibile per
# referenziarli nella compilazione Roslyn dei componenti-plugin; in modalita' single-file
# quegli assembly verrebbero incorporati nell'eseguibile e non piu' presenti su disco,
# facendo fallire silenziosamente il caricamento dei plugin. Una cartella con i file
# sciolti (identica nella forma a bin/Debug/net8.0/ gia' usata in sviluppo) evita il problema.
Set-Location (Join-Path $PSScriptRoot "..")

$Rid = "win-x64"
$Out = "dist/$Rid"

dotnet publish src/Ide.App/Ide.App.csproj `
  -c Release `
  -r $Rid `
  --self-contained true `
  -o $Out

Write-Host ""
Write-Host "Pubblicato in $Out/"
Write-Host "Per avviare: $Out\Ide.App.exe"
Write-Host "Prerequisito di sistema sulla macchina di destinazione: runtime WebView2 (vedi README.md)."
