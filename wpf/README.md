# MacDock WPF v0.5.1

사용법과 지원 범위는 [메인 설명서](../README.md)를 참고하세요. 이 디렉터리는 현재 배포되는 WPF 앱입니다.

## 배포 빌드

Windows x64에서 .NET 10 SDK, Go 1.25 이상, x64 MinGW C 컴파일러가 필요합니다. PATH에 go와 gcc를 추가하거나 MSBuild의 GoTool 속성과 CC 환경 변수로 경로를 지정하세요. 아래 명령은 저장소 루트에서 실행합니다. 빌드 시 fileshare의 Go 코드와 웹 자산을 MacDockShare.dll로 컴파일하고 단일 파일 배포에 포함합니다.

```powershell
dotnet publish wpf/MacDock.Wpf.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o wpf/publish/compact
Copy-Item wpf/Run-compact.cmd wpf/publish/compact/Run-MacDock.cmd
dotnet publish wpf/MacDock.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o wpf/publish/standalone
```

compact는 .NET 10 Desktop Runtime x64가 필요합니다. standalone은 런타임을 포함합니다. Run-MacDock.cmd는 사용자 폴더에 설치된 .NET도 찾습니다.

배포 결과, 테스트 결과, 사용자 설정은 소스 관리에 포함하지 않습니다. 자동시작을 설정할 때는 dotnet run 대신 배포 실행 파일로 앱을 시작하세요.
