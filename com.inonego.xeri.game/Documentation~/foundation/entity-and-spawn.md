# Entity와 Spawn 수명

Entity 영역은 **논리 객체의 식별과 Registry 등록 수명**을 관리합니다. Scene에 존재하는 Actor, 표현 자원인 Presentation, World의 지속 상태는 서로 다른 소유권을 가집니다.

## 구성 요소

| 요소 | 관리 책임 |
|---|---|
| `EntityBase` / `IEntity` | Key, `SpawnState`, 등록 Revision 및 Entity 수명 훅 |
| `EntitySpawnRegistryBase<TEntity>` | Entity Key 검증, 외부 Key 등록 및 현재 수명 조회 |
| `EntitySpawnRegistry<TEntity>` | `Acquire()` 기반 로컬 Spawn |
| `SpawnRegistryBase<TKey,T>` | 일반 등록·해제 transaction, Binding, 이벤트, 복원 연결 |
| `EntityHandle` | 객체 참조·Key·등록 Revision을 묶은 비소유 로컬 Handle |
| `ISpawnRegistryBinding<TKey,T>` | Registry 등록 수명에 필수 자원을 동기화하는 연결 |

## EntityBase: 논리 객체의 정체성과 수명

`EntityBase`는 Key와 Spawn 상태 및 수명 훅을 제공합니다. HP·Group·Actor·Presentation·사망 처리 규칙은 모든 Entity의 필수 속성이 아닙니다. 필요한 게임 타입이 `IReadOnlyHPOwner` / `IHPOwner`, `IReadOnlyGroupMember` / `IGroupMember` 등의 capability를 조합합니다.

Entity의 Key는 **현재 Registry 안에서만** 유일합니다. 서로 다른 Registry에서는 같은 Key를 사용할 수 있습니다. Key 값 `0`도 정상 발급될 수 있으므로 Entity 없음의 표시로 사용하지 않습니다.

`RegistrationRevision`은 Entity가 등록되어 `Spawned`가 될 때마다 증가하는 **비직렬화 수명 번호**입니다. 등록이 실패한 뒤 롤백되더라도 증가한 Revision은 재사용하지 않습니다. 이 번호는 로컬 메모리용이므로 원격 Wire ID나 영속 ID로 쓰지 않습니다. Entity Key도 해당 Registry 안에서만 유효한 식별자입니다.

원격 요청과 Network Object 매핑은 그 Registry의 Entity Key를 기준으로 구성합니다. Host가 `NetworkId → Entity Key` 대응표를 소유하고, 요청을 처리할 때 `Find(key)`로 현재 Entity를 조회합니다. 권한 Registry가 Key를 다시 발급하지 않으면 Key가 그 Registry 수명 안에서 대상을 식별하므로 별도의 Generation이 필요하지 않습니다. 기본 `IncreKeyGenerator`는 단조 증가하며 한 번 쓴 값을 재사용하지 않습니다. 해제된 외부 Key를 다시 쓰는 정책을 택한 Host만 재사용을 구별하는 값을 자신의 대응표에 포함합니다.

## EntitySpawnRegistry: 등록과 해제

게임은 `EntitySpawnRegistry<TEntity>`를 상속해 `Acquire()`와 필요한 경우 `Release(entity, reason)` 정책을 정의합니다. Registry가 Key 할당, `SpawnState`, membership과 필수 Binding을 관리하므로 획득 함수에서 상태를 임의로 확정하지 않습니다.

| 경로 | API | Entity 게임 Spawn/Despawn 훅 | Registry Binding |
|---|---|---|---|
| 로컬 Spawn | `TrySpawn(out entity)` | 실행 | 실행 |
| 외부 Key를 사용한 게임 Spawn | `SpawnWithKey(entity, key)` | 실행 | 실행 |
| 준비된 객체의 직접 등록 | `RegisterWithKey(entity, key)` | 실행하지 않음 | 실행 |
| 게임 Despawn | `entity.Despawn()` | 게임 Spawn된 수명에서는 실행 | 정리 |
| 직접 등록 해제 | `Unregister(entity)` | 실행하지 않음 | 정리 |
| 현재 등록 조회 | `Find(key)`, `Registered` | 없음 | 없음 |

