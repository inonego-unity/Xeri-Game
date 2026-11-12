# Ability 사용에서 Execution·Targeting·Delivery까지 연결하기

이 가이드는 **Xeri API를 실제 Host가 조합하는 순서**를 설명합니다. Xeri는 특정 전투 시스템과 배타 실행 슬롯을 제공하지 않으므로, 예시의 승인·리소스·피해 규칙은 프로젝트가 소유합니다.

## 전제와 역할

- 논리 Owner가 `AbilityState`와 재충전 Tick·저장 책임을 보유합니다.
- 게임 Host가 입력 Entry를 해석하고 실행 Slot 획득·해제, 기존 Action 선점, 새 Session 시작·정리를 결정합니다.
- `ExecutionDefinition`은 공유 설정이며 `ExecutionSession`은 요청마다 새로 생성됩니다.
- 대상 후보·Collider → 게임 Recipient 매핑·실제 피해/회복은 프로젝트에서 구현합니다.

관련 계약은 [Ability](../../modules/action/ability.md), [Execution](../../modules/action/execution.md), [Spatial·Targeting](../../modules/action/spatial-targeting.md), [Delivery·Operation](../../modules/action/delivery-operation.md)에서 설명합니다.

## 1. 동작하는 Ability·Execution Host 예제

다음 예제는 Xeri의 `AbilityState`, `AbilityBindings`, `AbilityUseSelector`, `AbilityActivationPolicy`, `ExecutionSession`을 연결하는 **독립적인 C# 클래스**입니다. `ExampleAbilityHost` 자체가 프로젝트 Host의 가장 단순한 형태이며, 입력·Animator·Motor·실제 자원 저장소는 구현하지 않습니다. 샘플의 `active` 필드는 동시에 하나의 Execution만 실행한다는 **예제의 배타 정책**입니다.

~~~csharp
using System;
using inonego.Xeri;
using inonego.Xeri.Game;

public sealed class ExampleAbilityHost : IDisposable
{
    private readonly AbilityEntryKey entry = new("primary");
    private readonly AbilityState state = new(new AbilityKey("example"));
    private readonly AbilityBindings<object> bindings = new();
    private readonly ExecutionDefinition execution =
        new(new FixedTimelineExecutionFlowDefinition(0.6f));

    private readonly Lease registration;
    private readonly AbilityUseSelector<object, string, ExecutionDefinition> selector;
    private ExecutionSession active;
    private ulong nextId;

