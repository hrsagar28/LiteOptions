#!/bin/bash
set -e
cd "$(dirname "$0")"
CSC="dotnet /usr/lib/dotnet/sdk/8.0.132/Roslyn/bincore/csc.dll"
REF=/usr/lib/mono/4.8-api
REFS="-r:$REF/mscorlib.dll -r:$REF/System.dll -r:$REF/System.Core.dll -r:$REF/System.Drawing.dll -r:$REF/System.Windows.Forms.dll -r:$REF/System.Runtime.Serialization.dll -r:$REF/System.Xml.dll -r:$REF/System.IO.Compression.dll -r:$REF/Facades/System.Runtime.dll -r:$REF/Facades/System.Runtime.InteropServices.RuntimeInformation.dll -r:$REF/Facades/netstandard.dll"
mkdir -p build/test
$CSC -nologo -noconfig -nostdlib -langversion:10 -unsafe+ -target:exe -platform:anycpu -out:build/test/LiteOptionsTests.exe -main:LiteOptions.Tests.TestMain \
  -resource:assets/tray.ico,LiteOptions.app.ico -resource:assets/logo.png,LiteOptions.logo.png $REFS assets/AssemblyInfo.cs $(find src -name "*.cs" ! -name Program.cs | sort) tests/Fakes.cs tests/Tests.cs
mono build/test/LiteOptionsTests.exe
