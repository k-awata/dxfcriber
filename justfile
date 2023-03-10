@_default:
    just --list

# Release the binary to Github
release ver msg:
    dotnet publish
    cp bin/Release/net7.0/win-x64/publish/dxfcriber.exe .
    rm -f *.zip
    7z a dxfcriber_{{ver}}_win-x64.zip dxfcriber.exe LICENSE README.md
    git tag -a v{{ver}} -m "{{msg}}"
    git push origin v{{ver}}
    gh release create -n "{{msg}}" v{{ver}} dxfcriber_{{ver}}_win-x64.zip

# Test
test:
    dotnet run -- temp/*.dxf -r 1 --ymin 100 --ymax 275 --color 6 -c no,285,294 -c desc,295,364 -c qty,365,405 > temp/output.csv
