# Xeri Game 문서 사이트 유지보수

Xeri Game의 공개 문서 사이트는 **DocFX**로 빌드합니다. Xeri Core·Xeri UI·Xeri Fabric과 같은 Manual + 생성 API Reference 구조를 사용합니다.

## 문서의 위치

- `com.inonego.xeri.game/Documentation~/`: 사용자를 위한 Manual의 단일 원본
- `Docs/documentation-style.md`: 문서 작성 규칙
- `Docs/templates/`: 모듈 개요와 사용 가이드 템플릿
- `Docs/api-bridge/`: API Reference 진입 문서
- `docfx.json`, `toc.yml`, `index.md`: DocFX 구성과 사이트 내비게이션
- `build-docs.ps1`: 로컬 API metadata 생성 및 DocFX 빌드
- `Docs/api-snapshot.ps1`: 공개 API 소스 계산·snapshot 생성 및 복원
- `Docs/generated-api.zip`, `Docs/generated-api.sha256`: CI용 API metadata와 소스 검증 해시
- `.github/workflows/docs-pages.yml`: GitHub Pages 배포

API 설명은 공개 C# 선언과 XML 주석에 근거해야 하며, 기능의 실행 정책·사용 절차는 Manual에 기록합니다.

## 로컬 빌드

Unity 개발 프로젝트를 지정하여 공개 API 문서를 생성합니다.

```powershell
./build-docs.ps1 -UnityProjectRoot "C:\Path\To\UnityProject"
```

Unity 프로젝트에는 현재 Xeri Core·Xeri Game 패키지가 함께 설치되어 있어야 하고, `inonego.Xeri.Game.csproj` 파일을 생성할 수 있어야 합니다.

API 정보의 생성에는 Unity의 참조 라이브러리와 해당 어셈블리 프로젝트가 필요합니다. DocFX helper는 Game의 **production Runtime 소스**만 API 대상으로 삼고, `Tests/`는 제외합니다.

### CI와 동일한 검증

```powershell
./build-docs.ps1 -VerifySnapshot
./build-docs.ps1 -SkipMetadata
```

첫 명령은 현재 production C#·asmdef·package.json과 저장된 snapshot의 일치를 확인합니다. 두 번째 명령은 CI처럼 저장된 snapshot만으로 사이트를 빌드합니다.

Runtime API가 변경되면 전체 metadata를 새로 생성해 **snapshot과 해시를 함께 갱신**해야 합니다. 오래된 snapshot을 그대로 둔 채 사이트만 배포하면 API 문서가 실제 구현과 달라집니다.

## 배포

GitHub Actions는 main 브랜치 Push 또는 수동 실행 시 다음 순서로 작업합니다.

```text
Checkout → API snapshot 검증 → DocFX build
→ GitHub Pages artifact 업로드 → 배포
```

예상 GitHub Pages 주소: https://inonego-unity.github.io/Xeri-Game/

실제 배포에는 해당 저장소의 Pages 설정에서 GitHub Actions 배포가 활성화돼 있어야 합니다. `_site/`와 `.docfx/`는 생성 산출물로서 Git에 포함하지 않습니다.

## 문서 구조 원칙

Xeri Game의 `Foundation`과 `Modules`는 **패키지 폴더와 공개 Manual의 탐색 분류**입니다. 모든 Runtime 어셈블리를 그 이름으로 쪼개거나 C# 네임스페이스에 삽입하지 않습니다.

범용 기능의 계약을 설명해야 할 때는 Xeri Core 문서를 가리키고, Xeri-Game Manual에서 Core 문서를 중복 작성하지 않습니다. Objective와 Narrative는 실제 구현을 통합할 때 해당 모듈 문서를 연결합니다.
