# 배포용 zip 두 가지를 release\ 폴더에 만든다.
#   SnapTranslate-<버전>-win-x64.zip                : 작은 배포본(.NET 9 데스크톱 런타임 필요)
#   SnapTranslate-<버전>-win-x64-self-contained.zip : 런타임 포함(설치 불필요, 용량 큼)
# 사용: powershell -File tools\package.ps1 [-Step All|Publish|Zip]
#   All     : 빌드하고 압축까지 (기본)
#   Publish : release\stage 에 빌드만 한다 (CI 에서 실행 파일에 서명하기 전 단계)
#   Zip     : release\stage 에 있는 파일을 압축만 한다 (서명한 실행 파일을 넣은 뒤)
param([ValidateSet('All', 'Publish', 'Zip')][string]$Step = 'All')
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'SnapTranslate.csproj'

# 버전은 csproj 의 <Version> 에서 읽는다
[xml]$xml = Get-Content $project -Encoding UTF8
$version = ($xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { throw 'csproj 에서 Version 을 찾지 못했습니다.' }

$release = Join-Path $root 'release'
$packages = @(
    @{ Name = "SnapTranslate-$version-win-x64"; SelfContained = 'false' },
    @{ Name = "SnapTranslate-$version-win-x64-self-contained"; SelfContained = 'true' }
)

foreach ($package in $packages) {
    $stage = Join-Path $release ("stage\" + $package.Name)
    New-Item -ItemType Directory -Force $stage | Out-Null

    if ($Step -ne 'Zip') {

    # 실행 파일 하나로 묶는다. 디버그 기호(pdb)는 넣지 않는다
    dotnet publish $project -c Release -r win-x64 --self-contained $package.SelfContained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=$($package.SelfContained) `
        -p:DebugType=none -p:DebugSymbols=false -o $stage --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "publish 실패: $($package.Name)" }

    Copy-Item (Join-Path $root 'README.md') $stage -Force
    Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'LICENSE.txt') -Force
    # 함께 배포하는 .NET 런타임 등의 라이선스 고지
    Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $stage -Force
    }
    if ($Step -eq 'Publish') { continue }

    # 개인 설정 파일이 섞여 들어가지 않게 필요한 파일만 고른다
    $files = Get-ChildItem $stage -File | Where-Object { $_.Name -ne 'config.json' }
    $zip = Join-Path $release ($package.Name + '.zip')
    Compress-Archive -Path $files.FullName -DestinationPath $zip -Force
    $size = [Math]::Round((Get-Item $zip).Length / 1MB, 1)
    "$($package.Name).zip  ${size} MB  (" + (($files | ForEach-Object Name) -join ', ') + ")"
}
