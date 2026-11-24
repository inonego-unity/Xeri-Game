# Actor와 Presentation 수명

**Entity는 논리 객체이고 Actor는 Unity 표현 객체입니다.** Presentation은 Actor와 함께 사용되는 시각·부가 표현 자원이며, Actor와 동일한 객체 수명을 가질 필요는 없습니다. 이들은 서로 다른 소유자의 작업으로 구성합니다.

## 1. 구성 요소와 소유권

| 요소 | 주 책임 | 소유하지 않는 것 |
| --- | --- | --- |
| `EntityActorBase<TEntity>` | `MonoBehaviour` Actor의 Entity 참조와 Bind/Unbind 훅 | Entity 등록, 객체 획득·반환 |
| `IEntityActorFactory` / `EntityActorFactory` | `IGameObjectProvider` 획득·준비·원래 Provider로 terminal 반환 | Entity와 Actor의 등록 관계 |
| `EntityActorRegistry<TEntityActor,TEntity>` | Actor 객체 membership과 Entity 참조 기반 0..N 관계 | GameObject 풀, Game Domain 상태 |
| `EntityActorController` | Actor 객체 수명과 `Attach/Detach/Rebind` 관계 수명 | 자동 Registry 구독·Presentation 생성 |
| `EntityPresentationRegistry<TOwner,TPresentation>` | Presentation 객체 membership과 Owner 참조 기반 0..N 관계 | 실제 자원 생성·풀링 |
| `IEntityPresentationProvider` | Actor별 한 종류의 Presentation 획득·갱신·반환 | Registry/Actor 전체 수명 |
| `EntityPresentationCoordinator` | Provider의 생성·Rebind·Rollback·Release 순서 | 구체 Presentation의 저장·생성 방식 |
| `EntityActorRuntime` | Controller + Coordinator의 **수동 Actor·Presentation 트랜잭션** | 게임 활성화·Actor 영속 상태 |
| `EntityActorRuntimeBinding` | Registry 등록 수명을 Runtime의 Actor·Presentation과 선택적으로 동기화 | Registry·외부 Factory·Provider 소유권 |

각 요소는 별도로 사용할 수 있습니다. 모든 Entity에 Actor를 만들거나 모든 Actor에 Presentation Provider를 붙일 필요는 없습니다.

## 2. Actor 객체와 Entity 관계

`EntityActorBase<TEntity>`는 `Entity`, `IsBound`, `HasKey`, `Key`를 읽기 전용으로 노출합니다. Entity 관계가 없으면 **Detached**, 관계가 있으면 **Bound**입니다. `Awake`와 `OnEnable`에서는 아직 Entity가 연결돼 있지 않을 수 있으므로 Entity 의존 초기화는 `OnBindEntity` 훅에, 관계 정리는 `OnUnbindEntity` 훅에 둡니다.

**`IsBound`는 Entity 참조가 연결됐다는 뜻이지, 특정 `EntitySpawnRegistry`에 등록돼 있거나 현재 `Spawned`인지를 검증한 결과가 아닙니다.** 수동 `Attach`는 Entity의 Registry membership을 검사하지 않습니다. 등록 수명에 대한 보증이 필요하면 Registry 소유자가 `TryResolve(EntityHandle, out entity)` 같은 별도 검증을 수행해야 합니다. `Key` 역시 연결된 Entity에 Key가 없으면 예외를 던집니다.

| Controller API | 결과 |
| --- | --- |
| `Create(entity, context)` | Factory가 Actor 객체를 준비하고 **Detached**로 등록 |
| `Attach(actor, entity)` | 등록된 Detached Actor를 해당 Entity에 연결 |
| `Detach(actor)` | Bound 관계를 종료하고 Actor 객체는 유지 |
| `Rebind(actor, nextEntity)` | 객체를 반환하지 않고 현재 Entity 관계를 새 대상으로 이전 |
| `Release(actor, reason)` | 연결을 종료하고 Actor를 Factory에 terminal 반환 |
| `ReleaseAll(entity, reason)` / `ReleaseAll(reason)` | 호출 시점의 대상 Actor 수명만 반환 |

Entity와 Actor 관계는 1:1이 아닙니다. 한 Entity에 Actor 0..N개를 연결할 수 있습니다. `FindAll(entity)`로 관련 Actor를 조회하고 `TryFindSingle`은 정확히 한 개가 연결된 경우에만 성공합니다. `Actors`는 객체 수명 전체, `BoundActors`는 연결된 객체, `DetachedActors`는 분리됐으나 살아 있는 객체를 나타냅니다.

`EntityActorRegistry`의 변경은 해당 Controller가 독점합니다. 외부에는 조회 경계만 제공하며 동일 Registry를 두 Controller가 공동 소유하지 않습니다. **Controller는 Entity Registry에 자동 Bind하지 않습니다.** Registry 기반 자동 구성은 아래 Runtime Binding의 역할입니다.

## 3. Factory와 Pool 반환

