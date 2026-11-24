# Ability 사용에서 Execution·Targeting·Delivery까지 연결하기

이 가이드는 **Xeri API를 실제 Host가 조합하는 순서**를 설명합니다. Xeri는 특정 전투 시스템을 제공하지 않으므로, 예시의 승인·리소스·피해 규칙은 프로젝트가 소유합니다.

## 전제와 역할

- 논리 Owner가 `AbilityState`와 재충전 Tick·저장 책임을 보유합니다.
- 게임 Host가 입력 Entry를 해석하고 선점 규칙과 Session 진행을 결정합니다. Session 단위 배타 실행의 점유·교체 순서·해제는 `ExecutionChannel`이 맡습니다.
- `ExecutionSpec`은 공유 설정이며 `ExecutionSession`은 요청마다 새로 생성됩니다.
- 대상 후보·Collider → 게임 Recipient 매핑·실제 피해/회복은 프로젝트에서 구현합니다.

관련 계약은 [Ability](../../modules/action/ability.md), [Execution](../../modules/action/execution.md), [Spatial·Targeting](../../modules/action/spatial-targeting.md), [Delivery·Operation](../../modules/action/delivery-operation.md)에서 설명합니다.

## 1. 동작하는 Ability·Execution Host 예제

다음 예제는 Xeri의 `AbilityState`, `AbilityBindings`, `AbilityUseSelector`, `AbilityActivationPolicy`, `ExecutionSession`, `ExecutionChannel`을 연결하는 **독립적인 C# 클래스**입니다. `ExampleAbilityHost` 자체가 프로젝트 Host의 가장 단순한 형태이며, 입력·Animator·Motor·실제 자원 저장소는 구현하지 않습니다. 채널의 판정 함수(실행 중이면 거절)는 동시에 하나의 Execution만 실행한다는 **예제의 배타 정책**입니다.

~~~csharp
using System;
using inonego.Xeri;
using inonego.Xeri.Game;

public sealed class ExampleAbilityHost : IDisposable
{
    private readonly AbilityEntryKey entry = new("primary");
    private readonly AbilityState state = new(new AbilityKey("example"));
    private readonly AbilityBindings<object> bindings = new();
    private readonly ExecutionSpec execution =
        new(new FixedTimelineExecutionFlowSpec(0.6f));

    private readonly Lease registration;
    private readonly AbilityUseSelector<object, string, ExecutionSpec> selector;
    private readonly ExecutionChannel<int> channel =
        new((current, next) => ExecutionChannelDecision.Reject);
    private ulong nextId;

    public ExampleAbilityHost()
    {
        state.Control.ConfigureCharge(capacity: 2, rechargeDuration: 3f);
        var instance = new AbilityInstance<object>(new object(), state);
        var layer = new AbilityBindingLayer<object>(priority: 0);
        layer.Set(entry, instance);
        registration = bindings.AcquireLayer(layer);

        var policy = new AbilityActivationPolicy<object>(
            requirement: new IAbilityActivationRequirement<object>[]
            {
                new AbilityCooldownRequirement<object>()
            },
            cost: new IAbilityActivationCost<object>[]
            {
                new AbilityChargeCost<object>(amount: 1)
            }
        );
        selector = new AbilityUseSelector<object, string, ExecutionSpec>(
            new Func<AbilityActivationContext<object>, string,
                AbilityUseChoice<object, ExecutionSpec>>[]
            {
                (request, payload) => new AbilityUseChoice<object, ExecutionSpec>(
                    execution,
                    policy,
                    // Cooldown 시작은 begin 성공 후 실행되는 transition에 둡니다.
                    transition: () =>
                    {
                        request.Ability.State.Control.TryBeginCooldown(0.6f);
                    }
                )
            }
        );
    }

    public bool TryUse(object instigator)
    {
        if (!channel.CanStart(0) ||
            !bindings.TryResolve(entry, out var binding))
            return false;

        var context = new AbilityActivationContext<object>(
            this, instigator, binding, payload: "primary"
        );
        if (!selector.TrySelect(bindings, context, out var selection, out _))
            return false;

        if (!selection.Evaluate().Succeeded)
            return false;

        // 채널이 거절하면 begin이 false, 시작이 실패하면 예외가 되어
        // TryActivate가 지불한 Charge를 환급합니다.
        return selection.TryActivate(
            (in AbilityUse<ExecutionSpec> use) => channel.TryStart(
                use.Execution.CreateSession(
                    ++nextId, new inonego.Xeri.Game.ExecutionContext(this, instigator)
                ),
                0
            ),
            out _,
            out _
        );
    }

    public void Tick(float deltaTime)
    {
        state.Advance(deltaTime);
        var session = channel.Active;
        if (session != null)
        {
            session.Advance(deltaTime);
            session.TryComplete();   // 끝나면 채널이 스스로 비워집니다.
        }
    }

    public void Cancel() => channel.Active?.End(ExecutionEndReason.Interrupted);

