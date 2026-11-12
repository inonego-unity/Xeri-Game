# Xeri Execution — Flow·Behavior·Trigger

Execution은 **하나의 행동이 시작되고 진행되고 종료되는 수명**을 제공합니다. 지속 Ability 상태, 게임 Input·Motor, 배타 실행 슬롯, Spawn 결과를 직접 소유하지 않습니다. 네임스페이스는 `inonego.Xeri.Game`입니다.

## 핵심 계약

| 타입 | 역할 |
|---|---|
| `ExecutionDefinition` | 공유할 Flow와 Behavior 정의를 보관하고 독립 Session 생성 |
| `ExecutionFlowDefinition` / `ExecutionFlowRuntime` | 시간 진행·완료·Command 수락 정책 |
| `ExecutionBehaviorDefinition` / `ExecutionBehaviorRuntime` | 실행별 로직의 Begin·Advance·Signal·End 수명 |
| `ExecutionSession` | ID, Owner·Instigator, 시간, Flow, Behaviors, Variables, Commit·End, Child 소유 |
| `ExecutionSignal` / `ExecutionCommand` | Behavior 알림과 Flow 제어 요청을 구별하는 문자열 키 |
| `ExecutionTriggerDefinition` / `ExecutionTrigger` | 시간창·시간점·Signal 조건을 Runtime에서 평가 |
| `ExecutionEndReason` | Completed, Cancelled, Interrupted, Replaced |

`ExecutionContext(owner, instigator)`는 한 실행의 논리 Owner와 실제 실행자만 보관합니다. 세션의 개별 primitive 변수는 `ExecutionSession.Variables`(`ValueRegistry`)에 저장됩니다.

## Definition에서 Session 만들기

~~~csharp
using inonego.Xeri.Game;

object owner = new object();
object instigator = owner; // 게임에 따라 다른 실행 주체를 전달
float deltaTime = 0.016f; // 프로젝트의 실행 Tick

var definition = new ExecutionDefinition(
    new FixedTimelineExecutionFlowDefinition(duration: 0.6f)
);
definition.Normalize();

var session = definition.CreateSession(
    executionID: 1UL,
    context: new inonego.Xeri.Game.ExecutionContext(owner, instigator)
);

session.Begin();
session.Advance(deltaTime);
if (session.IsComplete)
{
    session.TryComplete();
}
~~~

`ExecutionDefinition.Flow`는 하나의 Flow 정의이며 `Behaviors`는 여러 Behavior 정의입니다. `CreateSession()`은 매번 **새 Flow Runtime과 새 Behavior Runtime**을 구성합니다. 공유 Definition 자체에 진행 상태를 보관하지 않습니다. 한 Runtime을 서로 다른 Session에 동시에 바인딩할 수 없습니다.

`ExecutionSession`을 직접 만들 수도 있지만 그때는 `ExecutionFlowRuntime` 인스턴스가 필요합니다. 기본적인 사용에는 `Definition.CreateSession`이 더 안전합니다.

## 시간 진행과 종료

1. `Begin()`: Flow부터 시작하고 Behavior를 등록 순서대로 시작합니다. 동일 Session의 Begin은 한 번만 허용합니다.
2. `Advance(deltaTime)`: `Time`을 최대 `MaximumTime`까지 전진시키며 **Flow → Behavior 순서**로 같은 `AppliedDelta`를 전달합니다. 요청한 `RequestedDelta`와 실제 `AppliedDelta`는 다를 수 있습니다.
3. `IsComplete`: Flow 완료 또는 최대 시간 상한에 도달한 Active Session을 가리킵니다. 상태 판정일 뿐 자동 종료가 아닙니다.
4. `TryComplete()`: `IsComplete`인 Session을 `Completed` 사유로 종료합니다.
5. `End(reason)`: 아직 Active인 Session을 명시한 이유로 종료합니다. 종료된 Session에 대한 반복 종료는 false입니다.

`ExecutionEndReason.None`은 명시적인 `End(reason)`에 사용할 수 없습니다. `Interrupted`·`Replaced` 같은 선택은 게임 Host의 행동 전환 정책입니다.

