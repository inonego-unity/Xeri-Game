# Foundation

여러 Game 기능이 공유하는 객체 식별·등록·기본 상태·WorldObject 연결 계약입니다.

| 영역 | 제공 기능 | 문서 |
|---|---|---|
| Entity·Spawn | Key, Handle, Registry, Actor, Presentation | [Entity와 Spawn](entity-and-spawn.md) |
| State | State Machine과 상태 전이 | [State Machine](state-machine.md) |
| World | 안정 ID와 WorldObjectState 연결 | [WorldObject](world-object.md) |

Foundation에 기존 공통 코드를 배치했다는 의미이지, 아직 없는 Simulation/Network 기능을 구현했다는 뜻은 아닙니다.

처음 시작한다면 [Entity Registry 구현](../guides/foundation/create-entity-registry.md), [Actor Runtime 조립](../guides/foundation/compose-entity-actor-runtime.md)을 참고하세요.
