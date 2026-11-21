#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
root=$PWD
managed=${1:-${ONI_MANAGED_DIR:-}}
if [[ -z $managed || ! -f $managed/Assembly-CSharp.dll ]]; then
    echo 'Specify the ONI Managed folder containing Assembly-CSharp.dll (argument 1 or ONI_MANAGED_DIR).' >&2
    exit 2
fi
managed=$(cd -- "$managed" && pwd -P)
out=${2:-"$root/dist"}
mkdir -p -- "$out"
out=$(cd -- "$out" && pwd -P)
dotnet=${DOTNET_EXE:-dotnet}
command -v -- "$dotnet" >/dev/null || { echo 'Install the .NET SDK or set DOTNET_EXE.' >&2; exit 2; }
command -v zip >/dev/null || { echo 'The zip command is required.' >&2; exit 2; }
tmp=$(mktemp -d)
trap 'rm -rf -- "$tmp"' EXIT
"$dotnet" build "$root/OniPrecision.csproj" -c Release -o "$tmp/build" "-p:OniManagedDir=$managed"
mkdir -p -- "$tmp/package/OniPrecision"
for name in mod.yaml mod_info.yaml; do
    cp -- "$root/$name" "$tmp/package/OniPrecision/$name"
done
cp -- "$tmp/build/OniPrecision.dll" "$tmp/package/OniPrecision/OniPrecision.dll"
cp -- "$root/README.zh-CN.md" "$tmp/package/README.zh-CN.md"
(
    cd -- "$tmp/package"
    zip -q "$tmp/OniPrecision-install.zip" README.zh-CN.md \
        OniPrecision/mod.yaml OniPrecision/mod_info.yaml OniPrecision/OniPrecision.dll
)
mv -f -- "$tmp/OniPrecision-install.zip" "$out/OniPrecision-install.zip"
printf 'Install archive: %s\n' "$out/OniPrecision-install.zip"
