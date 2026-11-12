# Entity와 Spawn 수명

Xeri Game의 Entity 기반은 **identity와 Spawn lifecycle**만 소유합니다.
`EntitySpawnRegistry`가 Key 발급·등록·rollback을 관리하고, `EntityBase`는 Key·`SpawnState`·lifecycle hook만 제공합니다.
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
| `IReadOnlyEntity` | Key, HasKey, SpawnState 읽기 |
| `IEntity` | Registry가 Key와 Spawn 상태를 관리하는 내부 계약 |
| `ISpawnRegistry<TKey,T>` | 등록 객체 조회와 committed lifecycle notification |
| `ISpawnRegistryBinding<TKey,T>` | Spawn transaction에 포함되는 required binding |
| `EntitySpawnRegistry` | Entity용 Key 생성과 Spawn/Despawn transaction |
| `EntityBase` | identity와 Entity lifecycle hook |
| `DespawnReason` | 종료 원인 전달 |

HP와 Group이 필요한 Entity는 `IReadOnlyHPOwner/IHPOwner`, `IReadOnlyGroupMember/IGroupMember` capability를 조합합니다.

## Spawn transaction

```text
Acquire
→ Key 확정
→ Spawning
→ Registry 등록 + runtime 연결
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
→ 획득 출처로 반환
→ Registry OnDespawned notification
```

정리 단계 일부가 실패해도 가능한 terminal cleanup은 계속 시도합니다.

## 직렬화 후 복원

`SpawnRegistry.OnAfterDeserialize()`는 serialized membership을 기준으로 `SpawnState`, `DespawnFromRegistry` 등 Registry 내부 runtime 연결만 self-rehydrate합니다.
외부 binding과 committed notification은 재생하지 않습니다.

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
- required lifecycle participant: `ISpawnRegistryBinding<TKey,T>`
- 새 Actor 생성: `IEntityActorFactory` / `EntityActorFactory`
- Actor lookup/lifecycle: `EntityActorRegistry` / `EntityActorController`
- 새 Presentation 구성: `IEntityPresentationProvider`

## 관련 문서

- [Entity Registry 구현하기](../guides/foundation/create-entity-registry.md)
- [Entity Actor Runtime 조립하기](../guides/foundation/compose-entity-actor-runtime.md)
- [HP](../modules/combat/hp.md)
- [소유권과 수명](https://inonego-unity.github.io/Xeri/docs/concepts/ownership-and-lifetime.html)