세션은 `PreviousTime`, `Time`, `RequestedDelta`, `AppliedDelta`, `MaximumTime`을 공개합니다. 시간값의 NaN·Infinity는 거부하고, 음수 Delta는 실행 시간을 역행시키지 않습니다. Begin 도중 또는 이미 실행 중인 `Advance` 재진입은 지원하지 않습니다.

## Flow 선택

### Fixed Timeline

`FixedTimelineExecutionFlowDefinition(duration)`은 고정 시간만큼 진행하고 완료합니다. 시간 진행 외 별도의 Command를 수락하지 않습니다.

### Hold / Release

`HoldReleaseExecutionFlowDefinition(releaseCommandKey, minHoldTime, maxHoldTime, releaseRecoveryDuration)`은 최소 유지 구간, 최대 유지 구간, 해제 후 회복 시간을 가집니다.

~~~csharp
var holdFlow = new HoldReleaseExecutionFlowDefinition(
    releaseCommandKey: "Release",
    minHoldTime: 0.2f,
    maxHoldTime: 1.0f,
    releaseRecoveryDuration: 0.3f
);
var holdDefinition = new ExecutionDefinition(holdFlow);
var holdSession = holdDefinition.CreateSession(2UL, new ExecutionContext(owner, instigator));
holdSession.Begin();

ExecutionCommandResult result =
    holdSession.Command(new ExecutionCommand("Release"));
~~~

- 최소 유지 시간이 지나기 전의 유효 Release Command는 `Deferred`가 될 수 있습니다. 유지 경계에 도달하면 해제됩니다.
- 허용된 해제는 `Accepted`, 알 수 없는 키·이미 해제된 상태는 `Rejected`입니다.
- 최대 유지 시간에 도달하면 명령 없이 자동 해제하며, `releaseRecoveryDuration` 이후 완료됩니다.
- 현재 Flow 상태는 `session.Flow.IsHolding`, `HasReleased`, `ReleaseTime`, `ReleaseProgress`로 읽습니다.
- Command 키는 프로젝트 정의 문자열입니다. Xeri가 Attack/Skill/Jump 열거형을 강제하지 않습니다.

## Command와 Signal은 서로 다릅니다

| API | 전달 대상 | 결과 |
|---|---|---|
| `session.Command(new ExecutionCommand(key))` | `Flow.Command` | `ExecutionCommandResult`: Accepted·Deferred·Rejected |
| `session.PublishSignal(new ExecutionSignal(key))` | 등록된 `Behavior.Signal` | 별도의 Command 판정·Flow 전이 없음 |

`Command`는 Flow가 해석하는 제어 입력입니다. `PublishSignal`은 Behavior가 관찰하는 이벤트이며, 호출 자체가 Trigger를 자동 생성·구독하지 않습니다. Signal은 유효한 Key가 필요하고 Active Session에서만 발행할 수 있습니다.

## Behavior와 Trigger의 소유권

`ExecutionBehaviorDefinition.CreateRuntime()`이 각 세션에 독립된 `ExecutionBehaviorRuntime`을 만듭니다. Runtime은 `Begin(session)`·`Advance(session, deltaTime)`·`Signal(session, signal)`·`End(session, reason)`을 구현할 수 있습니다.

Trigger는 **Behavior 또는 호출자가 직접 생성·구동**합니다. `ExecutionDefinition`에 Trigger 목록을 전달한다고 자동 설치되는 구조가 아닙니다. Behavior에서 필요한 Trigger Runtime을 만든 후 `Begin()`, `Signal(signal, time)`, `TryEvaluate(previousTime, currentTime, out effectiveTime)`, `End()`를 해당 수명에 맞춰 호출합니다.

| Trigger 정의 | 판정 |
|---|---|
| `ExecutionTimePointTriggerDefinition(time)` | 시간 지점을 처음 통과할 때 한 번 |
| `ExecutionTimeWindowTriggerDefinition(beginTime, endTime)` | 진행 구간이 반열린 시간창 [begin, end)와 교차할 때 |
| `ExecutionSignalTriggerDefinition(signalKey, offset)` | 첫 일치 Signal을 기록하고 Offset 이후 한 번 |

`TryEvaluate`가 false를 반환하면 `effectiveTime`은 유효한 이벤트 시각이 아닙니다. Trigger는 피해 적용 자체를 담당하지 않으며, 그 조건을 사용하는 Behavior가 프로젝트 작업을 호출합니다. Trigger 설정의 `Normalize(maximumDuration)`은 호출자가 적절한 시점에 수행해야 합니다.

