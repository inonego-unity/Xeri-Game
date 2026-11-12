# Entity Actor Runtime 조립하기

논리 Entity, Scene Actor, Presentation은 서로 다른 수명으로 관리합니다.
Xeri는 Actor object lifetime, Entity binding lifetime, Presentation transaction을 분리하고 필요한 상위 transaction을 `EntityActorRuntime`으로 조립합니다.

## 책임 경계

```text
EntitySpawnRegistry
└─ logical Entity lifecycle

EntityActorController
├─ EntityActorFactory
├─ EntityActorRegistry
├─ Actor object lifetime
└─ Entity ↔ Actor binding lifetime

EntityPresentationCoordinator
└─ Presentation Provider transaction

EntityActorRuntime
└─ ActorController + PresentationCoordinator transaction
```

`EntityActorRuntime`은 Actor state persistence, gameplay activation, Save, AI, Control을 소유하지 않습니다.

## 1. Actor Controller

프로젝트 Actor는 `EntityActorBase<TEntity>`를 상속하고,
프로젝트 Factory는 `EntityActorFactory<TEntityActor,TEntity,TContext>`를 구현합니다.

```csharp
var actorController =
    new EntityActorController<MyActor, MyEntity, MyActorContext>
    (
        actorFactory
    );
```

Controller는 Actor object lifetime과 Entity binding lifetime을 분리합니다.

```text
Create(entity, context)
→ Detached Actor object 생성

Attach(actor, entity)
→ Detached → Bound

Detach(actor)
→ Bound → Detached

Rebind(actor, nextEntity)
→ Bound owner를 next Entity로 교체

Release(actor, reason)
→ Actor object lifetime 종료
```

`Actors`는 모든 live Actor를 반환하고,
`BoundActors`는 Entity에 연결된 Actor만,
`DetachedActors`는 object lifetime은 살아 있지만 Entity binding이 없는 Actor만 반환합니다.

`TryFindSingleActor(entity, out actor)`는 정확히 한 개의 Bound Actor가 있을 때만 `true`입니다.

## 2. Presentation Coordinator

Presentation Provider는 `IEntityPresentationProvider<TEntityActor,TEntity>`를 구현합니다.

```csharp
var presentationCoordinator =
    new EntityPresentationCoordinator<MyActor, MyEntity>();

presentationCoordinator.Register(myPresentationProvider);
```

Provider lifecycle:

```text
CreateForActor
→ 등록 순서로 create

Release
→ 역순으로 release

Transfer
→ Actor의 Entity owner 변경

TransferOwner
→ Presentation object의 Actor owner 변경
```

create/transfer 도중 실패하면 완료된 Provider를 역순 rollback합니다.

## 3. EntityActorRuntime

```csharp
var actorRuntime =
    new EntityActorRuntime<MyActor, MyEntity, MyActorContext>
    (
        actorController,
        presentationCoordinator
    );
```

Runtime은 Registry나 Factory가 아닙니다.
Actor Controller와 Presentation Coordinator 사이의 transaction 순서만 소유합니다.

## 4. Create

```csharp
var actor = actorRuntime.Create(entity, context);
```

transaction:

```text
EntityActorController.Create
→ EntityActorController.Attach
→ EntityPresentationCoordinator.CreateForActor
→ Actor 반환
```

Presentation 생성이 실패하면 Actor까지 `DespawnReason.SpawnRollback`으로 정리합니다.

`Create`가 반환한 Actor는 Entity에 Bound되어 있지만 프로젝트 gameplay activation까지 자동으로 수행되지는 않습니다.

## 5. project commit

gameplay activation과 persistent state apply는 프로젝트 책임입니다.

```text
EntityActorRuntime.Create
→ project state apply
→ project Activate
→ project commit notification
```

commit 전에 프로젝트 단계가 실패하면:

```csharp
actorRuntime.Rollback(actor);
```

`Rollback`은 `DespawnReason.SpawnRollback`을 사용합니다.
corpse, retention, dissolve 같은 정상 Remove/Dead 정책과 rollback을 같은 의미로 처리하지 않습니다.

## 6. Transfer

같은 Actor instance를 다른 Entity에 연결하면서 Presentation Provider transaction까지 함께 수행하려면:

```csharp
actorRuntime.Transfer(actor, nextEntity);
```

transaction:

```text
EntityActorController.Rebind
→ EntityPresentationCoordinator.Transfer
→ 성공

Presentation Transfer 실패
→ EntityActorController.Rebind(previousEntity)
→ Provider rollback 결과 전달
```

persistent Actor state ownership, gameplay active state, Party/Control 정책은 프로젝트가 이 transaction 바깥에서 조립합니다.

## 7. Release

```csharp
actorRuntime.Release(actor, reason);
```

순서:

```text
EntityPresentationCoordinator.Release
→ EntityActorController.Release
   → Entity binding 종료
   → Actor Registry 제거
   → Actor Cleanup
   → Pool Release
```

Presentation Release가 실패해도 Actor 반환은 계속 시도합니다.
여러 단계가 실패하면 오류를 aggregate하여 caller에 전달합니다.

## 8. Detached Actor

Actor object를 유지하면서 Entity relation만 종료해야 하면 Controller의 explicit binding API를 사용합니다.

```text
Bound Actor
→ EntityActorController.Detach
→ Detached Actor

Detached Actor
→ EntityActorController.Attach
→ Bound Actor
```

Detached Actor의 persistence owner, gameplay activation 복원, timeout 정책은 프로젝트 책임입니다.

Presentation Provider와 함께 owner migration이 필요한 경우 raw `Attach/Detach`를 임의 조합하기보다 프로젝트 상위 transaction에서 rollback까지 함께 정의합니다.

## 9. persistence

Save/Load가 필요한 프로젝트는 Actor state를 프로젝트 타입으로 유지합니다.

```text
Xeri
EntityActorRuntime
= live Actor + Presentation transaction

Project
ActorState
= persistent snapshot
```

예:

```text
Restore
EntityActorRuntime.Create
→ ApplyState
→ Activate
→ gameplay Spawn notification은 재생하지 않음

Dematerialize
ToState
→ EntityActorRuntime.Release
→ persistent ActorState 유지

Remove
persistent ActorState 제거
→ EntityActorRuntime.Release
```

Xeri Actor runtime에 프로젝트-specific continuity state를 넣지 않습니다.

## 10. detached Presentation

Actor gameplay lifetime이 끝난 뒤 visual lifetime만 더 필요한 경우 detached Presentation은 프로젝트 Presentation owner가 관리합니다.

Downed/Knockdown처럼 위치, 충돌, 피격, revive 등 gameplay 참여가 계속되는 상태는 Actor를 release하지 않습니다.

```text
Alive / Downed
Entity → live Actor → bound Presentation

Terminal Actor lifetime 종료
Actor release → 필요하면 detached Presentation residual
```

## 관련 문서

- [Entity와 Spawn 수명](../../foundation/entity-and-spawn.md)
- [Entity Registry 구현하기](create-entity-registry.md)
- [소유권과 수명](https://inonego-unity.github.io/Xeri/docs/concepts/ownership-and-lifetime.html)
- [Xeri 통합 패턴](https://inonego-unity.github.io/Xeri/docs/concepts/integration-patterns.html)
