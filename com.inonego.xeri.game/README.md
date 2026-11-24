# Xeri Game

Xeri Game은 여러 프로젝트에서 재사용하는 게임 도메인 기반과 게임 기능을 제공하는 Unity UPM 패키지입니다.

- 패키지: `com.inonego.xeri.game`
- 기본 어셈블리: `inonego.Xeri.Game`
- 기본 네임스페이스: `inonego.Xeri.Game`
- 기본 의존성: `com.inonego.xeri` (Xeri Core)
- UI, Fabric 및 특정 Network SDK는 기본 어셈블리의 필수 의존성이 아닙니다.

## Runtime 구성

```text
Runtime/
├── Foundation/
│   ├── Entity/               # Entity, Handle, Actor, Presentation, Entity Spawn
│   ├── Spawn/                # SpawnRegistry와 객체 수명 계약
│   ├── State/                # State Machine
│   └── World/                # WorldObject와 상태 계약
└── Modules/
    ├── Action/               # Ability, Execution, Operation, Delivery, Targeting
    ├── AI/
    ├── Character/            # Controller, GroundCheck, GroundSuspension, Jump
    ├── Combat/               # HP, Faction
    ├── Interaction/          # Use, Reaction
    ├── Presentation/         # Billboard
    ├── Progression/          # Level
    └── World/                # Zone, Board, Physics
```

`Foundation`은 현재 공통 게임 기반을 배치한 영역입니다. World Manager, Simulation Clock, Network Adapter 등 별도의 공통 시스템이 구현되어 있다는 뜻은 아닙니다.

`Foundation`과 `Modules`는 디렉터리 분류명이며 C# 네임스페이스에 삽입하지 않습니다. Runtime의 네임스페이스는 `inonego.Xeri.Game`이며 Character Controller 계열은 `inonego.Xeri.Game.Controller`를 사용합니다.

## 의존성 및 어셈블리

- 기본 Runtime 어셈블리 `inonego.Xeri.Game`은 `inonego.Xeri`만 참조합니다.
- Core가 Game에 의존하지 않도록 구성합니다.
- UI, Fabric, Network SDK 연동이 필요하면 선택적 연동 어셈블리에서 참조합니다.
- 일반 직렬화와 런타임 저장·복원 기능은 현재 시스템의 기능 계약으로 제공됩니다.

## 테스트

`Tests/EDIT`, `Tests/PLAY`에 게임 회귀 테스트가 있습니다.

- EditMode 어셈블리: `inonego.Xeri.Game.TEST.EDIT`
- PlayMode 어셈블리: `inonego.Xeri.Game.TEST.PLAY`
- 테스트 어셈블리는 Core와 Game을 참조합니다. PlayMode 어셈블리에서만 `Unity.InputSystem`도 참조합니다.
- Game Runtime의 `AssemblyInfo.cs`는 Game 테스트 어셈블리에 내부 API를 제한 공개합니다.

공개 시스템 설명과 사용 절차는 [Xeri Game Manual](Documentation~/index.md)에서 관리하며, API Reference는 DocFX로 생성합니다. `Documentation~`는 Unity에서 Import하지 않으므로 문서 파일에 `.meta`를 만들지 않습니다.

패키지 구조 및 Unity 메타데이터 검사는 저장소의 `scripts/validate-package.py`로 수행할 수 있습니다. 실제 컴파일과 테스트 실행은 Unity Editor에서 별도로 확인합니다.

Objective·Narrative는 별도 기능 작업 트리에서 개발 중이며 현재 기본 Runtime 구성에는 포함되어 있지 않습니다.
