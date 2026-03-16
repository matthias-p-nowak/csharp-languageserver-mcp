set shell := ["sh", "-cu"]
set windows-shell := ["powershell.exe", "-NoLogo", "-NoProfile", "-Command"]

project := "src/Rosalyn.Server/Rosalyn.Server.csproj"
test_project := "tests/Rosalyn.Server.Tests/Rosalyn.Server.Tests.csproj"
publish_dir := "publish"

build:
    dotnet build {{project}}

run:
    dotnet run --project {{project}}

test:
    dotnet test {{test_project}}

publish runtime="linux-x64":
    dotnet publish {{project}} -c Release -r {{runtime}} -o {{publish_dir}} --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false

wpublish:
    just publish win-x64
    if (Test-Path 'C:\WingTech\bin\Rosalyn.Server.000') { Remove-Item 'C:\WingTech\bin\Rosalyn.Server.000' -Force }
    if (Test-Path 'C:\WingTech\bin\Rosalyn.Server.exe') { Move-Item 'C:\WingTech\bin\Rosalyn.Server.exe' 'C:\WingTech\bin\Rosalyn.Server.000' -Force }
    Copy-Item '{{publish_dir}}\Rosalyn.Server.exe' 'C:\WingTech\bin\Rosalyn.Server.exe' -Force

clean:
    find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