    public void Dispose()
    {
        try
        {
            channel.Active?.End(ExecutionEndReason.Cancelled);
        }
        finally
        {
            registration.Dispose();
        }
    }
}
~~~

사용하는 측에서는 Host를 생성한 후 사용 시점에 `TryUse(instigator)`를 호출하고, 프레임 또는 게임 Tick마다 `Tick(deltaTime)`을 호출합니다. Host 소유 수명이 끝나면 `Dispose()`를 호출해 진행 중인 실행과 Binding Layer 등록을 정리합니다. 실제 게임에서는 게임 루프/Actor·Service 수명에 이 호출을 연결합니다.

이 예제의 `object` Spec과 `string` Payload(게임이 요청에 덧붙이는 데이터)는 Xeri가 강제하는 데이터 모델이 아닙니다. 실제 게임에서는 공유 Spec과 구조화된 요청 Context를 사용하면 됩니다. `AbilityActivationContext.Payload`는 `object`이며 Selector가 `TPayload`로 캐스팅해 규칙에 전달합니다. 새로운 입력·모드·타깃 규칙은 `AbilityUseSelector`의 규칙과 `AbilityUseChoice`의 Execution·Policy·Transition 선택에 반영합니다.

## 2. 사용 선택과 비용 소비

- `bindings.TryResolve(entry, out binding)`은 현재 Entry에서 사용할 실제 Ability Instance를 해석합니다.
- `selector.TrySelect(bindings, context, out selection, out reason)`는 일치하는 첫 규칙의 Execution·Requirement·Cost를 고르며 **부작용을 발생시키지 않아야 합니다**. Context의 Binding은 `bindings`가 현재 해석하는 연결과 같아야 하고, 선택 시점의 `bindings.Revision`·`State.Revision`을 함께 기록합니다.
- `selection.Evaluate()`는 현재성(토큰이 그대로인지, 선택적 `isCurrent`), Requirement, Cost를 **비용 소비 없이** 검사합니다. 통과했다고 이후 자원이 예약되는 것은 아닙니다.
- `selection.TryActivate(begin, out result, out use)`는 현재성을 재검사하고 비용을 지불한 뒤 `begin(in use)`로 실행을 시작합니다. 실행을 시작하지 못하면(`begin`이 false 또는 예외) 지불한 비용을 영수증으로 되돌립니다. **Selection은 성공·거부·예외 후에도 재사용할 수 없습니다.** 시도 전의 선조건 예외(`begin`이 null이거나 같은 State가 이미 사용 중인 경우)는 Selection을 소비하지 않습니다.
- `AbilityCooldownRequirement`와 `AbilityChargeCost`는 선택적인 정책 구성 요소입니다. 이 예제는 Cooldown을 `begin` 성공 후 실행되는 `Transition`에서 시작합니다.

