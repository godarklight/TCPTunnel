dotnet publish -r win-x64 -c Release --no-self-contained
dotnet publish -r linux-x64 -c Release --no-self-contained
dotnet publish -r osx-x64 -c Release --no-self-contained
cd bin/Release/netcoreapp3.1/win-x64/
rm tunnel.zip
rm -rf win-x64
mv publish win-x64
zip -r tunnel.zip win-x64/
scp tunnel.zip root@192.168.5.1:/var/www/tunnel.zip
