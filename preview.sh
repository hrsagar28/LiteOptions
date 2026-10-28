#!/bin/bash
# Renders the UI with simulated devices under Mono/Xvfb (layout check only; fonts differ from Windows)
set -e
cd "$(dirname "$0")"
CSC="dotnet /usr/lib/dotnet/sdk/8.0.132/Roslyn/bincore/csc.dll"; REF=/usr/lib/mono/4.8-api
REFS="-r:$REF/mscorlib.dll -r:$REF/System.dll -r:$REF/System.Core.dll -r:$REF/System.Drawing.dll -r:$REF/System.Windows.Forms.dll -r:$REF/System.Runtime.Serialization.dll -r:$REF/System.Xml.dll -r:$REF/System.IO.Compression.dll -r:$REF/Facades/System.Runtime.dll -r:$REF/Facades/System.Runtime.InteropServices.RuntimeInformation.dll -r:$REF/Facades/netstandard.dll"
$CSC -nologo -noconfig -nostdlib -langversion:10 -unsafe+ -nowarn:0649 -target:exe -out:build/test/Preview.exe -main:LiteOptions.Tests.PreviewMain \
  -resource:assets/tray.ico,LiteOptions.app.ico -resource:assets/logo.png,LiteOptions.logo.png $REFS assets/AssemblyInfo.cs $(find src -name '*.cs' ! -name Program.cs | sort) tests/Fakes.cs tests/Preview.cs
pkill Xvfb || true; sleep 0.5; (Xvfb :99 -screen 0 1600x1500x24 >/dev/null 2>&1 &) ; sleep 1
cd build/test
for theme in light dark; do
  rm -rf "preview-$theme"; rm -rf ~/.config/LiteOptions
  LITEOPTIONS_THEME=$theme DISPLAY=:99 timeout 120 mono Preview.exe "preview-$theme" 2>&1 | grep -v "^$\|Gtk\|locale" | tail -4
done
