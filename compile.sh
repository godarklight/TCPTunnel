#!/bin/sh
dotnet publish -r win-x64 -c Release --no-self-contained
dotnet publish -r linux-x64 -c Release --no-self-contained
dotnet publish -r osx-x64 -c Release --no-self-contained
dotnet publish -r linux-arm -c Release --no-self-contained
dotnet publish -r linux-arm64 -c Release --no-self-contained
rm -rf build
mkdir build
cd build
cp -av ../bin/Release/netcoreapp3.1/win-x64/publish/ win-x64 
cp -av ../bin/Release/netcoreapp3.1/osx-x64/publish/ osx-x64 
cp -av ../bin/Release/netcoreapp3.1/linux-x64/publish/ linux-x64 
cp -av ../bin/Release/netcoreapp3.1/linux-arm/publish/ linux-arm
cp -av ../bin/Release/netcoreapp3.1/linux-arm64/publish/ linux-arm64
zip -r release.zip .
