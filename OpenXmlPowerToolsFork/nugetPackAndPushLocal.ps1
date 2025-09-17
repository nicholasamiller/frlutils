dotnet pack

dotnet nuget push (((gci '.\bin\Release\*.nupkg') | Sort-Object LastWriteTime -Descending | select -First 1).FullName) --api-key NUGET_SERVER_API_KEY --source http://192.168.1.152:5555/v3/index.json