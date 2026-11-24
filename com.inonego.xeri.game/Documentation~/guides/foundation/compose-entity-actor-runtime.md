# Entity Actor·Presentation Runtime 조립하기

이 가이드는 논리 Entity에 대응하는 **Actor 객체와 Presentation 자원의 수명**을 조립하는 방법을 설명합니다. Actor를 수동 0..N개 관리할지, Registry 등록과 함께 자동 생성할지 선택할 수 있습니다. World 지속 상태·Scene 전환과 Network/Save는 별도 시스템의 책임입니다.

## 구성 요소와 소유권

| 요소 | 소유·수행하는 것 | 소유하지 않는 것 |
|---|---|---|
| `EntitySpawnRegistry` | Entity Key, 등록/해제, 필수 Binding | Scene Actor, Presentation |
| `EntityActorController` | Actor 객체의 생성·반환과 Entity reference 연결 | Entity 등록 수명 |
| `EntityActorRegistry` | Controller가 관리하는 Actor의 조회 관계 | Actor 생성·반환 실행 |
| `EntityPresentationCoordinator` | Provider가 생성한 Presentation의 수명 transaction | Actor 객체의 자체 수명 |
| `EntityActorRuntime` | Actor·Presentation 작업 순서와 실패 정리 | 게임플레이 활성화, Actor Snapshot |
| `EntityActorRuntimeBinding` | Registry Entity마다 자동 생성한 Actor·Presentation | Registry, 수동 Actor |

Entity Key는 Registry 내부의 논리 식별자입니다. Actor·Presentation은 이 Key를 저장·복원하는 별도 상태 저장소가 아닙니다.

## 1. Actor 객체와 Entity 연결

게임 Actor는 `EntityActorBase<TEntity>`를, 생성 정책은 `IEntityActorFactory` 또는 `EntityActorFactory` 파생 클래스를 사용합니다. 다음 예시의 `actorFactory`는 게임이 준비한 Factory입니다.

```csharp
var controller = new EntityActorController<MyActor, MyEntity, MyActorContext>
(
    actorFactory
);
```

Controller는 Actor 객체 수명과 Entity에 연결된 기간을 분리합니다.

| API | Actor 상태 변화 |
|---|---|
| `Create(entity, context)` | 새 Actor를 만들고 Detached로 등록 |
| `Attach(actor, entity)` | Detached → Bound |
| `Detach(actor)` | Bound → Detached; Actor는 계속 유지 |
| `Rebind(actor, nextEntity)` | 동일 Actor의 연결 Entity 변경 |
| `Release(actor, reason)` | Entity 연결 및 Actor 등록 종료, Provider에 반환 |

하나의 Entity에 Actor가 **0..N개** 연결될 수 있습니다. `TryFindSingle`은 정확히 하나일 때만 성공합니다. **수동 `Attach`는 Entity의 Registry 등록 여부를 검사하지 않습니다.** `IsBound`는 참조 관계만 의미하며 Registry 수명의 유효성은 필요할 때 별도로 확인해야 합니다. 독립 Controller가 반환한 Actor는 Entity Registry의 가입·탈퇴에 자동 반응하지 않습니다.

## 2. Presentation Provider와 Coordinator

Presentation Provider는 `IEntityPresentationProvider<TEntityActor,TEntity>`를 구현합니다. Owner 이전이나 Entity Rebind 콜백이 필요 없는 단순 Provider는 `EntityPresentationProviderBase<TEntityActor,TEntity>`를 상속할 수 있습니다.

```csharp
var coordinator = new EntityPresentationCoordinator<MyActor, MyEntity>();
coordinator.AddProvider(visualProvider);
```

Provider는 등록 순서로 Create하고 역순으로 Release합니다. 생성이나 Rebind 중 오류가 나면 시도한 Provider까지 역순으로 정리를 시도합니다.

- `RebindEntity`: 동일 Actor가 참조하는 Entity의 변경을 Presentation에 통지합니다.
- `RebindOwner`: 활성 Presentation의 Actor 소유자를 다른 Actor로 이전합니다.
- 활성 Actor가 남아 있거나 수명 transaction 중이면 `AddProvider` / `RemoveProvider`로 Provider 구성을 변경하지 않습니다.
- 같은 Actor의 Create·Release·Rebind를 Provider 콜백 안에서 중첩 호출할 수 없습니다. 서로 다른 Actor의 수명 작업은 중첩될 수 있으므로 Provider는 상태를 Actor별로 관리합니다.

## 3. EntityActorRuntime으로 일괄 조립

Actor 객체와 Presentation을 항상 함께 구성한다면 Controller와 Coordinator를 `EntityActorRuntime`에 전달합니다.

```csharp
var runtime = new EntityActorRuntime<MyActor, MyEntity, MyActorContext>
(
    controller,
    coordinator
);

var actor = runtime.Create(entity, context);
```

### Create·Rollback·Release

```text
Create:
  Controller 내부 CreateCore → AttachCore → Coordinator 내부 CreateCore

Rollback(actor):
  Coordinator 내부 ReleaseCore(SpawnRollback) → Controller 내부 ReleaseCore(SpawnRollback)

Release(actor, reason):
  Coordinator 내부 ReleaseCore → Controller 내부 ReleaseCore
```

`Create`가 반환한 Actor는 Entity에 연결되어 있지만 게임의 Activate, AI 시작, 저장 상태 반영까지 자동 수행하지 않습니다. 호출자 설정 단계가 완료되기 전에 실패하면 `Rollback(actor)`으로 정리합니다.

