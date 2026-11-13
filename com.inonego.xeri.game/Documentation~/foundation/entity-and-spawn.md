# Entity와 Spawn 수명

Xeri Game의 Entity 기반은 **identity와 Spawn lifecycle**만 소유합니다.
`EntitySpawnRegistry`가 Key 발급·등록·rollback을 관리하고, `EntityBase`는 Key·`SpawnState`·로컬 등록 `RegistrationRevision`·lifecycle hook을 제공합니다.
HP, Group, Actor, Presentation과 사망 정책은 Entity 기반의 필수 책임이 아닙니다.

## 언제 사용하는가

- Runtime 객체에 안정적인 Entity Key와 중앙 조회가 필요할 때
- Spawn/Despawn transaction과 rollback 경계를 통일할 때
- 논리 Entity와 Scene Actor 수명을 분리할 때
- required lifecycle participant를 Registry transaction에 포함해야 할 때

단순 Scene 참조만 필요한 객체라면 별도 Entity Registry가 필요하지 않을 수 있습니다.

## 기본 사용

프로젝트는 `EntityBase` 파생 타입과 `EntitySpawnRegistry<TEntity>` 파생 Registry를 만들고 획득/반환 정책만 정의합니다.

```csharp
[Serializable]
public sealed class SampleEntity : EntityBase
{
    // NONE
}
```
## 핵심 계약

| 계약 | 역할 |
|---|---|
| `IReadOnlyEntity` | Key, HasKey, SpawnState 및 로컬 RegistrationRevision 읽기 |
| `IEntity` | Registry가 Key와 Spawn 상태를 관리하는 내부 계약 |
| `ISpawnRegistry<TKey,T>` | 등록 객체 조회와 committed lifecycle notification |
| `ISpawnRegistryBinding<TKey,T>` | Spawn transaction에 포함되는 required binding |
| `EntitySpawnRegistry` | Entity용 Key 생성과 Spawn/Despawn transaction |
| `EntityBase` | Registry별 Key, 로컬 등록 Revision, Entity lifecycle hook |
| `DespawnReason` | 종료 원인 전달 |

HP와 Group이 필요한 Entity는 `IReadOnlyHPOwner/IHPOwner`, `IReadOnlyGroupMember/IGroupMember` capability를 조합합니다.

## Identity와 등록 수명

`Entity.Key`는 **등록된 Registry 안에서만** 유일합니다. 같은 Key를 가진 Entity가 다른 Registry에 동시에 존재할 수 있습니다. Entity의 Key와 Spawn 상태는 Registry가 관리합니다. `EntityBase` 파생형은 `protected internal virtual SetKey/ClearKey`를 확장할 수 있지만, Registry의 발급 Key·등록 상태 불변식을 유지해야 합니다. `EntityHandle`은 C# 객체 참조·Key·`RegistrationRevision`을 보존하는 로컬 비소유 Handle이며 직렬화용 형식이 아닙니다. Entity가 Despawn된 뒤 **동일 인스턴스가 같은 Key로 다시 등록돼도** 이전 Handle은 다시 유효해지지 않습니다.

`RegistrationRevision`은 Entity가 `Spawned` 상태로 진입할 때마다 증가하는 **비직렬화 로컬 수명 번호**입니다. 필수 Binding이나 완료 훅의 실패로 해당 등록이 롤백돼도 이미 증가한 Revision은 유지됩니다. 다른 프로세스·Registry·Replay를 통틀어 식별하는 Network/Persistent ID가 아니므로 RPC나 저장 데이터의 Entity ID로 사용하지 않습니다.

외부에서 확정한 Key를 지정해 등록할 때는 `EntitySpawnRegistryBase<TEntity>.SpawnWithKey(entity, key)`를 사용합니다. 객체에 이미 확정된 Key가 있다면 공통 `SpawnRegistryBase<TKey, T>.SpawnExisting(entity)` 경로를 그대로 사용할 수 있습니다. 이미 등록된 Key를 다시 가져오면 예외가 발생하며 기존 등록은 유지됩니다. 새 로컬 Spawn에 KeyGenerator가 이미 등록된 Key를 발급해도 예외를 발생시키고 다른 Key로 자동 보정하지 않습니다. 외부 Key와 로컬 발급 Key를 함께 사용하는 Host는 **Key 범위를 분리하거나 충돌을 방지하는 생성기 정책**을 선택해야 합니다. 로컬 Key 발급 이전에 획득한 Entity가 이미 Key를 가지고 있어 등록이 거절되면 해당 Key를 변경하지 않습니다. 공통 Spawn transaction에 진입한 뒤의 실패 정리는 `SpawnRegistry`가 담당합니다. Key 할당 훅에서 부분 할당 후 실패하면 이번 발급 시도에서 생성한 Key만 롤백합니다. `ClearKey()`는 정상 Despawn과 Spawn rollback에 호출되며, 훅에서 예외가 발생해도 `EntityBase`의 기본 Key는 해제됩니다.

`TrySpawn()`은 `Acquire()`가 객체를 제공하지 않아 `null`을 반환한 경우에 `false`를 반환합니다. 이 동작은 일반 `SpawnRegistry`와 Entity 전용 Registry에 공통으로 적용됩니다. Key 충돌이나 등록 계약 위반은 예외로 알립니다.

`IWorldObject.ID`는 Scene/Persistence 상태를 연결하는 별도 문자열 ID이며, Entity Key나 Actor·NetworkObject 식별자와 자동 대응되지 않습니다.

## Spawn transaction

