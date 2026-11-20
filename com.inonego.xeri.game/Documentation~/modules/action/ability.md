# Xeri Game Ability — 상태·바인딩·활성화

Ability는 **재사용 가능한 Spec**, **소유자별 지속 상태**, **입력 Entry의 현재 연결**, **이번 사용의 실행 선택·승인**을 분리합니다. 공격·스킬·회피 등 게임별 의미와 실제 Action 실행 권한은 호출 프로젝트가 정합니다.

## 언제 사용하는가

- 무기·장비·폼 변경으로 같은 입력 Entry의 Ability를 교체하지만 기존 Ability의 상태는 유지해야 할 때
- Cooldown·Charge·사용자 정의 primitive 값을 논리 Owner 단위로 관리할 때
- 동일한 Ability가 요청 Context에 따라 다른 Execution·Requirement·Cost를 선택해야 할 때
- 사용 가능 여부를 먼저 검사하고, 실제 승인 시에만 비용을 소비하며, 실행을 시작하지 못하면 비용을 되돌려야 할 때
- 선택 이후 상태가 바뀌었는지 감지하거나 저장한 상태를 복원해야 할 때

네임스페이스는 `inonego.Xeri.Game`입니다.

## 핵심 타입과 책임

| API | 역할 |
|---|---|
| `AbilityKey` / `AbilityEntryKey` | 실제 Ability Identity와 입력 Entry Identity를 각각 식별하는 문자열 기반 값 |
| `AbilityInstance<TSpec>` | 공유 Spec과 Owner별 `AbilityState` 조합. 실행 중인 Cast를 뜻하지 않음 |
| `AbilityState` / `AbilityStateControl` | 지속 Cooldown·Charge·`ValueRegistry` 관찰과 변경, 변경 번호 `Revision` |
| `AbilityBindingLayer<TSpec>` | Entry → Instance의 일부 연결과 Priority |
| `AbilityBindings<TSpec>` | Layer 등록 `AcquireLayer`·`AcquireOverride`, `TryResolve`, `ResolveAll`, 변경 번호 `Revision` |
| `AbilityActivationContext<TSpec>` | Owner, Instigator, 현재 Binding, 게임이 요청에 덧붙이는 `object` Payload |
| `AbilityUseSelector<TSpec,TPayload,TExecution>` | 등록된 규칙의 순서대로 첫 비-null Choice 선택 |
| `AbilityUseChoice<TSpec,TExecution>` / `AbilityUseSelection<TSpec,TExecution>` | 선택한 Execution·Policy·현재성 검사·Transition을 한 번의 사용으로 묶음 |
| `AbilityUseOrigin` / `AbilityUse<TExecution>` / `AbilityBegin<TExecution>` | 사용 출처(`AbilityKey`, `EntryKey`), 선택한 Execution, 실행 시작 콜백 |
| `IAbilityActivationRequirement<TSpec>` | `Evaluate(context)`로 사용 가능 여부만 판단 |
| `IAbilityActivationCost<TSpec>` / `AbilityCostReceipt` | 무소비 `Evaluate(context)`와 실제 소비 `Pay(context)`. `Pay`는 환급용 영수증을 반환 |
| `AbilityActivationPolicy<TSpec>` | Requirement 전체 검사 → Cost 전체 검사 → Cost 등록 순서대로 Pay |
| `AbilityCooldownRequirement<TSpec>` / `AbilityChargeCost<TSpec>` | 제공되는 Cooldown 검사·Charge 비용 구현 |

## 지속 상태: Ability Identity와 실행 수명 분리

`AbilityState`는 유효한 `AbilityKey`로 생성합니다. `AbilityInstance<TSpec>`는 Spec 참조와 State를 함께 보관하고, 동일 Spec을 여러 Owner가 공유해도 각 Owner의 State는 별도입니다. Key가 없는 State(직렬화용 기본 생성자로 만든 뒤 복원하지 않은 State)는 사용 출처를 만들 수 없으므로 연결하면 `ArgumentException`입니다.

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

