echo "build: Build started"

Push-Location $PSScriptRoot

if(Test-Path .\artifacts) {
	echo "build: Cleaning .\artifacts"
	Remove-Item .\artifacts -Force -Recurse
}

& dotnet restore --no-cache

$branch = @{ $true = $env:APPVEYOR_REPO_BRANCH; $false = $(git symbolic-ref --short -q HEAD) }[$env:APPVEYOR_REPO_BRANCH -ne $NULL];
$revision = @{ $true = "{0:00000}" -f [convert]::ToInt32("0" + $env:APPVEYOR_BUILD_NUMBER, 10); $false = "local" }[$env:APPVEYOR_BUILD_NUMBER -ne $NULL];
$suffix = @{ $true = ""; $false = "$($branch.Substring(0, [math]::Min(10,$branch.Length)))-$revision"}[$branch -eq "master" -and $revision -ne "local"]

echo "build: Version suffix is $suffix"
msbuild serilog-sinks-loggly.sln /m /t:restore /p:Configuration=Release
msbuild serilog-sinks-loggly.sln /t:build /m /p:Configuration=Release /p:VersionSuffix=$suffix
msbuild "src/Serilog.Sinks.Loggly/Serilog.Sinks.Loggly.csproj" /t:pack /p:Configuration=Release /p:VersionSuffix=$suffix /p:PackageOutputPath=artifacts /p:NoPackageAnalysis=true
foreach ($test in ls test/*.Tests) {
    Push-Location $test

	echo "build: Testing project in $test"

    & dotnet test -c Release
    if($LASTEXITCODE -ne 0) { exit 3 }

    Pop-Location
}
Pop-Location
