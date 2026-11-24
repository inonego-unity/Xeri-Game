# Foundation

Foundation은 게임 전체가 공통으로 사용할 **논리 객체의 등록 수명, Scene 표현의 수명, 행동 상태 전이, World 데이터 연결**을 위한 Runtime 계약입니다. 실제 게임 규칙과 이를 연결하는 Host의 소유권은 별도로 유지합니다.

## 시스템과 책임

| 영역 | 관리 대상 | 담당 요소 | 문서 |
|---|---|---|---|
| Entity / Spawn | 논리 객체의 등록·해제, Key와 수명 | `EntityBase`, `EntitySpawnRegistry`, `EntityHandle`, Registry Binding | [Entity와 Spawn](entity-and-spawn.md) |
| Actor / Presentation | Scene 표현 객체 및 시각 자원의 생성·연결·반환 | `EntityActorController`, `EntityActorRuntime`, `EntityActorRuntimeBinding` | [Actor와 Presentation](actor-and-presentation.md) |
| State Machine | 현재 실행 중인 행동과 전이 | `StateMachine<TOwner>`, `IState`, `StateBase<TOwner>` | [State Machine](state-machine.md) |
| World | 논리 World의 지속 데이터와 Scene 객체 연결 | `WorldObjectStateStore`, `WorldObjectSceneBinding` | [WorldObject](world-object.md) |

## 수명과 소유권 경계

- **Entity**는 Registry의 Key와 등록 수명을 갖습니다. **Actor**는 Scene에서 만들어졌다가 사라져도 Entity는 유지될 수 있습니다.
- **WorldObjectState**는 Store가 보유하는 Live 데이터입니다. **IWorldObject** Scene 표현이 해제되어도 상태는 유지될 수 있습니다.
- **State Machine의 IState**는 행동 생명주기입니다. WorldObjectState의 지속 데이터와 자동으로 같은 객체가 되지 않습니다.
- `EntityActorRuntimeBinding`과 `WorldObjectSceneBinding`은 서로를 자동으로 관리하지 않습니다. 게임 Host/Composition이 연결 순서와 역순 정리를 결정합니다.

## Foundation 바깥의 책임

게임 Host가 실제 Scene 로딩, Entity/Actor 생성 정책, FSM 전이 조건, World State Factory, 데이터 복원 및 종료 순서를 정합니다. Save 파일 직렬화, 네트워크 권한·동기화, Replica 공개 정책, Replay 시계, 게임플레이 보상 실행은 이 Foundation 계약의 기본 기능이 아닙니다.

## 온라인·상태 연속성과의 관계

Foundation은 로컬 수명 계약을 갖추고, 네트워크·저장·복원 기능은 소비처가 붙일 수 있는 지점으로만 제공합니다.

| 필요한 기능 | Foundation이 제공하는 것 | 소비처(Host/Adapter)가 정하는 것 |
|---|---|---|
| 원격 대상 식별 | Registry 안의 Entity Key, `Find(key)`, 로컬 수명 검증용 `EntityHandle` | `NetworkId → Entity Key` 대응표, Key 재사용 정책 |
| 외부 확정 Entity 등록 | `SpawnWithKey`, 게임 Spawn 훅 없는 `RegisterWithKey` | 서버가 확정한 Key와 값, 공개 범위 |
| 현재 상태 구성 | 값을 먼저 준비한 Entity 등록, Store 선적재 후 `Bind`, `ApplyCurrentState` | 적용 순서, 대상이 없을 때의 보관 정책 |
| 상태 캡처·저장 | `[Serializable]` 파생 `WorldObjectState`와 `States` 읽기 | 캡처 시점·형식, Snapshot 복제 |
| 오래된 상태 거절 | `ValidateCurrentState` 검증 단계 | Revision·Epoch 필드와 비교 규칙 |
| FSM 상태 복원 | `StateMachine.ReplaceCurrent`(파생 머신용) | 활성 상태 식별 방식, 상태 데이터 저장 |
| 예상 표현·보간 초기화 | `IEntityPresentationProvider` 구현 지점 | 예상·확정 구분, 취소·정정 |

## 시작 경로

- 논리 객체의 Key와 등록 수명이 필요하면 [Entity Registry 구현](../guides/foundation/create-entity-registry.md)을 참고합니다.
- Scene Actor와 Presentation을 구성하려면 [Entity Actor Runtime 조립](../guides/foundation/compose-entity-actor-runtime.md)을 참고합니다.
- 행동 상태를 분리하려면 [State Machine 구성](../guides/foundation/create-state-machine.md)을 참고합니다.
- Scene 변경 뒤 문·상자 등 객체의 현재 상태를 유지하려면 [WorldObject와 지속 상태](world-object.md) 및 [WorldObject 구성 가이드](../guides/foundation/create-world-object.md)를 참고합니다.
