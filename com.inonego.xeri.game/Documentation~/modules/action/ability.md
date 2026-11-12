# Xeri Game Ability — 상태·바인딩·활성화

Ability는 **재사용 가능한 정의**, **소유자별 지속 상태**, **입력 Entry의 현재 연결**, **이번 사용의 실행 선택·승인**을 분리합니다. 공격·스킬·회피 등 게임별 의미와 실제 Action 실행 권한은 호출 프로젝트가 정합니다.

## 언제 사용하는가

- 무기·장비·폼 변경으로 같은 입력 Entry의 Ability를 교체하지만 기존 Ability의 상태는 유지해야 할 때
- Cooldown·Charge·사용자 정의 primitive 값을 논리 Owner 단위로 관리할 때
- 동일한 Ability가 요청 Context에 따라 다른 Execution·Requirement·Cost를 선택해야 할 때
- 사용 가능 여부를 먼저 검사하고, 실제 승인 시에만 비용을 소비해야 할 때

네임스페이스는 `inonego.Xeri.Game`입니다.

## 핵심 타입과 책임

| API | 역할 |
|---|---|
| `AbilityKey` / `AbilityEntryKey` | 실제 Ability Identity와 입력 Entry Identity를 각각 식별하는 문자열 기반 값 |
| `AbilityInstance<TDefinition>` | 공유 Definition과 Owner별 `AbilityState` 조합. 실행 중인 Cast를 뜻하지 않음 |
| `AbilityState` / `AbilityStateControl` | 지속 Cooldown·Charge·`ValueRegistry` 관찰과 변경 |
| `AbilityBindingLayer<TDefinition>` | Entry → Instance의 일부 연결과 Priority |
| `AbilityBindings<TDefinition>` | Layer 등록 `AcquireLayer`·`AcquireOverride`, `TryResolve`, `ResolveAll` |
| `AbilityActivationContext<TDefinition,TProjectContext>` | Owner, Instigator, 현재 Binding, Project 요청 맥락 |
| `AbilityUseSelector<TDefinition,TProjectContext,TExecution>` | 등록된 규칙의 순서대로 첫 비-null Choice 선택 |
| `AbilityUseChoice<...>` / `AbilityUseSelection<...>` | 선택한 Execution·Policy·현재성 검사·선택 후 Transition을 한 번의 사용으로 묶음 |
| `IAbilityActivationRequirement<...>` | `Evaluate(context)`로 사용 가능 여부만 판단 |
| `IAbilityActivationCost<...>` | 무소비 `Evaluate(context)`와 실제 소비 `Pay(context)` 분리 |
| `AbilityActivationPolicy<...>` | Requirement 전체 검사 → Cost 전체 검사 → Cost 등록 순서대로 Pay |
| `AbilityCooldownRequirement<...>` / `AbilityChargeCost<...>` | 제공되는 Cooldown 검사·Charge 비용 구현 |

## 지속 상태: Ability Identity와 실행 수명 분리

`AbilityState`는 유효한 `AbilityKey`로 생성합니다. `AbilityInstance<TDefinition>`는 Definition 참조와 State를 함께 보관하고, 동일 Definition을 여러 Owner가 공유해도 각 Owner의 State는 별도입니다.

~~~csharp
using inonego.Xeri.Game;

var state = new AbilityState(new AbilityKey("dash"));
state.Control.ConfigureCharge(capacity: 2, rechargeDuration: 3f);
var instance = new AbilityInstance<object>(new object(), state);

// 실제 게임의 논리 Owner가 매 Tick 호출합니다.
state.Advance(0.016f); // 실제 게임에서는 Tick의 deltaTime을 전달

// 변경은 읽기 전용 Charge가 아니라 Control을 통해 수행합니다.
bool spent = state.Control.TryConsumeCharge(1);
bool cooldownStarted = state.Control.TryBeginCooldown(1.5f);
~~~

- `State.Cooldown`은 `IReadOnlyTimer`, `State.Charge`는 `IReadOnlyAbilityChargeState`로 노출됩니다. `Charge`는 구성하지 않았으면 null일 수 있습니다.
- `State.Values`는 Owner에 귀속된 직렬화 가능한 primitive 값을 보관하는 `ValueRegistry`입니다. 세션의 `ExecutionSession.Variables`와 다릅니다.
- `Control.ConfigureCharge(capacity, rechargeDuration, startFull)`로 수량·재충전 주기를 설정하고, `TryConsumeCharge`·`RestoreCharge`로 소비·복원합니다. Charge는 순차 재충전하며 Cooldown과 독립입니다.
- `Advance(deltaTime)`은 Cooldown과 이미 진행 중이던 Recharge를 갱신합니다. 같은 Tick 도중 새로 시작된 Recharge에 이전 경과 시간을 소급 적용하지 않습니다. NaN·무한 시간과 중첩 Advance는 거부합니다.
- Unity 직렬화는 필드 상태를 다루지만 프로젝트 전체의 Save/Load, 소유자 복원, 실행 세션 재생까지 제공하는 것은 아닙니다.

## Entry Binding: 지금 사용할 Instance 해석

~~~csharp
var entry = new AbilityEntryKey("primary");
var baseLayer = new AbilityBindingLayer<object>(priority: 0);
baseLayer.Set(entry, instance);

