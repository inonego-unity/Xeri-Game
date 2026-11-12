# Entity Registry 구현하기

이 가이드는 프로젝트의 논리 Entity를 Xeri `EntitySpawnRegistry`에 연결하는 기본 구조를 설명합니다.

## 1. Entity 타입 정의

Entity Core에 HP나 Group은 필수가 아닙니다. 가장 작은 Entity는 identity와 lifecycle만 가집니다.

```csharp
using System;
using inonego.Xeri.Game;

[Serializable]
public sealed class SampleEntity : EntityBase
{
    // NONE
}
```

HP 또는 Group이 필요한 concrete Entity만 해당 capability를 구현합니다.
예를 들어 HP consumer에게 노출해야 한다면 `IReadOnlyHPOwner<T>` 또는 `IHPOwner<T>`를 구현하고 기존 HP 객체를 반환합니다.

## 2. Registry 구현

프로젝트가 실제 Entity를 어떻게 획득하고 반환하는지만 구현합니다.

```csharp
[Serializable]
public sealed class SampleEntityRegistry : EntitySpawnRegistry<SampleEntity>
{
    protected override SampleEntity Acquire()
    {
        return new SampleEntity();
    }

    protected override void Release(SampleEntity entity, DespawnReason reason)
    {
        // Pool 반환이나 외부 resource 정리를 수행한다.
    }
}
```

Registry가 Key와 `SpawnState` 전이를 관리하므로 `Acquire()`에서 임의 Key나 Spawn 상태를 설정하지 않습니다.
기본 `IncreKeyGenerator`는 0도 정상 Key로 발급할 수 있으므로 **0을 Entity 없음 sentinel로 사용하지 않습니다.**

## 3. Spawn과 조회

```csharp
var registry = new SampleEntityRegistry();

if (registry.TrySpawn(out SampleEntity entity))
{
    ulong key = entity.Key;
    SampleEntity same = registry.Find(key);
}
```

optional Entity 관계가 필요하면 별도 bool/nullable/handle 등으로 존재 여부를 표현합니다.

## 4. Required lifecycle binding

Entity Spawn/Despawn과 반드시 함께 성공해야 하는 runtime participant는 `ISpawnRegistryBinding<TKey,T>`으로 연결합니다.
여러 binding은 Spawn 시 정순, Despawn/rollback 시 역순으로 처리됩니다.
HP 종료 상태를 Entity Despawn으로 연결하는 게임별 정책도 Core base type에 넣기보다 이런 runtime composition 경계에서 연결할 수 있습니다.

## 5. Actor가 필요한 경우

논리 Entity와 Scene Actor lifecycle을 분리합니다.

```text
EntitySpawnRegistry
└─ Entity

EntityActorController
└─ Entity reference → Actor 0..N
```

Actor object lifetime과 Entity binding lifetime을 직접 관리할 때는 `EntityActorController`를 사용합니다.

```text
Create(entity, context)
→ Detached Actor 생성

Attach(actor, entity)
→ Entity binding 생성

Detach(actor)
→ Entity binding 종료, Actor object 유지

Rebind(actor, nextEntity)
→ 동일 Actor의 Entity owner 교체

Release(actor, reason)
→ Actor object lifetime 종료
```

`TryFindSingleActor`는 정확히 하나의 Bound Actor가 있을 때만 성공하며 0개 또는 복수이면 `false`입니다.

Presentation Provider까지 같은 transaction으로 조립해야 하면 `EntityActorRuntime`을 사용합니다.

```text
EntityActorRuntime.Create
→ EntityActorController.Create
→ EntityActorController.Attach
→ EntityPresentationCoordinator.CreateForActor

commit 전 project setup 실패
→ EntityActorRuntime.Rollback

Entity owner migration
→ EntityActorRuntime.Transfer

terminal release
→ EntityPresentationCoordinator.Release
→ EntityActorController.Release
```

Actor를 Pool/Provider로 반환하기 전에 Entity binding과 Actor Registry relation이 종료됩니다.
Save state, gameplay Activate, AI/Control 정책은 프로젝트 책임으로 남깁니다.

선택적 Entity Registry binding은 Entity당 자동 Actor 하나를 동기화하는 편의 정책일 뿐입니다.
Entity → Actor 0..N이 필요한 프로젝트는 explicit Actor lifecycle을 사용합니다.

## 6. Despawn

```csharp
entity.Despawn();
```

Despawn은 required binding과 Registry membership을 terminal 정리한 뒤 획득 출처로 객체를 반환합니다.
committed observer notification은 transaction 완료 이후 경계입니다.

## 관련 문서

- [Entity와 Spawn 수명](../../foundation/entity-and-spawn.md)
- [HP](../../modules/combat/hp.md)
- [Entity Actor Runtime 조립하기](compose-entity-actor-runtime.md)
- [Object Pooling](https://inonego-unity.github.io/Xeri/docs/modules/utility/pooling.html)
- [소유권과 수명](https://inonego-unity.github.io/Xeri/docs/concepts/ownership-and-lifetime.html)