환급 규칙의 전체 표(`Pay` 중 예외, `begin` 실패, `Transition` 예외)와 Xeri가 보장하지 않는 범위는 [Ability — Cost 환급과 게임 Host 책임](../../modules/action/ability.md#cost-환급과-게임-host-책임)에 있습니다. 영수증을 반환하지 않는 외부 자원의 소비는 Host가 직접 되돌립니다.

## 3. 배타 실행과 실행 종료

예제는 [`ExecutionChannel<TClaim>`](../../modules/action/execution.md#배타-실행-채널) 하나로 한 번에 하나의 Session만 허용합니다. 판정 함수가 항상 `Reject`이므로 실행 중에는 새 사용이 거절됩니다. 현재 Action을 교체하려면 `Replace`, 중단하려면 `Interrupt`를 반환하도록 판정 함수를 바꾸고, Claim에 우선순위나 행동 종류를 담습니다. 독립 실행이 여러 개라면 채널을 여러 개 둡니다.

`begin` 안에서 `channel.TryStart(session, claim)`을 반환하면 선점 거절은 `false`, 이전 종료·새 시작 실패는 예외가 되어 `TryActivate`가 비용을 환급합니다. 채널은 Session이 어떤 이유로든 끝나면 스스로 비워지므로 Host가 종료 콜백으로 슬롯을 비울 필요가 없습니다. `Dispose()`는 진행 중인 Session을 취소하고 Binding 등록 Lease를 해제합니다.

`begin` 안에서 채널 시작 뒤에 Host 쪽 처리가 실패하면, Host는 시작한 Session을 직접 끝낸 뒤 예외를 전달해야 합니다. Session이 아닌 배타 행동(점프, 착지 회복 등)의 점유는 Host가 별도로 관리합니다.

## 4. 위치·타깃을 읽고 Delivery에 전달

Execution Trigger가 열렸다고 모든 후보를 바로 Damage로 처리하지 않습니다. **입력 검증 → 공간 관측 → 후보 선택 → 접촉** 순서로 구분합니다.

~~~csharp
using inonego.Xeri.Spatial;
using UnityEngine;

// 실행 시작 시점의 공간 출처를 고정하는 예시.
var spatialRequest = new SpatialRequest<object>(
    SpatialSource<object>.Point(targetPoint),
    SpatialAimMode.TowardSource
);
var spatial = new SpatialSession<object>(
    spatialRequest,
    SpatialTimeMode.Capture
);
spatial.Begin(0f, origin);

if (!spatial.Current.HasPosition ||
    !spatial.Current.HasDirection)
{
    // 게임별 유효하지 않은 타깃 요청으로 처리합니다.
    return;
}

// 실행/Behavior의 현재 위치·방향에 맞춰 게임 후보를 준비합니다.
// TargetSelector.TrySelect/SelectTop으로 적합성·점수 순위를 결정합니다.
// 접촉이 필요하면 PhysicsDelivery2D/3D나 ProjectileDelivery2D/3D를 호출합니다.
~~~

`SpatialTimeMode.Follow`가 필요하면 Session의 시간에 맞춰 `Evaluate(time, origin)`를 호출하세요. 게임의 적/아군 판정과 TargetFilter는 Xeri가 자동 생성하지 않습니다.

## 5. Collider Contact를 게임 Operation으로 전달

3D의 Volume 쿼리를 예로 들면:

~~~csharp
using inonego.Xeri.Game;
using UnityEngine;

// physicsVolume, layerMask, operation, owner, instigator는 Host가 제공합니다.
var physics = new PhysicsDelivery3D();
var workspace = new PhysicsDeliveryWorkspace3D(64, 256);
var batch = physics.QueryVolume(physicsVolume, layerMask, workspace);

if (batch.IsSaturated)
{
    // 버퍼 포화: 완전한 결과 집합이 아니므로 정책에 따라 재처리합니다.
    return;
}

for (int i = 0; i < batch.Count; ++i)
{
    DeliveryContact3D contact = workspace.GetContact(i);
    if (!contact.IsValid) continue;

    // 프로젝트 Operation이 Collider를 실제 수신자로 해석할 수 있습니다.
    var operationContext = new GameplayOperationContext<Collider, float>(
        owner, instigator, origin, contact.Recipient, payload: 12f
    );
    GameplayOperationResult result =
        GameplayOperationRunner.Invoke(operation, operationContext);

    // Applied/NoOp/Rejected는 프로젝트가 정의한 실제 적용 결과입니다.
}
~~~

`QueryVolume`은 겹침 Contact의 목록을 제공합니다. 투사체는 `ProjectileDelivery3D.Advance()`로 접촉 시점·소비 시간·남은 시간을 확인하며, 불완전한 `ProjectileDeliveryStepStatus.Incomplete`를 **접촉 없음으로 간주하지 않습니다**. 2D에서는 대응하는 2D 타입과 `ContactFilter2D`를 사용합니다.

## 운영 시 필수 확인

| 상황 | Host의 처리 원칙 |
|---|---|
| Binding 교체 | 이전 Ability State를 무조건 삭제하지 않고 별도 Owner 수명에 따라 보관 |
| `Selection.Evaluate()` 통과 후 채널 경쟁 | `TryActivate` 전에 `ExecutionChannel.CanStart()`가 거절하면 선택을 소비하지 않음. `begin` 안에서 채널이 거절하면 비용은 환급되고 선택은 소비됨 |
| Cost `Pay()`·`begin` 실패 | `TryActivate`가 영수증 비용을 역순 환급. 영수증 없는 외부 자원은 Host 정리 |
| `begin` 성공 후 `Transition` 예외 | 환급하지 않음. 상태 전이 실패 정리는 Host |
| 선택 이후 State·Binding 변경 | 현재성 토큰이 달라져 `InvalidState`로 거부. 다시 선택 |
| 메모리 안의 State 복원 | `CloneFrom`. Key 불일치와 사용 중 복원은 예외로 거부되고 변경 알림 없이 `Revision`만 증가. 복원 전 선택은 거부됨 |
| 저장한 State 복원 | 같은 Key·사용 중 아님을 Host가 확인한 뒤 직렬화로 복원. 복원 전 선택은 거부됨 |
| `Session.Begin()` 실패 | Xeri Session은 Cancelled 정리를 시도하고 `ExecutionChannel`은 비워짐. 외부 부작용은 Host 정리 |
| Contact 후보 부족·검색 불완전 | IsComplete/IsSaturated를 확인하고 재조회·유예·실패 정책 적용 |
| Execution 종료 후 Projectile 생존 | Projectile/World Owner가 계속 Tick·충돌·회수 담당 |
| Game Operation의 NoOp/Rejected | 실제 Damage가 발생했다고 가정하지 않음 |

이 가이드는 여러 API의 **조립 예시**입니다. 공유 Spec 저장 방식, 선점 판정 규칙, 대상 팀 규칙, 실제 피해 연산과 저장/복원 구현은 프로젝트에 맞게 선택합니다.
