# Entity Registry 구현하기

이 가이드는 논리 Entity의 Key 및 Spawn/Despawn 수명을 Registry에 위임하는 가장 작은 구성을 다룹니다. Scene Actor와 World 지속 데이터는 Entity 등록 자체의 필수 조건이 아닙니다.

## 1. Entity 타입 정의

```csharp
using System;

using inonego.Xeri.Game;

[Serializable]
public sealed class SampleEntity : EntityBase
{
    // NONE
}
```

`EntityBase`는 Key, `SpawnState`, `RegistrationGeneration`, 수명 훅을 가집니다. HP·Group 등이 필요하다면 구체 Entity가 별도 capability를 구현합니다.

## 2. Registry의 획득·반환 정책

```csharp
[Serializable]
public sealed class SampleEntityRegistry : EntitySpawnRegistry<SampleEntity>
{
    protected override SampleEntity Acquire()
    {
        return new SampleEntity();
    }
}
```

게임은 획득 출처를 결정하고 Registry는 Key 발급, membership, 상태 전이 및 실패 시 정리를 관리합니다. `Acquire()`는 미등록 Entity를 제공해야 합니다. 기본 `Release(entity, reason)`는 아무 일도 하지 않으므로, **Pool이나 외부 자원 반환이 필요한 Registry만 재정의**합니다. 기본 Key 생성기는 `0`도 유효한 Key로 생성할 수 있습니다.

## 3. 로컬 Spawn과 조회

```csharp
var registry = new SampleEntityRegistry();

if (registry.TrySpawn(out var entity))
{
    var key = entity.Key;
    var same = registry.Find(key);
    var handle = new EntityHandle(entity);

    if (registry.TryResolve(handle, out var current))
    {
        // 현재 등록 수명에 속한 Entity를 사용한다.
    }
}
```

`TrySpawn`이 `false`이면 `Acquire()`가 null을 반환한 것입니다. Key 중복이나 등록 수명 계약 위반은 예외로 보고합니다. `Find(key)`는 Key만 조회하고 `TryResolve`는 객체·Key·Revision 및 해당 Registry의 현재 membership을 함께 검증합니다.

## 4. 외부에서 준비된 Entity의 등록

게임 Spawn 훅을 실행하는 등록과, **호출자가 상태와 Key를 이미 구성한 Entity의 Registry membership만 확정**하는 직접 등록을 구분합니다. 직접 등록은 Save/Replica의 값 적용이나 권한·공개 범위 판단을 수행하지 않습니다. **`SpawnWithKey`는 외부 객체를 받더라도 게임 Spawn 경로**이므로 나중에 Despawn하면 Registry의 `Release(entity, reason)`가 호출됩니다.

```csharp
// 게임 Spawn 수명이 필요할 때:
registry.SpawnWithKey(entity, suppliedKey);

// 게임 Spawn 훅 없이 현재 객체만 등록할 때:
registry.RegisterWithKey(replica, disclosedKey);

// 직접 등록된 객체의 membership을 종료할 때:
registry.Unregister(replica);
```

`entity`, `replica`, `suppliedKey`, `disclosedKey`는 게임이 준비한 값입니다. `RegisterWithKey`에도 Registry 필수 Binding은 동일하게 적용됩니다. 직접 등록한 객체는 Registry가 획득한 객체가 아니므로 게임 Spawn용 `Release`를 호출하지 않습니다. 외부 Key와 로컬 Key 발급은 충돌하지 않게 관리합니다.

## 5. 등록과 함께 생성되는 자원 연결

등록된 Entity마다 필수 자원을 만들거나 해제해야 하면 `ISpawnRegistryBinding<TKey,T>`를 구현하고 `registry.AttachBinding(binding)`으로 연결합니다.

- `OnRegistered(key, entity)`는 등록 시도 중 필수 자원을 구성합니다.
- `OnUnregistering(key, entity, reason)`은 종료 또는 롤백 시 자원을 반환합니다.
- 여러 Binding은 등록 순서로 구성하고 역순으로 정리합니다.
- `AttachBinding`은 이미 등록된 Entity에도 동기화합니다. `DetachBinding`은 해당 Binding 자원만 정리합니다.

실제 Actor/Presentation 자동 연결은 이미 제공되는 `EntityActorRuntimeBinding`을 사용하면 됩니다. 게임이 직접 Actor를 0..N개 관리하려면 Controller·Runtime의 수동 API를 사용합니다.

## 6. Despawn과 Scene 경계

```csharp
entity.Despawn();
```

게임 Spawn 수명의 종료는 Registry 등록과 Binding 자원을 정리한 뒤 `Release(entity, reason)`를 호출합니다. `Acquire()`로 획득한 객체뿐 아니라 `SpawnWithKey`로 시작한 객체에도 동일하게 적용되므로 **반환 출처 정책을 Registry 구현에서 맞춰야 합니다.** 직접 등록 수명은 `Unregister`로 종료하며 `Release`를 실행하지 않습니다.

Scene 전환 시 Entity를 유지하고 Actor만 없애려면 Entity를 Despawn하지 말고 Actor Binding을 Unbind합니다. WorldObject의 지속 데이터는 별도의 Store를 사용합니다.

관련 문서: [Entity와 Spawn 계약](../../foundation/entity-and-spawn.md) · [Actor·Presentation 수명](../../foundation/actor-and-presentation.md) · [Actor Runtime 조립](compose-entity-actor-runtime.md) · [WorldObject](../../foundation/world-object.md)
