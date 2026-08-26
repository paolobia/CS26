#!/usr/bin/env bash
# Costruisce i pacchetti di release pronti da scaricare (IDE gia' compilato + progetto
# template + esempi), uno zip per piattaforma, in dist-release/.
#
# Uso: scripts/package-release.sh <versione>   (es. scripts/package-release.sh 0.1.0)
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="${1:?Uso: scripts/package-release.sh <versione>, es. 0.1.0}"
OUT_ROOT="dist-release"
rm -rf "${OUT_ROOT:?}"
mkdir -p "$OUT_ROOT"

# File/cartelle che non hanno senso in un pacchetto scaricato (build locali, git, IDE
# files, script di packaging stessi).
EXCLUDES=(--exclude "bin" --exclude "obj" --exclude ".vs")

package_platform() {
    local rid="$1"          # linux-x64 | win-x64
    local exe_name="$2"     # Ide.App | Ide.App.exe
    local pkg_name="IdeApp-v${VERSION}-${rid}"
    local pkg_dir="$OUT_ROOT/$pkg_name"

    echo "== Pubblico Ide.App per $rid =="
    # NIENTE -p:PublishSingleFile: testato e verificato che rompe completamente
    # ComponentPluginLoader. In single-file, self-contained bundla anche il runtime .NET
    # stesso nell'eseguibile: Assembly.Location ritorna stringa vuota per QUALUNQUE assembly
    # incorporato (non solo VbControls/Ide.Designer - anche System.Private.CoreLib e tutto
    # il resto), quindi Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), ...) e
    # AppDomain.CurrentDomain.GetAssemblies().Select(a => a.Location) non producono piu'
    # alcun riferimento valido per Roslyn: la compilazione di Components/*.cs fallisce
    # perfino su "System" (verificato: 256 errori, 0 componenti caricati). Restare su una
    # cartella self-contained "sciolta" e' l'unica opzione finche' ComponentPluginLoader
    # dipende da percorsi fisici di assembly per le MetadataReference.
    dotnet publish src/Ide.App/Ide.App.csproj -c Release -r "$rid" --self-contained true -o "$pkg_dir"

    echo "== Copio template e progetti di esempio (sorgente) =="
    mkdir -p "$pkg_dir/templates"
    rsync -a "${EXCLUDES[@]}" templates/BlazorPwaTemplate/ "$pkg_dir/templates/BlazorPwaTemplate/"
    mkdir -p "$pkg_dir/samples"
    rsync -a "${EXCLUDES[@]}" samples/HelloWorldApp/ "$pkg_dir/samples/HelloWorldApp/"
    rsync -a "${EXCLUDES[@]}" samples/RssFeedViewer/ "$pkg_dir/samples/RssFeedViewer/"

    # templates/BlazorPwaTemplate.csproj e samples/RssFeedViewer.csproj includono via glob
    # (<Compile Include="..\..\src\VbControls\**\*.cs" />) i file .cs di VbControls/
    # VbControls.Abstractions - non sono progetti .csproj a se stanti, solo sorgenti: servono
    # comunque in questo pacchetto, alla stessa profondita' relativa, altrimenti dotnet
    # watch/build falliscono per mancanza dei file al primo avvio.
    mkdir -p "$pkg_dir/src"
    rsync -a "${EXCLUDES[@]}" src/VbControls/ "$pkg_dir/src/VbControls/"
    rsync -a "${EXCLUDES[@]}" src/VbControls.Abstractions/ "$pkg_dir/src/VbControls.Abstractions/"

    echo "== Copio documentazione =="
    cp ARCHITECTURE.md "$pkg_dir/ARCHITECTURE.md"
    sed "s/{{EXE_NAME}}/$exe_name/g" scripts/release-readme-template.md > "$pkg_dir/README.md"

    echo "== Comprimo $pkg_name.zip =="
    (cd "$OUT_ROOT" && zip -q -r "${pkg_name}.zip" "$pkg_name")
    echo "-> $OUT_ROOT/${pkg_name}.zip"
}

package_platform "linux-x64" "Ide.App"
package_platform "win-x64" "Ide.App.exe"

echo ""
echo "Pacchetti pronti in $OUT_ROOT/:"
ls -lh "$OUT_ROOT"/*.zip