Presentation Release가 실패해도 Actor 반환은 계속 시도하며 가능한 오류를 함께 전달합니다. Actor의 Registry 관계는 Pool/Provider 반환 이전에 해제됩니다. 정상 Despawn과 `SpawnRollback`은 서로 다른 종료 의미이므로 게임에서 구분해야 합니다.

### Actor 재연결

`runtime.Rebind(actor, nextEntity)`는 Controller에서 Entity 관계를 변경하고 Coordinator에서 Provider의 Rebind 콜백을 실행합니다. Provider가 실패하면 이미 시도한 Provider의 변경을 역순 정리하고 Controller를 이전 Entity로 복구합니다. 복구까지 실패하면 오류를 함께 전달합니다.

Actor를 Detached로 남기거나 Presentation 없이 운영하려면 **Runtime에 결합되지 않은 독립 Controller**에서 명시적 `Create/Attach/Detach/Rebind/Release`를 사용합니다. 이미 Runtime에 귀속된 동일 Controller에 직접 변경을 호출하면 거절됩니다.

## 4. Registry에 자동 Actor 연결

Registry 등록 수명에 Actor 하나를 자동으로 대응시키려면 `EntityActorRuntimeBinding`을 선택합니다.

```csharp
var binding = new EntityActorRuntimeBinding<MyActor, MyEntity, MyActorContext>(runtime);

// Registry에 이미 있는 Entity와 이후 등록되는 Entity를 함께 동기화한다.
binding.Bind(entityRegistry, (key, entity) => ResolveContext(key, entity));

if (binding.TryFind(entity, out var boundActor))
{
    // Binding이 생성한 Actor만 조회된다.
}

// Entity는 유지하고 자동 Actor·Presentation만 해제한다.
binding.Unbind();
```

`ResolveContext`는 게임이 제공하는 Context 생성 함수입니다. Binding은 `ISpawnRegistryBinding<ulong,TEntity>`로 Registry에 연결됩니다. 등록 시 Actor·Presentation을 만들고, 등록 해제 또는 Unbind 시 해당 자원을 반환합니다.

### Context가 없는 간단한 구성

Context가 필요 없을 때는 `IEntityActorFactory<MyActor, MyEntity, EntityActorNoContext>` 구현체와 Provider를 전달해 한 번에 조립할 수 있습니다. 앞의 `MyActorContext` 기반 Factory와는 **서로 다른 타입의 구성**입니다. 아래의 `gameObjectProvider`는 호출자가 준비한 `IGameObjectProvider`입니다.

```csharp
var noContextActorFactory = new EntityActorFactory<MyActor, MyEntity>(gameObjectProvider);
var binding = EntityActorRuntimeBinding<MyActor, MyEntity>.CreateAndBind
(
    entityRegistry,
    noContextActorFactory,
    visualProvider
);
```

Provider가 필요 없으면 `CreateAndBind(entityRegistry, noContextActorFactory)`를 사용합니다.

| 구성 방식 | 적합한 경우 |
|---|---|
| Controller 직접 사용 | Actor의 Detached / Attach / Rebind / 0..N 수명 직접 제어 |
| Runtime 직접 사용 | Actor와 Presentation을 하나의 수동 transaction으로 제어 |
| RuntimeBinding 사용 | Registry에 등록된 Entity마다 자동 Actor 하나 동기화 |

Binding은 **자동으로 생성한 Actor만** 소유합니다. 같은 Runtime에서 수동으로 만든 Actor, 기존 Entity Registry, Factory 및 Provider는 소유하지 않습니다. 자동 Actor의 Release 실패는 terminal 반환 계약으로 다루며 동일 Actor를 무조건 재시도하지 않습니다. 다만 **활성 Registry transaction 때문에 `DetachBinding`이 시작되지 못한 `Unbind` 실패**는 terminal 반환이 아닙니다. 이 경우 Binding이 계속 연결되어 있으므로 transaction 종료 후 소유자가 해제를 다시 호출해야 합니다.

## 5. 다른 Foundation 시스템과의 경계

Actor Runtime Binding의 `Unbind()`는 **자신이 자동 생성한 Actor·Presentation만 반환**합니다. Entity Registry의 논리 등록 수명은 유지되며, `DespawnAll()`의 결정은 Registry 소유자에게 남습니다. 수동 Actor는 해당 수동 Runtime·Controller 소유자가 별도로 해제합니다.

WorldObject의 지속 데이터와 Scene 객체 연결은 [WorldObject와 지속 상태](../../foundation/world-object.md)에서 설명합니다. Entity Key와 WorldObject ID를 하나의 식별자로 취급하지 않습니다. Scene 조립 시에는 각 Binding 소유자가 자신의 연결·해제와 오류 처리를 수행하며, 전체 호출 순서는 외부 Host가 정합니다.

외부 Key로 이미 준비된 Entity를 게임 Spawn 없이 등록하는 방법은 [Entity Registry 구현](create-entity-registry.md)에서 설명합니다. 네트워크 공개 정책, Save/Restore와 Replay 기능을 Actor Binding이 제공하는 것으로 해석하지 않습니다.

## 관련 문서

- [Entity와 Spawn 계약](../../foundation/entity-and-spawn.md)
- [Actor·Presentation 개별 수명 계약](../../foundation/actor-and-presentation.md)
- [Entity Registry 구현](create-entity-registry.md)
- [WorldObject와 지속 상태](../../foundation/world-object.md)
- [WorldObject 구성 가이드](create-world-object.md)
- [State Machine](../../foundation/state-machine.md)
- [Xeri 소유권과 수명](https://inonego-unity.github.io/Xeri/docs/concepts/ownership-and-lifetime.html)
