#!/usr/bin/pwsh

#build script for creating powershell module packages

#build master solution
dotnet publish -c Release ./Cinegy.InstallModule.sln

#copy binaries for centralized access or packaging, split into sub-projects
New-Item -ItemType Directory -Force -Path bin/Cinegy.InstallModule
Copy-Item -Recurse -Force ./Cinegy.InstallModule/bin/Release/netstandard2.0/publish/*.dll ./bin/Cinegy.InstallModule/
