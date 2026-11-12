# Xeri Delivery·Gameplay Operation — 접촉과 실제 적용

Delivery는 Unity Physics의 후보와 이동 경로를 **접촉 데이터(Contact)** 로 변환합니다. Gameplay Operation은 그 접촉을 받아 **프로젝트가 정의한 실제 게임 작업**을 호출하는 경계입니다. Contact는 Hit·Damage·Heal·Kill과 동의어가 아닙니다.

두 계약은 `inonego.Xeri.Game`에 있습니다.

## 어떤 Delivery를 선택할까

| 필요 | 제공 API | 확인할 결과 |
|---|---|---|
| 현재 영역 안의 모든 Collider 후보 | `PhysicsDelivery2D/3D.QueryVolume` | `DeliveryBatchResult.Count`, `IsSaturated` |
| Ray 구간의 가까운 접촉 | `PhysicsDelivery2D/3D.QueryTrace` | `DeliveryContactQueryResult2D/3D` |
| 현재 영역의 겹침 존재 | `QueryAnyOverlap` | `HasContact`와 `IsComplete` |
| Volume을 움직일 때의 접촉 | `QueryCast` | Contact와 검색 완전성 |
| 이동하는 투사체의 한 번의 직선 경로 | `ProjectileDelivery2D/3D.Advance` | `ProjectileDeliveryStepResult2D/3D` |
| Damage·Heal·Status 적용 | `IGameplayOperation<...>` | `GameplayOperationResult` |

2D는 `PhysicsVolume2D`, `ContactFilter2D`와 `Collider2D`를 사용합니다. 3D는 `PhysicsVolume3D`, `LayerMask`, `Collider`를 사용하며 `PhysicsDelivery3D(QueryTriggerInteraction)`으로 Trigger 포함 정책을 지정합니다.

## Physics Delivery와 재사용 Workspace

`PhysicsDeliveryWorkspace2D/3D(capacity, maximumCapacity)`는 NonAlloc 후보 버퍼와 Contact 목록을 호출자가 재사용하도록 제공합니다. 초기·최대 용량은 양수이고 최대값은 초기값 이상이어야 합니다. 포화 시 최대 용량까지만 재질의·증설합니다.

~~~csharp
using inonego.Xeri.Game;

// volume 및 layers는 프로젝트가 구성한 PhysicsVolume3D와 LayerMask입니다.
var workspace = new PhysicsDeliveryWorkspace3D(
    capacity: 64,
    maximumCapacity: 256
);
var physics = new PhysicsDelivery3D();
DeliveryBatchResult batch = physics.QueryVolume(volume, layers, workspace);

for (int i = 0; i < batch.Count; ++i)
{
    DeliveryContact3D contact = workspace.GetContact(i);
    // 프로젝트에서 Collider → 실제 Recipient 의미로 변환합니다.
}

if (batch.IsSaturated)
{
    // 결과 일부가 누락됐을 수 있으므로 완전한 전체 목록이라고 단정하지 않습니다.
}
~~~

- `Workspace.ContactCount`와 `GetContact(index)`는 **마지막 QueryVolume**의 결과에만 사용합니다. 다음 질의를 시작하면 이전 인덱스·결과의 의미는 무효가 됩니다.
- **같은 Workspace로 중첩 질의하는 것은 허용되지 않습니다.** 실행/스레드 간 결과를 나눠야 한다면 Workspace를 독립적으로 소유하세요.
- `QueryVolume`의 `IsSaturated`가 true면 반환된 `Count` 및 Contact 목록이 전체 집합이 아닐 수 있습니다.
- `QueryTrace`·`QueryAnyOverlap`·`QueryCast`는 `DeliveryContactQueryResult2D/3D`의 `HasContact`와 `IsComplete`를 **별도로** 보고합니다. 불완전하고 Contact가 없으면 '없음'을 확정할 수 없고, 불완전하고 Contact가 있어도 전체 후보 기준의 nearest라고 단정할 수 없습니다.
- `IDeliveryColliderFilter2D/3D.IsAccepted`는 게임의 Collider 후보 수용 정책입니다. 필터와 실제 피해 대상의 적합성을 같은 뜻으로 취급하지 마세요.

