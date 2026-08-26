# Pubblica Ide.App come cartella Windows self-contained (nessun .NET richiesto sulla
# macchina di destinazione, solo il runtime WebView2 - vedi README.md, di norma gia'
# preinstallato su Windows 10 21H2+/11).
#
# NOTA: niente -p:PublishSingleFile qui (a differenza di scripts/package-release.sh, usato
# per gli zip di release). Questo script serve per una publish locale rapida stile
# bin/Debug/net8.0/ (cartella con i file sciolti) - piu' comodo per iterare/debuggare sulla
# build Release senza gestire l'estrazione delle librerie native che il single-file richiede.
# VbControls/VbControls.Abstractions non sono piu' DLL a se stanti dalla v0.2.1 (sono
# sorgenti incluse in Ide.Designer); l'unico file che ComponentPluginLoader cerca ancora
# come fisico accanto all'eseguibile e' Microsoft.JSInterop.dll (vedi Ide.App.csproj,
# target KeepPluginAssembliesOutOfSingleFile).
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