Factory는 Entity와 생성 Context를 받아 GameObject Provider를 결정하고 Actor 컴포넌트를 준비합니다. Entity 연결 자체는 Controller가 합니다. 요청마다 다른 Provider를 선택하더라도 **획득 출처를 기억했다가 그 Provider로 반환**해야 합니다.

`PrepareActor`에서 실패하면 부분 자원 정리와 Provider 반환을 시도합니다. `Release`는 Cleanup 실패가 있어도 Provider 반환을 시도하고, 오류를 집계합니다. **반환은 terminal 시도**이므로 실패한 같은 수명을 무작정 다시 반환하지 않습니다.

`IEntityActorFactory` 구현은 **`Release` 호출이 끝나기 전에 같은 Actor 인스턴스를 다음 `Create` 결과로 재발급해서는 안 됩니다.** Controller의 `ReleaseAll`은 등록 serial을 확인해, 종료 작업 중 Pool이 다시 제공한 **새 수명**까지 반환하지 않도록 구별합니다.

## 4. Presentation 객체와 Provider

`EntityPresentationRegistry`는 Presentation 객체의 Owner 관계와 객체 등록 수명을 분리해 추적합니다. `Register(presentation)`로 Detached 객체를 등록하고 `Attach(owner, presentation)`/`Detach(presentation)`/`Rebind(presentation, nextOwner)`로 관계를 변경하며, **Detached가 된 뒤에만** `Unregister(presentation)`할 수 있습니다. 하나의 Owner에 Presentation 0..N개가 연결될 수 있습니다. `Presentations`·`BoundPresentations`·`DetachedPresentations`는 현재 관계의 조회 경계이며, **Registry 자체는 시각 자원을 획득·반환하지 않습니다.**

`FindAll(owner)`는 해당 Owner에 연결된 Presentation 전체를 조회합니다. `TryFindSingle<TConcrete>(owner, out presentation)`은 구체 타입에 맞는 항목이 없으면 `false`, 정확히 하나면 `true`이며, **둘 이상이면 예외**를 던집니다. Actor Controller의 비제네릭 `TryFindSingle`과 중복 처리 규칙이 다릅니다. **Registry의 `Rebind`는 Owner 조회 관계를 바꿀 뿐, 구체 Presentation 자원을 이전하거나 Provider rollback을 수행하지 않습니다.** 실제 표현 자원의 이전은 해당 Provider/Coordinator 계약을 통해 별도로 처리합니다.

`OnAttach`·`OnDetach`·`OnClear`는 관계 변경을 확정한 뒤 알립니다. 구독자 오류는 개별적으로 로그에 남기고 다른 구독자의 호출을 계속합니다. **같은 Presentation Registry에서 알림 도중 객체·관계를 중첩 변경하는 것은 거절**합니다. 이는 다음 Coordinator의 *같은 Actor 단위* 재진입 제한과 구별되는 **Registry 전체 알림 경계**입니다.

`IEntityPresentationProvider<TEntityActor,TEntity>`는 Actor에 대응하는 특정 Presentation 자원의 작업을 제공합니다.

| Callback | 책임 |
| --- | --- |
| `OnActorCreated` | Actor 수명에 필요한 자원 연결 |
| `OnActorReleasing` | 정상 종료 또는 부분 생성 실패의 자원 정리 |
| `OnActorRebound` / `OnActorRebindRollback` | 동일 Actor가 다른 Entity로 이동할 때 적용·복원 |
| `OnPresentationOwnerRebound` / `OnPresentationOwnerRebindRollback` | 같은 Presentation 종류의 Owner Actor 이전·복원 |

Rebind 작업이 불필요한 Provider는 `EntityPresentationProviderBase`를 상속해 생성·반환만 구현할 수 있습니다. 실제 Pool·표현 콘텐츠·부분 실패 시 자원 상태는 각 Provider가 소유합니다.

서버 확정 전의 예상 표현, 거절 시 취소·정정, Teleport 같은 불연속 구간의 보간 초기화는 Foundation이 별도 계약으로 두지 않습니다. 이런 동작이 필요한 소비처는 해당 Provider가 표현 상태로 소유하며, 권한 상태와 표시 상태를 섞지 않도록 Provider 안에서 구분합니다.

`EntityPresentationCoordinator`는 Provider를 `AddProvider` 순서로 생성하고 역순으로 반환합니다. 활성 Actor가 남아 있거나 트랜잭션 중일 때 Provider 목록 변경은 거절합니다. `Providers`는 읽기 전용입니다. **Coordinator는 `EntityPresentationRegistry`를 자동으로 생성·소유·갱신하지 않습니다.** 관계 조회 Registry가 필요한 Provider가 해당 연결 정책과 자원의 반환을 별도로 책임집니다. 같은 Actor의 중첩 수명 작업은 거절하지만 **다른 Actor의 콜백은 중첩될 수 있어** Provider 구현은 Actor별 상태를 분리해야 합니다.

## 5. Actor Runtime: 수동 조합