- `State.Cooldown`과 `State.Charge.Recharge`는 값만 읽는 `ITimerProgress`(`IsRunning`, `IsPaused`, `Duration`, `ElapsedTime`, `RemainingTime` 등)로, `State.Charge`는 `IReadOnlyAbilityChargeState`로 노출됩니다. 내부 Timer를 구독할 수는 없으며 종료·충전은 아래 알림으로 받습니다. `Charge`는 구성하지 않은 새 State에서는 null입니다. 다만 Unity 직렬화(`JsonUtility`, 씬·프리팹)로 복원하거나 역직렬화하면 구성하지 않았더라도 기본 Charge(용량 1, 가득 참)가 생기므로 Charge 비용을 쓰는 Ability는 항상 `ConfigureCharge`로 구성하세요. 복원한 Charge 값이 범위를 벗어나면(용량 1 미만, 잔량이 0~용량 밖, 충전 시간이 유한한 양수가 아님) 용량은 1 이상, 잔량은 0~용량으로 맞추고 충전 시간은 기본값 1초로 되돌립니다. `ConfigureCharge`로 다시 구성해도 `Charge`와 `Charge.Recharge` 인스턴스는 그대로라 들고 있던 참조로 계속 읽을 수 있습니다.
- `State.Values`는 Owner에 귀속된 직렬화 가능한 primitive 값을 보관하는 `ValueRegistry`입니다. 세션의 `ExecutionSession.Variables`와 다릅니다.
- `Control.ConfigureCharge(capacity, rechargeDuration, startFull)`로 수량·재충전 주기를 설정하고, `TryConsumeCharge`·`RestoreCharge`로 소비·복원합니다. Charge는 순차 재충전하며 Cooldown과 독립입니다.
- Cooldown은 `TryBeginCooldown`으로 시작합니다. 진행 중이거나 일시정지된 Cooldown이 있으면 시작하지 않고 false를 반환합니다. 지속시간 0은 "Cooldown 없음"이라 시작할 것이 없으므로 항상 true를 반환하고 진행 중인 Cooldown과 `Revision`을 바꾸지 않습니다. 반환값은 "요청한 Cooldown을 시작했거나 시작할 것이 없다"는 뜻입니다. Cooldown은 `PauseCooldown`·`ResumeCooldown`으로 일시정지·재개합니다. 일시정지한 Cooldown은 `Advance`로 줄지 않습니다.

### 시간 진행과 알림

`Advance(deltaTime)`은 Cooldown과 Recharge를 같은 시간만큼 진행합니다. 큰 delta에서는 여러 Charge를 차례로 충전합니다. 진행 중에는 외부 코드를 호출하지 않습니다. NaN·무한 시간은 거부합니다. 상태를 어느 시점까지 진행할지는 논리 Owner의 Tick이 정하며, Binding에서 빠진 Instance도 자기 Owner가 Tick을 주는 동안 독립적으로 진행합니다.

진행이 끝나면 다음 알림을 보냅니다.

| 알림 | 시점 |
| --- | --- |
| `OnCooldownEnded(state)` | 진행 중이던 Cooldown이 시간 진행으로 끝났을 때. `ResetCooldown`·`ReduceCooldown`으로 끝낸 경우는 알리지 않습니다 |
| `OnChargeRecharged(state, count)` | 시간 진행으로 Charge가 충전됐을 때. 한 번의 `Advance`에서 충전된 수를 함께 전달합니다 |

알림 규칙은 다음과 같습니다.

- **진행이 끝난 뒤 보냅니다.** 구독자는 Cooldown과 Charge 진행이 모두 끝난 상태를 보며, 알림 안에서 Control·`Advance`·사용을 호출해도 됩니다. 그래서 충전이 가득 찬 직후 알림에서 사용하면 다시 시작되는 충전 주기는 다음 `Advance`부터 진행합니다.
- 한 번의 `Advance`에서 둘 다 생기면 `OnCooldownEnded`, `OnChargeRecharged` 순서로 보냅니다.
- 알림 안에서 다시 `Advance`하면 그 진행의 알림은 그 자리에서 먼저 보냅니다.
- 구독자 예외는 모든 구독자에게 보낸 뒤 전달합니다(하나면 그 예외, 여럿이면 `AggregateException`). 상태는 이미 확정돼 있으므로 되돌리지 않습니다.

### 변경 번호: Revision

`State.Revision`은 상태를 실제로 바꾼 연산마다 정확히 1씩 증가합니다. 바뀌지 않았으면 그대로입니다. 증가하는 경우는 다음과 같습니다.

- Control 호출이 실제로 상태를 바꾼 경우
- `Values` 변경
- 복원(`CloneFrom`, 직렬화 복원)
- 비용 환급

