#!/bin/bash
set -e
cd "$(dirname "$0")"
./build.sh >/dev/null
rm -rf dist && mkdir -p dist/LiteOptions/source
cp build/LiteOptions.exe README.txt dist/LiteOptions/
cp -r src tests assets build.sh test.sh preview.sh package.sh dist/LiteOptions/source/
rm -f dist/LiteOptions/source/assets/app-*.png
(cd dist && zip -qr LiteOptions-1.2.zip LiteOptions)
ls -la dist/LiteOptions-1.2.zip dist/LiteOptions/LiteOptions.exe
