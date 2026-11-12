# Xeri Game 문서

Xeri Game은 **Entity·Spawn·WorldObject의 게임 기반**과 Ability·Execution·Combat·Interaction·World 등 조합 가능한 게임 기능을 제공하는 Unity 패키지입니다.

Xeri Core를 기반으로 동작하며, 프로젝트별 전투 공식·입력 방식·AI 정책·씬 구성까지 대신 결정하지 않습니다.

## 시작하기

1. [설치와 패키지 구성](getting-started/installation.md)
2. [전체 구조와 의존 방향](concepts/architecture.md)
3. [Foundation](foundation/index.md)
4. [기능 모듈](modules/index.md)
5. [사용 가이드](guides/index.md)

## 목적별 찾기

| 목적 | 문서 |
|---|---|
| Entity 등록·Spawn/Despawn | [Entity와 Spawn](foundation/entity-and-spawn.md) |
| Actor와 Presentation 수명 조립 | [Entity Actor Runtime 가이드](guides/foundation/compose-entity-actor-runtime.md) |
| World Object에 상태 연결 | [WorldObject와 State](foundation/world-object.md) |
| State Machine 구성 | [State Machine](foundation/state-machine.md) |
| Ability를 Execution·Delivery에 연결 | [Action](modules/action/index.md) |
| HP·Faction 판정 | [Combat](modules/combat/index.md) |
| 상호작용 후보와 Action 연결 | [Interaction](modules/interaction/index.md) |
| Zone·Board·Physics Query | [World](modules/world/index.md) |
| AI 판단과 Group | [AI](modules/ai/group-and-brain.md) |
| Controller·Jump·GroundCheck | [Controller](modules/character/controller.md) |
| Level | [Progression](modules/progression/level.md) |
| Billboard 표현 | [Billboard](modules/presentation/billboard.md) |

## 패키지 경계

- **Xeri Core:** Key, Collection, Condition, 일반 직렬화, Pool, Spatial 등 범용 기능
- **Xeri Game:** Foundation과 게임 기능 Modules
- **Xeri UI / Xeri Fabric:** 별도 패키지. Xeri Game 기본 Runtime에는 필수 의존성을 추가하지 않습니다.
- **소비 프로젝트:** 게임별 Policy, Host 실행 승인, 저장소와 표현 연동

타입·멤버별 상세 정보는 [API Reference](../../Docs/api-bridge/index.md)를 참고합니다.