시간 진행(Cooldown 종료·재충전 완료를 포함한 `Advance`)으로는 증가하지 않습니다. 복제와 현재성 검사는 `Revision`을 비교해 "시간 외의 변경이 있었는가"를 판별합니다. `Revision`은 직렬화하지 않는 State 인스턴스별 번호이므로 같은 인스턴스 안에서만 비교하며, 권한이 바뀐 뒤처럼 다른 State 인스턴스의 번호와는 비교하지 않습니다. `AbilityBindings.Revision`은 Layer 등록·해제와 등록된 Layer의 내용 변경마다 증가하며 같은 역할을 합니다.

### 저장과 복원

`AbilityState`는 `[Serializable]`이며 Cooldown·Charge Timer와 `Values`가 Unity 직렬화에 포함됩니다. 진행 중이거나 일시정지한 상태, 부분 충전도 그대로 저장되고 복원됩니다. `Revision`과 실행 중 여부(in-use)는 직렬화하지 않습니다.

메모리 안의 사본(예측 롤백, 권한 Snapshot 적용, 서버 이관)은 Xeri Core의 깊은 복제 계약(`IDeepCloneable`)으로 다룹니다. `state.Clone()`은 같은 Key의 독립 사본을 만들고, `state.CloneFrom(source)`는 Cooldown·Charge·`Values`를 덮어씁니다. `CloneFrom`의 규칙은 다음과 같습니다.

- 인스턴스를 유지합니다. 덮어쓴 뒤에도 `Values`, `Charge`, Timer 참조와 `Values`·State 알림 구독이 그대로 남습니다. source에 Charge가 없으면 Charge를 제거하고, 대상에 Charge가 없으면 새로 만듭니다.
- 복사는 변경 알림(`Values.OnChange`, State 알림)을 보내지 않습니다. 대신 `Revision`이 1 증가합니다.
- 다른 `AbilityKey`의 상태는 `ArgumentException`, 사용 중인 State(`Selection.TryActivate` 실행 도중)는 `InvalidOperationException`으로 거부합니다.

저장 데이터의 복원은 `JsonUtility.FromJson`·`FromJsonOverwrite` 등 Unity 직렬화로 합니다. 복원이 끝나면 `Revision`이 1 증가해 복원 이전에 한 선택은 현재성 검사에서 거부됩니다. 복원 전에 보관한 `Values` 참조의 변경도 계속 `Revision`에 반영됩니다. 지연 보정이 필요하면 복원한 뒤 호출자가 `Advance`로 처리합니다.

직렬화 복원에는 두 가지 Host 규칙이 있습니다. 직렬화 콜백은 예외를 안전하게 던질 수 없으므로 `CloneFrom`과 달리 Xeri가 검사하지 않습니다.

- **같은 `AbilityKey`의 데이터만** 덮어씁니다.
- 사용 중인 State(`Selection.TryActivate` 실행 도중)에는 복원하지 않습니다.

프로젝트 전체의 Save/Load, 소유자 복원, 실행 Session 재생은 Xeri가 제공하지 않습니다. Session은 저장 대상이 아닙니다.

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

우선순위가 높은 Layer가 우선하고 **동일 Priority에서는 가장 나중에 등록한 Layer**가 우선합니다. `AcquireOverride(entry, instance, priority)`는 단일 Entry의 임시 Layer를 등록하고 `Lease`를 반환합니다. `Lease.Dispose()` 또는 `Release()`로 제거하면 아래 Layer의 연결이 다시 노출됩니다. 교체되었다가 복귀한 Instance는 같은 `AbilityState`를 그대로 유지합니다.

`ResolveAll(destination)`은 결과 목록을 비우고 Entry 키의 Ordinal 순서로 현재 연결을 채웁니다. 과거에 선택된 사용 Context나 시작된 Session을 자동으로 새 Binding으로 갈아끼우지 않습니다. 선택 이후의 Binding 변경은 `AbilityBindings.Revision`으로 감지합니다.

## 사용 선택: Execution과 비용을 같은 후보로 고정

~~~text
Bindings.TryResolve(entry) → AbilityActivationContext
    → AbilityUseSelector.TrySelect(bindings, context, out selection, out reason)
        → 첫 번째 일치 규칙의 AbilityUseChoice
            · 이번 실행(Execution)
            · 이번 Requirement와 Cost(Policy)
            · 현재성 토큰(Bindings Revision·State Revision)과 선택적 isCurrent
            · 시작 성공 후의 전이(Transition)
    → AbilityUseSelection.Evaluate() [선택적 사전 검사]
    → 프로젝트 Host 실행 가능 여부 판단
    → AbilityUseSelection.TryActivate(begin, out result, out use)
        · begin 콜백에서 프로젝트 Host가 선택된 Execution 시작