var bindings = new AbilityBindings<object>();
using var registration = bindings.AcquireLayer(baseLayer);

if (bindings.TryResolve(entry, out var binding))
{
    // binding.EntryKey와 binding.Instance를 함께 사용합니다.
}
~~~

우선순위가 높은 Layer가 우선하고 **동일 Priority에서는 가장 나중에 등록한 Layer**가 우선합니다. `AcquireOverride(entry, instance, priority)`는 단일 Entry의 임시 Layer를 등록하고 `Lease`를 반환합니다. `Lease.Dispose()` 또는 `Release()`로 제거하면 아래 Layer의 연결이 다시 노출됩니다.

`ResolveAll(destination)`은 결과 목록을 비우고 Entry 키의 Ordinal 순서로 현재 연결을 채웁니다. 과거에 선택된 사용 Context나 시작된 Session을 자동으로 새 Binding으로 갈아끼우지 않습니다.

## 사용 선택: Execution과 비용을 같은 후보로 고정

~~~text
Bindings.TryResolve(entry) → AbilityActivationContext
    → AbilityUseSelector.TrySelect(context)
        → 첫 번째 일치 규칙의 AbilityUseChoice
            · 이번 실행(Execution)
            · 이번 Requirement와 Cost(Policy)
            · 지금도 유효한지(IsCurrent)
            · 비용 소비 후 전이(Transition)
    → AbilityUseSelection.Evaluate() [선택적 사전 검사]
    → 프로젝트 Host 실행 가능 여부 판단
    → AbilityUseSelection.TryActivate(out result)
    → 프로젝트 Host가 선택된 Execution 시작
~~~

`AbilityUseSelector`는 `Func<AbilityActivationContext<...>,AbilityUseChoice<...>>` 목록을 생성자로 받아 등록 순서대로 호출합니다. 규칙은 **선택만** 하고 비용 소비, State 변경, Slot 선점을 하지 않아야 합니다. 규칙이 null을 반환하면 다음 규칙을 평가합니다.

`AbilityUseChoice<TDefinition,TProjectContext,TExecution>` 생성자는 `(execution, policy, isCurrent, transition = null)`입니다. `TExecution`은 프로젝트가 선택한 실행 정의 타입이며 반드시 `ExecutionDefinition`일 필요는 없습니다.

`AbilityUseSelection`의 계약:

1. `Context`는 선택 당시 요청, `Execution`은 선택한 실행입니다.
2. `Evaluate()`는 `attempted` 여부, `IsCurrent`, 모든 Requirement와 Cost의 `Evaluate`를 검사하고 소비하지 않습니다. 이 검사는 이후 실행을 예약하지 않습니다.
3. `TryActivate(out result)`는 **첫 시도에서 attempted를 확정**합니다. 거부·예외 후 같은 Selection을 재사용할 수 없고 새로 선택해야 합니다.
4. 먼저 Requirement 전체를, 그다음 Cost 전체를 평가합니다. 중간 거부 시 어떤 `Pay`도 호출하지 않습니다.
5. 모두 허용되면 Cost의 `Pay`를 등록 순서로 호출하고 `Transition`을 실행합니다. 반환되는 `AbilityActivationResult`에는 `Succeeded`, `Failure`, `PolicyIndex`, `Reason`이 있습니다.

`AbilityActivationFailure`에는 `InvalidState`, `RequirementRejected`, `CostRejected` 등이 있습니다. 외부 콜백 예외는 반환형 실패 결과로 자동 변환되지 않으므로 호출 프로젝트가 처리해야 합니다.

## Cost의 원자성과 게임 Host 책임

- `AbilityChargeCost<TDefinition,TProjectContext>(amount)`는 `State.Charge.Current`를 검사하고 `State.Control.TryConsumeCharge`로 비용을 소비합니다. 검사 이후 수량이 바뀌어 소비에 실패하면 예외를 던집니다.
- 한 Resource를 여러 Cost가 따로 검사할 때는 **합계 수량 보장**이 없습니다. 합산 비용을 하나의 Cost·Owner에서 검사하고 소비하세요.
- 서로 다른 Resource Owner의 비용을 원자적으로 묶는 트랜잭션·자동 환급은 제공하지 않습니다. `Pay` 또는 `Transition` 예외 전에 이미 적용한 소비는 유지될 수 있습니다.
- 배타 Slot 획득, 기존 Action의 선점·중단, Begin 실패 후 Slot 회수는 게임 Host가 담당합니다. 비용을 환급하려면 별도의 명시적 게임 정책이 필요합니다.
- Cooldown 검사도 **선택적인 Requirement**입니다. `AbilityCooldownRequirement`를 Policy에 추가해야 Cooldown이 사용 조건에 반영됩니다. Charge만 사용하는 Ability에 Cooldown 정책이 자동으로 강제되지는 않습니다.

## 관련 문서

- [Gameplay Framework 전체 구조](overview.md)
- [Execution — Flow·Behavior·Trigger](execution.md)
- [실제 연결 가이드](../../guides/action/ability-execution-delivery.md)
- [소유권과 수명](https://inonego-unity.github.io/Xeri/docs/concepts/ownership-and-lifetime.html)