## Contact가 나타내는 정보

`DeliveryContact2D/3D`는 다음의 정보를 보존합니다.

| 멤버 | 의미 |
|---|---|
| `Recipient` | 접촉한 `Collider2D` / `Collider`. 게임 도메인의 Entity가 아님 |
| `Position` / `Normal` | 접촉 또는 Overlap 근사 지점·법선 |
| `Direction` | 탐색 또는 Projectile의 진행 방향 |
| `Distance` | Sweep·Trace 시작부터 접촉까지의 거리 |
| `HasSurfaceGeometry` | 정확한 표면 Cast 지오메트리인지, Overlap 기반 근사인지 구분 |
| `IsValid` | Collider 참조가 있는지 확인 |
| `WithDirection(direction)` | 다른 정보를 유지하며 진행 방향만 교체한 Contact 반환 |

Overlap은 표면 지오메트리가 아닌 근사값일 수 있으므로 이 위치·법선을 정밀한 법선 반응이나 피해 지점의 확정 근거로 사용하기 전 `HasSurfaceGeometry`를 확인하세요.

## Projectile Delivery: 이동과 충돌을 한 Step으로

`ProjectileDelivery2D/3D`는 `MovingVolume2D/3D`를 보관하고 **현재 속도의 선형 Sweep**을 실행합니다.

~~~csharp
// projectile은 프로젝트가 MovingVolume3D와 LayerMask로 만든 상태입니다.
// query는 PhysicsDelivery3D 또는 IProjectileDeliveryQuery3D 구현체입니다.
ProjectileDeliveryStepResult3D step =
    projectile.Advance(deltaTime, query, workspace);

switch (step.Status)
{
    case ProjectileDeliveryStepStatus.Contact:
        // step.Contact를 게임 Recipient/Operation으로 변환합니다.
        break;
    case ProjectileDeliveryStepStatus.Incomplete:
        // 이 Step에서 이동을 확정하지 않았습니다. 재조회 정책이 필요합니다.
        break;
    case ProjectileDeliveryStepStatus.Expired:
        // 이동 수명 만료. 프로젝트 World Owner에서 정리합니다.
        break;
    case ProjectileDeliveryStepStatus.Active:
        // 이번 Step이 접촉 없이 진행됐습니다.
        break;
}
~~~

- `QueryInitialOverlap(query, workspace, filter)`로 이동 시작 위치의 겹침을 먼저 검사할 수 있습니다. `Advance`도 시작 겹침을 확인한 후 Cast를 수행합니다.
- `SetVelocity`로 다음 Sweep 속도를 교체할 수 있습니다. 비선형 이동·중력·가속도는 프로젝트가 작은 직선 Sweep 구간으로 분할해 제공합니다.
- `Advance(deltaTime, query, workspace, filter)`는 `ConsumedTime`과 `RemainingTime`을 반환합니다. 접촉 시 실제 접촉 시각까지만 경과 시간을 적용합니다.
- `Incomplete`는 검색 완전성이 부족해 현재 Step의 이동을 확정하지 않았음을 나타냅니다. **기존 위치·시간을 소비한 것으로 간주하지 마세요.**
- `Contact` 후 반사·관통·종료·잔여 시간의 재소비는 프로젝트 정책입니다. Contact 자체가 Projectile이나 Effect를 자동 삭제하지 않습니다.
- `IsExpired`는 접촉 여부와 별개로 수명 종료를 나타냅니다. `ProjectileDeliveryStepStatus`는 Active, Contact, Expired, Incomplete 네 종류입니다.
- 2D와 3D는 개념적으로 동일하지만 Filter와 Volume, 입력 타입이 다릅니다. 하나의 Physics API를 두 차원 모두에 쓰지 않습니다.

## Gameplay Operation: 게임 결과의 의미를 소유하는 계층