직접 등록은 **호출자가 값과 Key를 이미 준비한 Entity를 게임 Spawn 훅 없이 현재 Registry에 참여**시키는 경로입니다. 값의 생성·복원·검증·권한 판정과 외부 공개 범위는 호출자가 소유합니다. `RegisterWithKey` 자체가 Network Replica, Save Restore, Current State Apply를 구현하는 것은 아닙니다. 현재 상태를 구성할 때는 값을 먼저 준비한 Entity를 등록합니다. 그러면 Binding이 만드는 Actor와 Presentation이 준비된 값을 읽고 게임 Spawn 이벤트는 발생하지 않습니다. 직접 등록 객체는 Registry가 `Acquire()`한 것이 아니므로 해제할 때 해당 Registry의 `Release()`를 호출하지 않습니다. `Registered`는 읽기 전용 Dictionary 계약이며 직접 membership을 수정하지 않습니다.

`TrySpawn()`은 `Acquire()`가 null을 반환하면 `false`를 반환합니다. Key 충돌·잘못된 등록 상태 등 계약 위반은 예외입니다. `SpawnWithKey` / `RegisterWithKey`에 이미 등록된 Key를 주면 거절하며, Key 생성기가 충돌한 값을 발급해도 자동으로 다른 Key로 보정하지 않습니다. 외부 Key와 자체 발급 Key를 함께 쓰는 Host는 충돌 방지 정책을 마련해야 하며, 권한 Registry가 확정한 Key를 받는 Registry는 자체 발급을 쓰지 않는 구성이 가장 단순합니다.

## EntityHandle: 현재 등록 수명 조회

`new EntityHandle(entity)`는 등록된 Entity 참조, Key, Revision을 보관합니다. `IsValid()`는 현재 Entity 값들이 Handle 생성 시점과 일치하는지 확인합니다. `EntitySpawnRegistryBase<TEntity>.TryResolve(handle, out entity)`는 여기에 **정확히 그 Entity 인스턴스가 현재 Registry에 등록되어 있는지**까지 확인합니다.

같은 인스턴스와 Key를 다시 등록해도 이전 Revision을 가진 Handle은 유효하지 않습니다. `Find(key)`는 Key만 조회하므로 오래 보관한 타깃·지연 명령에는 등록 수명 검증이 필요합니다. `EntityHandle`은 로컬 메모리용이며 Save나 RPC 식별자로 사용하지 않습니다.

## SpawnRegistry와 Binding: transaction

`ISpawnRegistryBinding<TKey,T>`는 Registry 수명에 필수로 참여하는 외부 자원의 등록·해제를 처리합니다. `AttachBinding(binding)`은 현재 등록된 객체에 먼저 동기화하고 이후 등록에도 적용합니다. `DetachBinding(binding)`은 해당 Binding의 자원을 정리하지만 Entity membership은 유지합니다.

### 등록 순서

```text
객체·Key 준비 및 등록 상태 검증
→ 게임 Spawn: Entity.OnSpawning → Registry.OnSpawning
→ 공통 Registry.OnRegistering
→ membership 추가, Spawned 진입 및 RegistrationRevision 증가
→ 필수 Binding.OnRegistered (등록 순서)
→ 게임 Spawn: Entity.OnSpawned
→ 등록 확정
→ 공통 Registry.OnRegistered
→ 게임 Spawn: Registry.OnSpawned
```

직접 등록(`RegisterWithKey`)은 위 순서에서 **게임 Spawn 전용 훅·이벤트를 실행하지 않습니다.** `Spawned` 상태와 공통 Registry 등록 이벤트·Binding은 동일하게 구성합니다.

필수 Binding 또는 준비 단계가 실패하면 이미 호출한 Binding을 역순으로 정리하고 등록을 롤백합니다. 원래 오류와 정리 오류가 함께 발생하면 `AggregateException`으로 전달합니다. **`OnRegistered`/`OnSpawned` 완료 observer가 예외를 던져도 등록은 이미 확정된 상태**이며 자동으로 Despawn되지 않습니다. 공개 이벤트는 다른 구독자 호출을 계속 시도하며, 등록 시작 실패와 등록 완료 통지 실패는 서로 다른 결과입니다.

### 해제 순서

```text
게임 Spawn: Entity.OnDespawning → Registry.OnDespawning
→ 공통 Registry.OnUnregistering
→ 필수 Binding.OnUnregistering (역순)
→ Registry 내부 연결 해제 및 membership 제거
→ 게임 Spawn: Entity.OnDespawned 및 Key 해제
→ 공통 Registry.OnUnregistered
→ 게임 Spawn: Registry.OnDespawned
→ 게임 Spawn: Registry.Release(entity, reason)
```

