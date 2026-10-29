# 설치 파일 빌드

Windows에서 .NET 10 SDK와 Inno Setup 6를 설치한 뒤 저장소 루트에서 실행합니다.

```powershell
dotnet publish wpf/MacDock.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o wpf/publish/v0.4.6/standalone
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' installer/MacDock.iss
```

결과: `wpf/publish/v0.4.6/installer/MacDock-v0.4.6-win-x64-Setup.exe`.
버전 변경 시 csproj와 iss의 AppVersion을 함께 변경하세요. AppId는 업데이트 인식을 위해 유지하세요.

설치는 현재 사용자 범위이며 관리자 권한을 요청하지 않습니다. 제거 시 사용자 설정은 보존하고, 설치된 실행 파일을 가리키는 자동시작 항목만 지웁니다.