~~~

`AbilityUseSelector<TSpec,TPayload,TExecution>`는 `Func<AbilityActivationContext<TSpec>,TPayload,AbilityUseChoice<TSpec,TExecution>>` 목록을 생성자로 받아 등록 순서대로 호출합니다. Context의 `Payload`는 `object`이며 Selector가 `TPayload`로 캐스팅해 규칙에 전달합니다. 캐스팅할 수 없으면 예외입니다. 규칙은 **선택만** 하고 비용 소비, State 변경, Slot 선점을 하지 않아야 합니다. 규칙이 null을 반환하면 다음 규칙을 평가합니다.

`AbilityUseChoice<TSpec,TExecution>` 생성자는 `(execution, policy, isCurrent = null, transition = null)`입니다. `TExecution`은 프로젝트가 선택한 실행 Spec 타입이며 반드시 `ExecutionSpec`일 필요는 없습니다.

### 현재성

`TrySelect`는 Context의 Binding이 전달한 `bindings`에서 같은 Entry를 해석한 현재 연결과 같은 Instance일 때만 선택합니다. Binding 교체 전에 만든 낡은 Context나 등록되지 않은 Entry의 Context는 false와 사유를 반환합니다.

선택한 Selection은 그 시점의 `AbilityBindings.Revision`과 `State.Revision`을 현재성 토큰으로 기록합니다. 이 토큰이 달라지면 `Evaluate`와 `TryActivate`가 `InvalidState`로 거부합니다. 선택 이후 Cooldown 시작·Charge 소비·Values 변경·Binding 구성 변경·상태 복원이 있었다면 새로 선택해야 한다는 뜻입니다. Binding 구성 변경에는 어떤 Layer의 등록·해제나 어떤 Entry의 변경이 모두 포함되므로, 선택한 Entry와 무관한 변경도 선택을 무효화합니다. 시간 진행(`Advance`)은 토큰을 바꾸지 않습니다. `isCurrent`는 프로젝트가 추가로 거는 선택적 검사입니다.

현재성 토큰은 평가 단계에서만 확인합니다. 비용 소비(`Pay`)나 전이 중의 외부 알림이 Binding을 바꿔도 `begin`은 선택 시점의 Ability·Execution으로 진행하고 `AbilityUseOrigin`도 그 Ability를 가리킵니다. 사용된 것은 선택한 Ability이고 Binding 변경은 그 결과이기 때문입니다. 소비 이후의 Binding 변경 때문에 실행을 막아야 한다면 Host가 `begin` 안에서 판단합니다.

### 사전 검사와 승인

`Evaluate()`는 현재성(토큰과 `isCurrent`)과 모든 Requirement·Cost의 `Evaluate`를 비용 소비 없이 검사합니다. 통과해도 이후 실행이나 자원이 예약되지는 않습니다.

`TryActivate(begin, out result, out use)`는 선택을 **한 번만** 승인합니다. 거부·예외 후에도 같은 Selection을 재사용할 수 없고 새로 선택해야 합니다. 다만 시도 전의 선조건 예외(`begin`이 null이거나 같은 State가 이미 사용 중이어서 진입하지 못한 경우)는 Selection을 소비하지 않습니다. `begin`은 필수이며 진행 순서는 다음과 같습니다.

1. 사용 중(in-use) 구간 보호를 걸고, 첫 시도에서 시도 완료로 확정합니다. `begin` 안에서 같은 State의 다른 Selection을 `TryActivate`하면 `InvalidOperationException`을 던지며 그 Selection은 소비되지 않습니다.
2. 현재성 토큰과 Policy를 검사합니다. Requirement 전체, 그다음 Cost 전체를 평가하며 중간에 거부되면 어떤 `Pay`도 호출하지 않습니다.
3. 모두 허용되면 Cost의 `Pay`를 등록 순서로 호출하고 반환된 `AbilityCostReceipt`를 모읍니다.
4. `begin(in use)`를 호출합니다. `use`는 `AbilityUse<TExecution>`으로, `Origin`(`AbilityUseOrigin`의 `AbilityKey`·`EntryKey`)과 선택한 `Execution`을 담습니다.
5. `begin`이 true를 반환하면 `Transition`을 실행합니다.
6. 결과로 `AbilityActivationResult`(`Succeeded`, `Failure`, `PolicyIndex`, `Reason`)를 반환합니다.

