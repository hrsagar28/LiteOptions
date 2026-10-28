#!/bin/bash
# Builds LiteOptions.exe (.NET Framework 4.8, AnyCPU -> runs natively on x64 and ARM64 Windows)
set -e
cd "$(dirname "$0")"
CSC="dotnet /usr/lib/dotnet/sdk/8.0.132/Roslyn/bincore/csc.dll"
REF=/usr/lib/mono/4.8-api
REFS="-r:$REF/mscorlib.dll -r:$REF/System.dll -r:$REF/System.Core.dll -r:$REF/System.Drawing.dll -r:$REF/System.Windows.Forms.dll -r:$REF/System.Runtime.Serialization.dll -r:$REF/System.Xml.dll -r:$REF/System.IO.Compression.dll -r:$REF/Facades/System.Runtime.dll -r:$REF/Facades/System.Runtime.InteropServices.RuntimeInformation.dll -r:$REF/Facades/netstandard.dll"
mkdir -p build
$CSC -nologo -noconfig -nostdlib -langversion:10 -unsafe+ -optimize+ -deterministic -warn:4 -nowarn:1701,1702,0649 \
  -target:winexe -platform:anycpu -out:build/LiteOptions.exe \
  -win32icon:assets/app.ico -win32manifest:assets/app.manifest -resource:assets/tray.ico,LiteOptions.app.ico -resource:assets/logo.png,LiteOptions.logo.png \
  $REFS assets/AssemblyInfo.cs $(find src -name '*.cs' | sort)
ls -la build/LiteOptions.exe
