#!/bin/sh
dotnet publish -r win-x64 -c Release --no-self-contained
dotnet publish -r linux-x64 -c Release --no-self-contained
dotnet publish -r osx-x64 -c Release --no-self-contained
rsync -av --delete bin/Release/netcoreapp3.1/linux-x64/publish godarklight.info.tm:TCPTunnel/
cd bin/Release/netcoreapp3.1/win-x64/
rm tunnel.zip
rm -rf win-x64
mv publish win-x64
zip -r tunnel.zip win-x64/
scp tunnel.zip root@godarklight.info.tm:/var/www/tunnel.zip