`AbilityActivationFailure`는 `InvalidState`, `RequirementRejected`, `CostRejected`, `StartRejected`를 구분합니다. 성공 결과의 `Failure`는 `None`이며 `Succeeded`는 그때만 true입니다. 기본값(`default(AbilityActivationResult)`)은 `Uninitialized`이므로 `Succeeded`가 false입니다. 외부 콜백의 예외는 반환형 실패 결과로 자동 변환되지 않으므로 호출 프로젝트가 처리해야 합니다.

## Cost 환급과 게임 Host 책임

비용은 영수증으로 되돌립니다. `IAbilityActivationCost<TSpec>.Pay`는 `AbilityCostReceipt`를 반환합니다. 상태를 바꾸지 않았다면 `AbilityCostReceipt.NoChange`, 되돌려야 한다면 `new AbilityCostReceipt(refund)`를 반환합니다. `AbilityChargeCost<TSpec>(amount)`의 영수증은 소비 직전의 Charge 상태(수량과 충전 시계)를 정확히 복원합니다. 환급은 복원이므로 `CloneFrom`과 같이 알림을 보내지 않고 `Revision`만 1 올립니다. 진행 중인 충전 시계의 경과 시간도 그대로 이어집니다. 검사 이후 수량이 바뀌어 소비에 실패하면 예외를 던집니다.

검사·지불·시작·환급은 모두 `Selection.TryActivate` 하나로 진행합니다. `AbilityActivationPolicy`는 Requirement·Cost 구성만 공개하고, 검사(`Selection.Evaluate`)와 지불은 Selection이 내부에서 수행합니다.

| 상황 | 처리 |
|---|---|
| Requirement·Cost 평가 거부 | 소비 전이므로 환급할 것이 없음 |
| `Pay` 중 예외 | 이미 지불한 영수증을 역순으로 환급하고 예외를 다시 던짐 |
| `begin`이 false 반환 | 영수증을 역순으로 환급하고 `StartRejected` 반환 |
| `begin` 예외 | 영수증을 역순으로 환급하고 원래 예외를 다시 던짐 |
| `begin`이 지불 뒤 State를 바꿔(`Advance`·`RestoreCharge`·`ConfigureCharge` 등) Charge가 달라짐 | 영수증이 환급을 거부하는 예외를 던지고 Charge는 되돌려지지 않음. `begin` 예외가 함께 있으면 그 예외를 첫 항목으로 한 `AggregateException`, 없으면 환급 예외 하나를 전달 |
| `Transition` 예외 | 환급하지 않고 예외 전달 |

환급 중 오류가 겹치면 원래 예외를 첫 항목으로 하는 `AggregateException` 하나로 전달합니다. 환급은 `State.Revision`을 올립니다.

Xeri가 보장하지 않는 것은 다음과 같고, 프로젝트가 책임집니다.

- 영수증을 반환하지 않는 외부 Resource의 소비와 그 환급
- 서로 다른 Owner에 걸친 원자적 트랜잭션
- 한 Resource를 여러 Cost가 따로 검사할 때의 합계 수량 보장. 합산 비용은 하나의 Cost·Owner에서 검사하고 소비하세요.
- 선점 규칙(어떤 실행이 어떤 실행을 교체·중단하는지)과 Session이 아닌 배타 행동. Session 단위 배타 실행의 점유·교체 순서·실패 시 회수는 [`ExecutionChannel`](execution.md#배타-실행-채널)이 맡습니다.

Cooldown 검사도 **선택적인 Requirement**입니다. `AbilityCooldownRequirement`를 Policy에 추가해야 Cooldown이 사용 조건에 반영됩니다. Charge만 사용하는 Ability에 Cooldown 정책이 자동으로 강제되지는 않습니다.

## 관련 문서

- [Gameplay Framework 전체 구조](overview.md)
- [Execution — Flow·Behavior·Trigger](execution.md)
- [실제 연결 가이드](../../guides/action/ability-execution-delivery.md)
- [소유권과 수명](https://inonego-unity.github.io/Xeri/docs/concepts/ownership-and-lifetime.html)