```text
Acquire
→ Key 확정
→ Spawning
→ Registry 등록 + RegistrationRevision 갱신 + runtime 연결
→ required bindings OnSpawned (등록 순서)
→ Entity OnSpawned
→ COMMIT
→ Registry OnSpawned notification
```

required binding 실패는 이미 실행된 binding을 역순으로 rollback합니다.
commit 이후 observer 실패는 이미 확정된 Registry ownership을 되돌리지 않습니다.
## Despawn transaction

```text
Despawning
→ Entity OnDespawning
→ required bindings OnDespawning (역순)
→ Registry 내부 runtime 연결 해제
→ Registry membership 제거
→ Entity OnDespawned
→ Despawned
→ Registry OnDespawned notification
→ 획득 출처로 반환
```

정리 단계 일부가 실패해도 가능한 terminal cleanup은 계속 시도합니다.

## 직렬화 후 복원

`SpawnRegistry.OnAfterDeserialize()`는 serialized membership을 기준으로 `SpawnState`, `DespawnFromRegistry` 등 Registry 내부 runtime 연결만 self-rehydrate합니다. 외부 Binding과 공개 `OnSpawned` 이벤트는 재생하지 않습니다.

`AttachBinding(binding)`은 **현재 등록된 모든 객체**에 `ISpawnRegistryBinding.OnSpawned`를 동기화한 뒤, 이후 Spawn에도 그 Binding을 적용합니다. 연결 도중 실패하면 이미 호출한 객체를 역순으로 `SpawnRollback` 정리하고 해당 Binding을 등록하지 않습니다. `DetachBinding(binding)`은 현재 객체의 Binding 자원을 역순으로 `RegistryCleanup` 정리하지만, Entity의 Registry membership 자체는 변경하지 않습니다.

`EntityActorController.Bind(registry)`는 이 Binding 계약을 사용하여 복원된 Entity에 Actor를 연결합니다. 이 과정은 현재 상태의 Registry-Actor 관계를 재구성할 뿐, 과거의 Live Spawn 이벤트나 게임플레이 부작용을 재생하는 일반적인 Restore 기능은 아닙니다.

## Actor 수명

Scene에 materialize되는 객체는 `EntityActorBase<TEntity>`와 `EntityActorController`를 사용합니다.
`EntityActorRegistry`는 **Entity reference → Actor 0..N** live relation만 관리합니다.
Controller의 명시적 수명 API는 같은 Entity에 여러 Actor를 허용합니다.
`TryFindSingleActor`는 해당 Entity의 live Actor가 정확히 하나일 때만 `true`이며,
0개이거나 복수이면 `false`를 반환합니다.
선택적으로 Entity Registry에 Bind하면 Entity당 자동 Actor 하나를 동기화하는 별도 편의 정책을 사용할 수 있습니다.

`EntityActorRuntime<TEntityActor,TEntity,TContext>`은 기존 `EntityActorController`와
`EntityPresentationCoordinator`를 조합해 Actor create/release와 두 subsystem 사이의 rollback을 처리합니다.

```text
Create
→ ActorController.Spawn
→ PresentationCoordinator.Spawn

Rollback
→ DespawnReason.SpawnRollback으로 Presentation + Actor 정리

Release
→ PresentationCoordinator.Release
→ ActorController.Despawn
```

`Rollback(actor)`은 commit 전 실패한 transaction을 `DespawnReason.SpawnRollback`으로 정리합니다.
Runtime은 Actor state persistence, gameplay activation, Save, AI/Control 정책을 소유하지 않습니다.

Actor 반환 시 live Registry relation은 Pool/Provider 반환보다 먼저 제거되어야 합니다.

## Presentation 수명

`EntityPresentationRegistry<TOwner, TPresentation>`는 owner reference 기준 0..N Presentation lookup을 제공합니다.
`EntityPresentationCoordinator<TEntityActor,TEntity>`는 Provider를 등록 순서로 Spawn하고 역순으로 Release/rollback합니다.
Presentation의 실제 Pool, detached residual 수명과 구체적인 표현 정책은 consumer가 소유합니다.

## HP와 lifetime policy

HP의 Alive/Dead 상태는 Entity lifecycle과 별도 계약입니다.
Xeri Game의 Entity 기반은 HP Dead를 자동 Despawn으로 연결하지 않습니다.
필요한 게임은 `ISpawnRegistryBinding` 또는 별도 runtime composition에서 HP 상태와 Entity Despawn policy를 연결합니다.

## 확장 지점

- 새 Entity 모델: `IEntity` 또는 `EntityBase`
- 새 획득 정책: `EntitySpawnRegistry.Acquire()` / `Release()`
- 새 Key 정책: `IKeyGenerator<ulong>`
- 외부 확정 Key 등록: `EntitySpawnRegistryBase<TEntity>.SpawnWithKey(entity, key)`
- required lifecycle participant: `ISpawnRegistryBinding<TKey,T>`
- 새 Actor 생성: `IEntityActorFactory` / `EntityActorFactory`
- Actor lookup/lifecycle: `EntityActorRegistry` / `EntityActorController`
- 새 Presentation 구성: `IEntityPresentationProvider`

## 관련 문서

- [Entity Registry 구현하기](../guides/foundation/create-entity-registry.md)
- [Entity Actor Runtime 조립하기](../guides/foundation/compose-entity-actor-runtime.md)
- [HP](../modules/combat/hp.md)
- [소유권과 수명](https://inonego-unity.github.io/Xeri/docs/concepts/ownership-and-lifetime.html)
