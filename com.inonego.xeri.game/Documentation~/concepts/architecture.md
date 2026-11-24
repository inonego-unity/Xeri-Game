# Xeri Game 구조와 소유권

## 의존 방향

~~~text
소비 프로젝트 정책·콘텐츠·Host
        ↓
Xeri Game — Modules
        ↓
Xeri Game — Foundation
        ↓
Xeri Core — 공통 계약과 유틸리티
~~~

이 구조는 **논리적 책임 관계**입니다. Xeri Game의 기본 Runtime은 `inonego.Xeri.Game` 어셈블리를 사용하며, Foundation·Modules 폴더마다 별도의 Assembly를 요구하지 않습니다.

## Foundation

- [Entity와 Spawn Registry](../foundation/entity-and-spawn.md): 논리 객체 ID, Registry 소속과 등록 수명
- [Actor와 Presentation](../foundation/actor-and-presentation.md): Unity Actor 객체·Entity 관계와 표현 자원 소유권
- [State Machine](../foundation/state-machine.md): 상태·전이
- [WorldObject](../foundation/world-object.md): 지속 상태와 Scene 객체의 연결 계약

Foundation 자체가 WorldManager, Simulation Clock, Network Authority를 제공한다는 뜻은 아닙니다.

## Modules

[Action](../modules/action/index.md), [Combat](../modules/combat/index.md), [Interaction](../modules/interaction/index.md), [World](../modules/world/index.md), [Locomotion](../modules/locomotion/index.md) 등 기능이 서로 독립적인 정책과 실행 수명을 가집니다.

Ability는 전투 엔진이나 입력 시스템을 직접 소유하지 않고 선택된 결과를 프로젝트 Host가 실행합니다. Zone·Board·Physics도 콘텐츠의 결과 공식을 소유하지 않습니다.

## Core와 외부 패키지

`IKeyGenerator`, `XDictionary`, Spatial, Serializer, Lease, Pool은 Core에 있습니다. Game은 이를 사용하지만 Core가 Game 타입을 역참조하지 않습니다.

Xeri UI·Xeri Fabric과의 연결은 프로젝트 또는 선택적 연동 어셈블리가 소유합니다. Objective·Narrative 등 게임별 기능은 기본 Runtime에 모두 포함된 공통 Foundation 계약이 아니며, 각 소비 프로젝트가 필요한 Domain을 선택해 구성합니다.
