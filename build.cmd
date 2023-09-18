dotnet publish -c Release -r win-x64 -p:OutputType=WinExe --self-contained false
dotnet publish -c Release -r osx-x64 --self-contained false
dotnet publish -c Release -r linux-x64 --self-contained false