    public ExampleAbilityHost()
    {
        state.Control.ConfigureCharge(capacity: 2, rechargeDuration: 3f);
        var instance = new AbilityInstance<object>(new object(), state);
        var layer = new AbilityBindingLayer<object>(priority: 0);
        layer.Set(entry, instance);
        registration = bindings.AcquireLayer(layer);

        var policy = new AbilityActivationPolicy<object, string>(
            requirement: new IAbilityActivationRequirement<object, string>[]
            {
                new AbilityCooldownRequirement<object, string>()
            },
            cost: new IAbilityActivationCost<object, string>[]
            {
                new AbilityChargeCost<object, string>(amount: 1)
            }
        );
        selector = new AbilityUseSelector<object, string, ExecutionDefinition>(
            new Func<AbilityActivationContext<object, string>,
                AbilityUseChoice<object, string, ExecutionDefinition>>[]
            {
                request => new AbilityUseChoice<object, string, ExecutionDefinition>(
                    execution,
                    policy,
                    isCurrent: () =>
                        bindings.TryResolve(entry, out var latest) &&
                        ReferenceEquals(latest.Instance, request.Ability),
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
        if (active?.IsActive == true ||
            !bindings.TryResolve(entry, out var binding))
            return false;

        var context = new AbilityActivationContext<object, string>(
            this, instigator, binding, project: "primary"
        );
        if (!selector.TrySelect(context, out var selection, out _))
            return false;

        if (!selection.Evaluate().Succeeded)
            return false;

        // 이 예제의 배타 실행 Slot은 active 필드가 담당합니다.
        if (!selection.TryActivate(out _))
            return false;

        var session = selection.Execution.CreateSession(
            ++nextId, new inonego.Xeri.Game.ExecutionContext(this, instigator)
        );
        session.OnEnded += HandleEnded;
        active = session;
        try
        {
            session.Begin();
            return session.IsActive;
        }
        catch
        {
            if (ReferenceEquals(active, session)) active = null;
            throw; // 이미 소비한 Cost는 자동으로 복구되지 않습니다.
        }
    }

    public void Tick(float deltaTime)
    {
        state.Advance(deltaTime);
        var session = active;
        if (session?.IsActive == true)
        {
            session.Advance(deltaTime);
            session.TryComplete();
        }
    }

    public void Cancel() => active?.End(ExecutionEndReason.Interrupted);

    private void HandleEnded(ExecutionSession session, ExecutionEndReason reason)
    {
        session.OnEnded -= HandleEnded;
        if (ReferenceEquals(active, session)) active = null;
    }

    public void Dispose()
    {
        try
        {
            active?.End(ExecutionEndReason.Cancelled);
        }
        finally
        {
            registration.Dispose();
        }
    }
}
~~~

사용하는 측에서는 Host를 생성한 후 사용 시점에 `TryUse(instigator)`를 호출하고, 프레임 또는 게임 Tick마다 `Tick(deltaTime)`을 호출합니다. Host 소유 수명이 끝나면 `Dispose()`를 호출해 진행 중인 실행과 Binding Layer 등록을 정리합니다. 실제 게임에서는 게임 루프/Actor·Service 수명에 이 호출을 연결합니다.

이 예제의 `object` Definition과 `string` 요청 Context는 Xeri가 강제하는 데이터 모델이 아닙니다. 실제 게임에서는 공유 Definition과 구조화된 요청 Context를 사용하면 됩니다. 새로운 입력·모드·타깃 규칙은 `AbilityUseSelector`의 규칙과 `AbilityUseChoice`의 Execution·Policy·Transition 선택에 반영합니다.

## 2. 사용 선택과 비용 소비

- `bindings.TryResolve(entry, out binding)`은 현재 Entry에서 사용할 실제 Ability Instance를 해석합니다.
- `selector.TrySelect(context, ...)`는 일치하는 첫 규칙의 Execution·Requirement·Cost를 고르며 **부작용을 발생시키지 않아야 합니다**.
- `selection.Evaluate()`는 현재성, Requirement, Cost를 **비용 소비 없이** 검사합니다. 통과했다고 이후 자원이 예약되는 것은 아닙니다.
- `selection.TryActivate(out result)`는 현재성을 재검사하고 모든 Cost가 허용되면 등록 순서대로 `Pay()`한 후 상태 전이를 실행합니다. **Selection은 성공·거부·예외 후에도 재사용할 수 없습니다.**
- `AbilityCooldownRequirement`와 `AbilityChargeCost`는 선택적인 정책 구성 요소입니다. Cooldown은 별도의 `Transition`에서 시작하도록 이 예제에서 설정했습니다.

Cost가 실제 소비되기 시작한 후 `Pay`, `Transition`, `CreateSession`, `Begin`에서 예외가 발생하더라도 Xeri는 이미 지불한 Charge를 자동 복구하지 않습니다. 같은 자원을 여러 Cost가 소비한다면 합계 검사를 한 Resource Owner에 모아야 합니다.

## 3. Host 슬롯·실행 종료

예제에서는 `active`로 한 번에 하나의 Session만 허용합니다. 실제 게임에서는 현재 Action을 중단해 교체할지, 새로운 실행을 거부할지, 독립 슬롯을 여러 개 둘지 등을 **별도의 Host 정책**으로 구현합니다.

`ExecutionDefinition.CreateSession()`은 별도 Session을 만들며, `session.Begin()`부터 `Advance()` 및 `TryComplete()`가 진행됩니다. `HandleEnded()`는 정상 완료·중단·취소 후 `active`를 비웁니다. `Dispose()`는 세션을 취소하고 Binding 등록 Lease를 해제합니다.

더 복잡한 Host에서는 **슬롯 예약 → 비용 확정 → Session 생성·Begin → 종료 콜백으로 슬롯 해제**를 책임져야 합니다. Begin 실패나 다른 콜백 예외 시에도 슬롯 및 외부 자원 정리가 누락되지 않아야 하며, 비용 환급은 필요할 때만 별도 게임 규칙으로 정의합니다.

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
        owner, instigator, origin, contact.Recipient, project: 12f
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
| `Selection.Evaluate()` 통과 후 Slot 실패 | 선택은 소비하지 않음. Host 정책에 따라 재선택 가능 |
| Cost `Pay()` 중 예외 | 부분 소비가 발생할 수 있음. 자동 환급 없음 |
| `Session.Begin()` 실패 | Xeri Session은 Cancelled 정리를 시도함. Host Slot·외부 부작용 별도 정리 |
| Contact 후보 부족·검색 불완전 | IsComplete/IsSaturated를 확인하고 재조회·유예·실패 정책 적용 |
| Execution 종료 후 Projectile 생존 | Projectile/World Owner가 계속 Tick·충돌·회수 담당 |
| Game Operation의 NoOp/Rejected | 실제 Damage가 발생했다고 가정하지 않음 |

이 가이드는 여러 API의 **조립 예시**입니다. 공유 Definition 저장 방식, 게임 Host의 배타 정책, 대상 팀 규칙, 실제 피해 연산과 저장/복원 구현은 프로젝트에 맞게 선택합니다.
