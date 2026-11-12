# Xeri Game

Xeri Game은 여러 Unity 프로젝트에서 공통으로 사용하는 게임 객체 기반과 기능 모듈을 제공합니다.

## 패키지

- UPM: `com.inonego.xeri.game`
- Runtime 어셈블리·기본 네임스페이스: `inonego.Xeri.Game`
- 필수 의존성: [Xeri Core](https://github.com/inonego-unity/Xeri) (`com.inonego.xeri`)
- Xeri UI·Fabric 및 Network Provider 연동은 기본 Runtime의 필수 의존성이 아닙니다.

## 구성

- `Runtime/Foundation/`: Entity·Spawn·State Machine·WorldObject
- `Runtime/Modules/`: Action·AI·Character·Combat·Interaction·Presentation·Progression·World
- `Tests/EDIT/`, `Tests/PLAY/`: Game 회귀 테스트

## 설치와 문서

설치 및 조합 방법은 [Xeri Game Manual](com.inonego.xeri.game/Documentation~/index.md)에서 확인할 수 있습니다. [패키지 README](com.inonego.xeri.game/README.md)는 Runtime 구성과 어셈블리 경계를 설명합니다.

Unity Package Manager에서는 사용하는 Core와 Game의 버전을 맞춰 설치해야 합니다. 로컬 작업 트리를 사용할 때는 두 패키지 디렉터리를 `file:` 의존성으로 지정합니다.

## 검증

- `python scripts/validate-package.py`: UPM 패키지·Unity 메타데이터 검사
- `./build-docs.ps1 -VerifySnapshot`: API 문서 스냅샷 최신성 검사
- `./build-docs.ps1 -SkipMetadata`: DocFX 사이트 생성

공개 Manual은 Unity가 Import하지 않는 `Documentation~`에서 관리합니다.