### Trigger를 Behavior 실행 수명에 연결하기

다음 정의는 세션마다 별도 Trigger를 생성하고 실행 시간이 0.2초 지점을 처음 통과하면 프로젝트 callback을 한 번 호출합니다. 시간 샘플은 `ExecutionSession.PreviousTime`과 `Time`에서 받습니다.

~~~csharp
using System;
using inonego.Xeri.Game;

// Definition은 공유 설정이고, 상태가 있는 Trigger는 Runtime마다 새로 생성합니다.
public sealed class ExampleTimePointBehavior : ExecutionBehaviorDefinition
{
    private readonly Action<ExecutionSession, float> onReached;

    public ExampleTimePointBehavior(Action<ExecutionSession, float> onReached)
    {
        this.onReached = onReached ?? throw new ArgumentNullException(nameof(onReached));
    }

    public override ExecutionBehaviorRuntime CreateRuntime() => new Runtime(onReached);

    private sealed class Runtime : ExecutionBehaviorRuntime
    {
        private readonly Action<ExecutionSession, float> onReached;
        private readonly ExecutionTrigger trigger =
            new ExecutionTimePointTriggerDefinition(0.2f).CreateTrigger();

        public Runtime(Action<ExecutionSession, float> onReached)
        {
            this.onReached = onReached;
        }

        public override void Begin(ExecutionSession session) => trigger.Begin();

        public override void Advance(ExecutionSession session, float deltaTime)
        {
            if (trigger.TryEvaluate(
                session.PreviousTime, session.Time, out float effectiveTime))
            {
                onReached(session, effectiveTime);
            }
        }

        public override void End(ExecutionSession session, ExecutionEndReason reason)
        {
            trigger.End();
        }
    }
}
~~~

`ExecutionDefinition`의 `behaviors` 배열에 `new ExampleTimePointBehavior((session, time) => { /* 프로젝트 작업 */ })`를 전달하면 됩니다. Signal 기반 Trigger를 선택했다면 `ExecutionBehaviorRuntime.Signal()`에서 `trigger.Signal(signal, session.Time)`을 연결하고, `End()`에서 해제합니다.

## Commit·Child·이벤트

`session.TryCommit()`은 Active Session에서 단 한 번 `OnCommitted`를 알립니다. **Ability Activation의 Cost 소비와는 다른 동작**입니다. `OnBegun`은 Flow와 Behavior가 시작된 후, `OnEnded`는 종료 처리 후 사유와 함께 발행됩니다.

`session.AttachChild(child)`는 **이미 Active 상태인 Child**를 한 부모에만 연결합니다. 중복·자기 자신·순환 소유를 방지합니다. 부모가 끝나면 연결된 Child도 부모 수명에 따라 정리됩니다. `DetachChild(child)`는 연결만 해제하고 Child의 실행을 끝내지 않습니다.

Projectile, World Effect, 지속 오브젝트가 부모 실행과 독립적으로 살아야 한다면 프로젝트의 별도 Owner가 소유하게 하세요. 무조건 Child Session에 넣으면 부모 종료 시 함께 끝납니다.

## 실패와 정리

- Begin 시 Flow·Behavior·`OnBegun` 중 예외가 발생하면, 시작된 요소를 역순으로 정리하고 `Cancelled` 종료를 확정합니다. 정리 중에도 예외가 나면 AggregateException이 발생할 수 있습니다.
- `Advance`·Command·Signal·`OnCommitted` 중 예외가 발생하면 실패 종료로 수렴하고 예외를 호출자에게 전달합니다.
- `End`는 Behavior를 역순으로 정리한 뒤 Flow, 소유 Child, 종료 이벤트를 처리합니다. 외부 구독자와 게임 오브젝트가 만든 부작용까지 자동으로 보상하는 것은 아닙니다.
- 게임 Host는 실행 Slot·Animation·Movement·외부 Entity 소유권의 해제를 별도로 보장해야 합니다.

## 관련 문서

- [Gameplay Framework 전체 구조](overview.md)
- [Ability 사용 승인](ability.md)
- [Spatial·Targeting](spatial-targeting.md)
- [연결 가이드](../../guides/action/ability-execution-delivery.md)
