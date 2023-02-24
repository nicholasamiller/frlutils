dotnet pack

dotnet nuget push ((gci '.\bin\Debug\*.nupkg') | select -First 1).FullName --api-key $nugetKey --source https://api.nuget.org/v3/index.json