직접 등록(`Unregister`)은 **게임 Spawn/Despawn 훅과 `Release`를 실행하지 않고**, Registry 등록 종료 이벤트·필수 Binding 정리 및 Key 해제로 수명을 끝냅니다. 반대로 `SpawnWithKey`는 외부에서 준비한 Entity라도 **게임 Spawn 경로**이므로 종료할 때 `Release`가 호출됩니다. 외부 Entity를 어떤 출처로 반환할지는 Registry 구현의 정책입니다.

해제 중 일부 단계가 실패해도 가능한 **terminal cleanup**은 계속 시도하며 오류를 전달합니다. 완료된 수명을 같은 객체에 대해 무조건 다시 반환하지 않습니다. `DespawnReason`은 반환 사유이고 등록이 실제 Commit되었는지와 별개의 정보입니다. 게임은 `Code`로 시야 이탈에 따른 정리나 예측 거절 같은 사유를 구분해 전달할 수 있으며, 그것이 게임상의 제거를 뜻하는지는 호출자가 정합니다.

### 재진입과 일괄 수명 경계

같은 Entity의 등록·해제·완료 알림 중 동일 Entity로 다시 진입하면 거절합니다. **다른 Entity의 동기 수명 호출은 허용**하되, 아직 진행 중인 원래 Entity로 돌아오는 순환 재진입은 차단합니다. `DespawnAll()` 중에는 새 Spawn과 중첩 일괄 종료를 허용하지 않습니다. `AttachBinding`/`DetachBinding`의 현재 집합 동기화 중에는 Spawn·Despawn·Binding 구성 변경을 금지합니다.

### 직렬화 후 재구성

`SpawnRegistry.OnAfterDeserialize()`는 저장된 membership을 기준으로 Registry 내부 수명 연결을 복원합니다. 외부 Binding과 게임 Spawn 이벤트를 자동 재실행하지 않습니다. 필요한 Actor/Presentation은 Host가 `AttachBinding` 또는 `EntityActorRuntimeBinding.Bind(registry)`로 현재 등록 대상에 다시 연결합니다.

## Actor·Presentation과 연결하는 경계

Entity Registry는 **논리 객체의 등록 수명**을 관리하며 Actor의 GameObject 획득이나 Presentation 자원을 직접 생성하지 않습니다. 표현이 필요할 때만 `ISpawnRegistryBinding`을 통해 `EntityActorRuntimeBinding`을 선택적으로 연결합니다. 이 Binding은 현재·이후 등록된 Entity에 필요한 Actor와 Presentation을 구성하고, **자신이 자동 생성한 자원만** 반환합니다. `Unbind()` 후에도 Registry의 Entity는 유지됩니다.

Actor 객체·Entity 관계의 0..N 관리, Factory의 Pool 반환, Provider의 생성·Rebind·Rollback, Runtime 수동·자동 소유권에 대한 세부 계약은 [Actor와 Presentation 수명](actor-and-presentation.md)에 있습니다. 실제 구성 절차는 [Entity Actor Runtime 조립 가이드](../guides/foundation/compose-entity-actor-runtime.md)를 참고합니다.

## World·State 및 상위 Host와의 경계

| 대상 | 식별·수명 |
|---|---|
| Entity | Registry Key와 RegistrationRevision, Spawn/Unregister |
| Actor | Scene 객체 수명과 Entity reference 연결 |
| WorldObject | 별도의 문자열 ID와 Store의 지속 데이터 연결 |
| State Machine | Owner의 현재 행동 상태 전이 |

World의 상태는 Actor가 사라져도 Store에 남을 수 있으며 Entity는 Actor Binding을 Unbind해도 Registry에 남습니다. 둘을 조립하는 Scene 전환 순서와 상태 복원, 네트워크 공개 필드·권한·Epoch, 저장 형식은 게임 Host가 결정합니다.

관련 문서: [Foundation](index.md) · [Actor와 Presentation](actor-and-presentation.md) · [Entity Registry 구현](../guides/foundation/create-entity-registry.md) · [Actor Runtime 조립](../guides/foundation/compose-entity-actor-runtime.md) · [WorldObject](world-object.md)
