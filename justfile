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
    rm -frv {{publish_dir}}
    dotnet publish {{project}} -c Release -r {{runtime}} -o {{publish_dir}} --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false

clean:
    find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
