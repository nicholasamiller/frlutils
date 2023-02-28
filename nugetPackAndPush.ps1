dotnet pack

dotnet nuget push (((gci '.\bin\Debug\*.nupkg') | Sort-Object LastWriteTime -Descending | select -First 1).FullName) --api-key $nugetKey --source https://api.nuget.org/v3/index.json