`IGameplayOperation<TRecipient,TProjectContext>`는 다음 메서드를 구현합니다.

~~~csharp
GameplayOperationResult Invoke(
    in GameplayOperationContext<TRecipient, TProjectContext> context
);
~~~

`GameplayOperationContext<TRecipient,TProjectContext>(owner, instigator, origin, recipient, project)`는 지속 Owner·발생 주체·출처·실제 수신자·프로젝트 데이터의 의미를 분리합니다.

`GameplayOperationRunner.Invoke(operation, context)`가 게임 Operation을 호출합니다. operation이 null이면 `Rejected` 결과를 반환합니다. 실제 Damage·Heal·Buff 수치는 Operation 구현과 대상의 게임별 Rule이 결정합니다.

다음 예제는 게임별 변경 의미를 직접 구현하는 `IGameplayOperation`의 **완결된 C# 예제**입니다. 여기에서는 설명을 위해 수신자 ID별 진행량을 누적하지만, 같은 계약에서 Damage·Heal·Buff도 프로젝트 규칙으로 구현할 수 있습니다.

~~~csharp
using System;
using System.Collections.Generic;
using inonego.Xeri.Game;

// 실제 프로젝트에서는 Progress, Damage, Heal 등의 의미를 여기서 구현합니다.
public sealed class ExampleProgressOperation : IGameplayOperation<string, int>
{
    private readonly Dictionary<string, int> totals = new();

    public GameplayOperationResult Invoke(
        in GameplayOperationContext<string, int> context)
    {
        if (string.IsNullOrWhiteSpace(context.Recipient))
            return GameplayOperationResult.Reject("Recipient가 없습니다.");
        if (context.Project <= 0)
            return GameplayOperationResult.NoOp("변경량이 없습니다.");

        totals.TryGetValue(context.Recipient, out int previous);
        totals[context.Recipient] = previous + context.Project;
        return GameplayOperationResult.Applied();
    }

    public GameplayOperationResult Apply(
        object owner, object instigator, object origin, string recipient, int amount)
    {
        var context = new GameplayOperationContext<string, int>(
            owner, instigator, origin, recipient, amount);
        return GameplayOperationRunner.Invoke(this, context);
    }
}
~~~

호출자는 `ExampleProgressOperation.Apply(owner, instigator, origin, recipient, amount)`를 사용합니다. `Invoke`에서 `Recipient`와 `Project`를 해석하는 것은 프로젝트 책임이며, Xeri는 사용자의 진행량 딕셔너리나 체력 구조를 알지 못합니다.

`GameplayOperationResult` 상태는 `Applied`, `NoOp`, `Rejected`입니다. `Succeeded`는 Rejected가 아닌 경우를 포함하며 `NoOp`도 성공한 호출로 분류될 수 있습니다. `Reason`은 프로젝트 Operation이 설정한 이유를 전달합니다.

## 권장 수명·실패 규칙

- **Query Workspace 소유자**가 실행 구간에서 버퍼를 재사용하고 동시/중첩 접근을 방지합니다. Contact를 장기 보존해야 한다면 값과 실제 게임 Identity를 별도 데이터로 캡처합니다.
- **Projectile/World Owner**가 Tick·이동 갱신·종료·회수를 맡습니다. `ExecutionSession`이 끝난 뒤에도 독립 Projectile이 남을 수 있습니다.
- **Project Adapter**가 Collider를 Hurtbox/Entity 등 게임 도메인의 대상에 매핑하고 아군/면역/중복 Hit/최종 수치를 판정합니다.
- Operation 거부·부분 성공에 대한 보상, 피격 후 Effect·게임 룰·Spawn의 후속 작업은 Xeri Delivery의 책임이 아닙니다.

## 관련 문서

- [Gameplay Framework 전체 구조](overview.md)
- [Spatial·Targeting](spatial-targeting.md)
- [Execution](execution.md)
- [Ability에서 Operation까지 연결하는 가이드](../../guides/action/ability-execution-delivery.md)
