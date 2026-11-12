# HP

Xeri HP는 int/float 계열 값을 공통 `INumeric` 계약으로 다루는 체력 모델입니다.
현재값, 최대값, Alive/Dead 상태와 Heal/Damage 이벤트를 하나의 상태 경계로 관리합니다.

## 기본 사용

```csharp
var hp = new HP_I();
hp.MaxValue = 100;
hp.MakeAlive();

hp.ApplyDamage(25);
float ratio = hp.Ratio;

hp.ApplyHeal(10);
```

## 상태 모델

```text
Value > 0  ↔ Alive
Value == 0 ↔ Dead
```

`Value`가 0 경계를 넘으면 상태가 자동 전환됩니다.
`MakeAlive()`와 `MakeDead()`는 기본적으로 값도 각각 `MaxValue`와 0으로 맞춥니다.

## 값과 최대값
- `Value`: 0~`MaxValue` 범위로 clamp
- `MaxValue`: 0 이상
- `Ratio`: `Value / MaxValue`, MaxValue가 0이면 0

MaxValue를 현재 Value보다 작게 낮추면 Value도 새 최대값으로 조정됩니다.

## Heal과 Damage

`ApplyHeal()`과 `ApplyDamage()`는 Dead 상태에서는 동작하지 않습니다.
음수와 0 amount도 적용하지 않습니다.

```text
ApplyDamage
→ Value 감소
→ 0 도달 시 Dead 전환
→ OnDamage
```

## 이벤트

- `OnValueChange`
- `OnMaxValueChange`
- `OnStateChange`
- `OnHeal`
- `OnDamage`

상태 전환과 값 변경 이벤트는 같은 변경에서 함께 발생할 수 있으므로 구독자는 각각 독립된 의미로 처리합니다.
## Entity와 조합

HP는 Entity Core의 필수 책임이 아닙니다.
HP가 필요한 Entity는 `IReadOnlyHPOwner/IHPOwner` 또는 generic capability를 구현해 기존 HP 객체를 제공합니다.

```text
EntityBase
├─ Key / HasKey
├─ SpawnState
└─ lifecycle

optional:
IReadOnlyHPOwner / IHPOwner
└─ HP
```

HP Dead와 Entity Despawn은 서로 다른 lifecycle입니다.
Xeri Core는 Dead 상태를 자동으로 Registry Despawn에 연결하지 않습니다.
게임 규칙상 필요하면 runtime binding/composition에서 `OnStateChange`를 구독해 `DespawnReason.Dead` 같은 정책으로 연결합니다.

이 분리를 통해 HP 없는 Entity와, HP가 Dead여도 논리 Entity 수명을 유지하는 정책을 모두 지원할 수 있습니다.

## 책임 범위

HP는 값·상태·Heal/Damage 이벤트만 소유합니다.
방어력, 피해 타입, 무적 시간, 공격 판정, Entity 제거 정책은 상위 gameplay/runtime composition이 소유합니다.

## 관련 문서

- [Entity와 Spawn 수명](../../foundation/entity-and-spawn.md)
- [Entity Registry 구현하기](../../guides/foundation/create-entity-registry.md)
- [Xeri Game](../index.md)