`EntityActorRuntime<TEntityActor,TEntity,TContext>`은 Controller와 Coordinator를 한 번에 결합해 Actor 생성·Entity 연결·Presentation 구성·해제를 하나의 작업으로 만듭니다.

`Create(entity, context)`: Factory로 Detached Actor 획득 → Entity Attach → Provider 구성. 중간 실패는 이미 획득한 Actor/Provider를 역순 정리합니다.

`Rebind(actor, nextEntity)`: Actor의 Entity 관계와 Provider 관계를 함께 갱신하며, Presentation 실패 시 이전 Entity로의 복구를 시도합니다.

`Release(actor, reason)` / `Rollback(actor)`: Presentation → Actor 순서로 반환합니다. `Rollback(actor)`은 **수동 Actor에 `SpawnRollback` 사유로 Release하는 API**이며, 자동으로 과거 게임 데이터를 되돌리는 Snapshot 롤백이 아닙니다. 일부 Release가 실패해도 다른 소유 자원 정리를 시도하고, 시작된 수명은 terminal 종료합니다.

Runtime에 결합된 Controller와 Coordinator의 **독립 객체 수명 조작은 거절**됩니다. Actor 생성·이전·반환은 해당 Runtime을 통해 수행합니다. 기존 Actor 수명이 남아 있는 Controller를 나중에 Runtime에 결합할 수도 없습니다.

## 6. EntityActorRuntimeBinding: 자동 조합

`EntityActorRuntimeBinding<TEntityActor,TEntity,TContext>`은 Registry의 `ISpawnRegistryBinding<ulong,TEntity>` 참가자입니다. `Bind(registry, contextResolver)`를 호출하면 **현재 등록된 Entity와 이후 등록되는 Entity**에 대해 Runtime Actor·Presentation을 구성합니다. Registry 등록 종료나 `Unbind()` 시 해당 Binding이 생성한 Actor만 반환합니다.

- 기본 구성은 Entity당 **해당 Binding 소유의 Actor 한 개**입니다. 기존 Runtime에서 명시적으로 만든 Actor와 소유권이 섞이지 않습니다.
- `TryFind(entity, out actor)`는 이 Binding이 자동 구성한 Actor만 조회합니다.
- `BoundRegistry`는 현재 결합된 Registry를 보여 줍니다.
- 자동 소유 Actor는 수동 `Rebind`/`Release`의 대상이 아니며 해당 Registry 수명 경로에서 종료합니다.
- Context가 필요 없으면 `EntityActorRuntimeBinding<TEntityActor,TEntity>.CreateAndBind(registry, actorFactory, providers...)`로 Factory와 0개 이상의 Provider를 조립할 수 있습니다. Provider가 0개여도 자동 Actor는 생성됩니다.
- `Unbind`는 **Registry Entity를 Despawn하지 않고**, 외부 Factory·Provider나 별도의 수동 Actor도 반환하지 않습니다. 이후 같은 Binding을 다시 Bind할 수 있습니다.

`Bind` 중 현재 등록 Entity에 자원을 붙이다 실패하면 Registry의 역순 rollback과 Binding 소유 잔여 Actor 정리를 시도하고 오류를 전달합니다. `Unbind`는 **Registry의 `DetachBinding`이 완료되었거나 terminal로 해제된 경우**에 소유 Actor의 잔여 정리를 계속합니다. 반대로 Registry transaction 중 호출 등으로 **해제 자체가 거절되어 Binding이 아직 등록돼 있다면** `Unbind`는 그 관계를 유지한 채 예외를 전달합니다. 같은 수명 작업에 재진입하는 것도 거절됩니다.

## 7. 수명 간 독립성

| 사건 | Entity | Actor·Presentation |
| --- | --- | --- |
| Entity Spawn/직접 등록 | 논리 Registry 수명 시작 | 자동 Binding이 붙어 있으면 표현 구성 |
| Actor Detach/Release | 논리 Registry 관계 유지 | 관계만 분리하거나 객체 반환 |
| Registry Binding `Unbind` | Entity 등록 유지 | 그 Binding 소유 자동 Actor·Presentation만 반환 |
| Entity Despawn/직접 등록 해제 | 해당 Registry 수명 종료 | 연결된 필수 Binding이 소유한 표현 정리 |
| 새로운 Actor 수명 | 기존 Entity 유지 가능 | Pool의 동일 인스턴스라도 새 등록 serial/entry로 구별 |

Actor의 지속 상태, 실제 게임플레이 활성 여부, Scene의 영속 State, 저장·네트워크 표현은 소비 Domain의 별도 계약입니다. 표현을 제거했다고 해당 논리 객체를 삭제하거나 HP/Ability 수명을 종료하지 않습니다.

## 관련 문서

- [Entity와 Spawn Registry](entity-and-spawn.md)
- [Entity Actor Runtime 조립 가이드](../guides/foundation/compose-entity-actor-runtime.md)
- [Entity Registry 구현 가이드](../guides/foundation/create-entity-registry.md)
