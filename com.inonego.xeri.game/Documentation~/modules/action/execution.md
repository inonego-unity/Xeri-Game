# Xeri Execution — Flow·Behavior·Trigger

Execution은 **하나의 행동이 시작되고 진행되고 종료되는 수명**을 제공합니다. 지속 Ability 상태, 게임 Input·Motor, Spawn 결과를 직접 소유하지 않습니다. 배타 실행은 Session을 소유하는 `ExecutionChannel`이 중재합니다. 네임스페이스는 `inonego.Xeri.Game`입니다.

## 핵심 계약

| 타입 | 역할 |
|---|---|
| `ExecutionSpec` | 공유할 Flow와 Behavior Spec을 보관하고 독립 Session 생성 |
| `ExecutionFlowSpec` / `ExecutionFlowRuntime` | 시간 진행·완료·Command 수락 정책 |
| `ExecutionBehaviorSpec` / `ExecutionBehaviorRuntime` | 실행별 로직의 Begin·Advance·Signal·End 수명 |
| `ExecutionSession` | ID, Owner·Instigator, 시간, Flow, Behaviors, Variables, Commit·End, Child 소유 |
| `ExecutionSignal` / `ExecutionCommand` | Behavior 알림과 Flow 제어 요청을 구별하는 문자열 키 |
| `ExecutionTriggerSpec` / `ExecutionTrigger` | 시간창·시간점·Signal 조건을 Runtime에서 평가 |
| `ExecutionEndReason` | Completed, Cancelled, Interrupted, Replaced |
| `ExecutionChannel<TClaim>` | 활성 Session을 최대 하나만 두고 선점 판정·이전 종료·새 시작 순서를 보장 |

`ExecutionContext(owner, instigator, mode = ExecutionMode.Authoritative)`는 한 실행의 논리 Owner와 실제 실행자, 실행 `Mode`만 보관합니다. 세션의 개별 primitive 변수는 `ExecutionSession.Variables`(`ValueRegistry`)에 저장됩니다.

`ExecutionMode`는 이 실행이 권위 실행(`Authoritative`)인지, 예측(`Predicted`)인지, 재시뮬레이션(`Resimulating`)인지를 구분하는 값입니다. Xeri는 Mode를 Context에 담아 Flow·Behavior에 전달할 뿐 Mode에 따라 동작을 바꾸지 않습니다. 예측·재시뮬레이션 중 RPC·Cue·Spawn·보상 같은 외부 부작용을 억제하는 일은 Mode를 읽는 Behavior와 Host의 책임입니다.

## Spec에서 Session 만들기

~~~csharp
using inonego.Xeri.Game;

object owner = new object();
object instigator = owner; // 게임에 따라 다른 실행 주체를 전달
float deltaTime = 0.016f; // 프로젝트의 실행 Tick

var spec = new ExecutionSpec(
    new FixedTimelineExecutionFlowSpec(duration: 0.6f)
);
spec.Normalize();

var session = spec.CreateSession(
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

`ExecutionSpec.Flow`는 하나의 Flow Spec이며 `Behaviors`는 여러 Behavior Spec입니다. `CreateSession()`은 매번 **새 Flow Runtime과 새 Behavior Runtime**을 구성합니다. 공유 Spec 자체에 진행 상태를 보관하지 않습니다. 한 Runtime을 서로 다른 Session에 동시에 바인딩할 수 없습니다.

`ExecutionSession`을 직접 만들 수도 있지만 그때는 `ExecutionFlowRuntime` 인스턴스가 필요합니다. 기본적인 사용에는 `Spec.CreateSession`이 더 안전합니다.

## 시간 진행과 종료

1. `Begin()`: Flow부터 시작하고 Behavior를 등록 순서대로 시작합니다. 동일 Session의 Begin은 한 번만 허용합니다.
2. `Advance(deltaTime)`: `Time`을 최대 `MaximumTime`까지 전진시키며 **Flow → Behavior 순서**로 같은 `AppliedDelta`를 전달합니다. 요청한 `RequestedDelta`와 실제 `AppliedDelta`는 다를 수 있습니다.
3. `IsComplete`: Flow 완료 또는 최대 시간 상한에 도달한 Active Session을 가리킵니다. 상태 판정일 뿐 자동 종료가 아닙니다.
4. `TryComplete()`: `IsComplete`인 Session을 `Completed` 사유로 종료합니다.
5. `End(reason)`: 아직 Active인 Session을 명시한 이유로 종료합니다. 종료된 Session에 대한 반복 종료는 false입니다.

`Begin` 도중(Flow·Behavior의 `Begin` 안)에 `End(...)`를 호출하면 `InvalidOperationException`이 발생하고, `TryComplete()`는 `false`를 반환합니다.

`ExecutionEndReason.None`은 명시적인 `End(reason)`에 사용할 수 없습니다. `Interrupted`·`Replaced` 같은 선택은 게임 Host의 행동 전환 정책입니다. `ExecutionChannel`을 쓰면 프로젝트의 판정 함수가 이 선택을 반환합니다.

세션은 `PreviousTime`, `Time`, `RequestedDelta`, `AppliedDelta`, `MaximumTime`을 공개합니다. 시간값의 NaN·Infinity는 거부하고, 음수 Delta는 실행 시간을 역행시키지 않습니다. Begin 도중 또는 이미 실행 중인 `Advance` 재진입은 지원하지 않습니다.

## Flow 선택

### Fixed Timeline

`FixedTimelineExecutionFlowSpec(duration)`은 고정 시간만큼 진행하고 완료합니다. 시간 진행 외 별도의 Command를 수락하지 않습니다.

### Hold / Release

`HoldReleaseExecutionFlowSpec(releaseCommandKey, minHoldTime, maxHoldTime, releaseRecoveryDuration)`은 최소 유지 구간, 최대 유지 구간, 해제 후 회복 시간을 가집니다.

~~~csharp
var holdFlow = new HoldReleaseExecutionFlowSpec(
    releaseCommandKey: "Release",
    minHoldTime: 0.2f,
    maxHoldTime: 1.0f,
    releaseRecoveryDuration: 0.3f
);
var holdSpec = new ExecutionSpec(holdFlow);
var holdSession = holdSpec.CreateSession(2UL, new ExecutionContext(owner, instigator));
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

`Command`는 Flow가 해석하는 제어 입력입니다. `PublishSignal`은 Behavior가 관찰하는 이벤트이며, 호출 자체가 Trigger를 자동 생성·구독하지 않습니다. Signal은 유효한 Key가 필요하고 Active Session에서만 발행할 수 있습니다. Begin 도중(Flow·Behavior의 `Begin` 안)에는 `PublishSignal`이 `InvalidOperationException`을 던지고 `Command`는 `Rejected`를 반환하므로, 시작 직후의 입력은 Begin이 끝난 뒤 보내세요.

같은 연산의 중첩 호출은 거부합니다. Signal을 전달하는 동안(`Behavior.Signal` 안에서) `PublishSignal`을 다시 부르거나, Command를 처리하는 동안(`Flow.Command` 안에서) `Command`를 다시 부르면 `InvalidOperationException`이 발생합니다. 이 예외는 바깥 호출의 실패로 전파되므로 실패 종료 규칙에 따라 Session이 `Cancelled`로 끝납니다. 비활성 Session이나 유효하지 않은 Command에 `Command`가 `Rejected`를 반환하는 것과는 다른 경로입니다. 서로 다른 연산(예: Signal 처리 중 Command)은 막지 않으며, 연쇄 Signal이 필요하면 호출자가 Tick을 나누거나 Behavior 안에서 직접 처리하세요.

## Behavior와 Trigger의 소유권

`ExecutionBehaviorSpec.CreateRuntime()`이 각 세션에 독립된 `ExecutionBehaviorRuntime`을 만듭니다. `ExecutionSpec.CreateSession`은 타입을 고르지 않은 빈 Behavior 항목(null)은 건너뛰지만, `CreateRuntime()`이 null을 반환하면 Flow와 같이 `InvalidOperationException`으로 거부합니다. Runtime은 `Begin(session)`·`Advance(session, deltaTime)`·`Signal(session, signal)`·`End(session, reason)`을 구현할 수 있습니다.

Trigger는 **Behavior 또는 호출자가 직접 생성·구동**합니다. `ExecutionSpec`에 Trigger 목록을 전달한다고 자동 설치되는 구조가 아닙니다. Behavior에서 필요한 Trigger Runtime을 만든 후 `Begin()`, `Signal(signal, time)`, `TryEvaluate(previousTime, currentTime, out effectiveTime)`, `End()`를 해당 수명에 맞춰 호출합니다.

| Trigger Spec | 판정 |
|---|---|
| `ExecutionTimePointTriggerSpec(time)` | 시간 지점을 처음 통과할 때 한 번 |
| `ExecutionTimeWindowTriggerSpec(beginTime, endTime)` | 진행 구간이 반열린 시간창 [begin, end)와 교차할 때 |
| `ExecutionSignalTriggerSpec(signalKey, offset)` | 첫 일치 Signal을 기록하고 Offset 이후 한 번 |

시간 기반 Trigger(TimePoint·Window)는 시간이 앞으로 흐른 구간(`previousTime < currentTime`)에서만 발동하므로 `Advance(0)`처럼 시간이 흐르지 않은 Tick에서는 발동하지 않습니다. 0초 지점 TimePoint도 첫 양의 진행에서 발동합니다. Signal Trigger는 기록한 Signal 시각에 Offset을 더한 시각이 지나면 시간이 흐르지 않은 Tick에서도 발동합니다. `TryEvaluate`가 false를 반환하면 `effectiveTime`은 유효한 이벤트 시각이 아닙니다. Trigger는 피해 적용 자체를 담당하지 않으며, 그 조건을 사용하는 Behavior가 프로젝트 작업을 호출합니다. Trigger 설정의 `Normalize(maximumDuration)`은 호출자가 적절한 시점에 수행해야 합니다.

### Trigger를 Behavior 실행 수명에 연결하기

다음 Spec은 세션마다 별도 Trigger를 생성하고 실행 시간이 0.2초 지점을 처음 통과하면 프로젝트 callback을 한 번 호출합니다. 시간 샘플은 `ExecutionSession.PreviousTime`과 `Time`에서 받습니다.

~~~csharp
using System;
using inonego.Xeri.Game;

// Spec은 공유 설정이고, 상태가 있는 Trigger는 Runtime마다 새로 생성합니다.
public sealed class ExampleTimePointBehavior : ExecutionBehaviorSpec
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
            new ExecutionTimePointTriggerSpec(0.2f).CreateTrigger();

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

`ExecutionSpec`의 `behaviors` 배열에 `new ExampleTimePointBehavior((session, time) => { /* 프로젝트 작업 */ })`를 전달하면 됩니다. Signal 기반 Trigger를 선택했다면 `ExecutionBehaviorRuntime.Signal()`에서 `trigger.Signal(signal, session.Time)`을 연결하고, `End()`에서 해제합니다.

## Commit·Child·이벤트

`session.TryCommit()`은 Active Session에서 단 한 번 `OnCommitted`를 알립니다. **Ability Activation의 Cost 소비와는 다른 동작**입니다. `OnBegun`은 Flow와 Behavior가 시작된 후, `OnEnded`는 종료 처리 후 사유와 함께 발행됩니다.

`session.AttachChild(child)`는 **이미 Active 상태인 Child**를 한 부모에만 연결합니다. 중복·자기 자신·순환 소유를 방지합니다. 부모가 끝나면 연결된 Child도 부모 수명에 따라 정리됩니다. `DetachChild(child)`는 연결만 해제하고 Child의 실행을 끝내지 않습니다.

Projectile, World Effect, 지속 오브젝트가 부모 실행과 독립적으로 살아야 한다면 프로젝트의 별도 Owner가 소유하게 하세요. 무조건 Child Session에 넣으면 부모 종료 시 함께 끝납니다.

## 배타 실행 채널

`ExecutionChannel<TClaim>`은 한 채널에 활성 Session을 최대 하나만 둡니다. 별도 Token 없이 Session을 직접 소유하고, Session이 어떤 이유로든 끝나면 `OnEnded`로 채널이 비워집니다.

```csharp
var channel = new ExecutionChannel<int>
(
    (current, next) => next > current
        ? ExecutionChannelDecision.Replace
        : ExecutionChannelDecision.Reject
);

if (channel.CanStart(priority))                         // 부작용 없는 사전 검사
{
    channel.TryStart(spec.CreateSession(id, context), priority);
}
```

- `TClaim`은 우선순위·행동 종류처럼 프로젝트가 정하는 점유 정보입니다. 채널은 해석하지 않고 판정 함수 `(현재 Claim, 요청 Claim)`에 넘깁니다. 빈 채널에는 판정 없이 시작합니다.
- 선점은 **이전 Session `End(Replaced/Interrupted)` → 그 `OnEnded` → 새 Session `Begin()`** 순서입니다.
- 판정·이전 종료·새 시작 중(`IsTransitioning`)에는 `TryStart`가 `false`입니다. 이전 Session의 종료 통지나 새 Session의 시작 안에서 다시 선점할 수 없습니다.
- 이미 시작한 Session은 받지 않습니다(`ArgumentException`).
- 이전 종료 정리가 실패하면 이전 Session은 끝난 채로 채널이 비고 새 Session은 시작하지 않습니다. 새 Session 시작이 실패하면 Session이 `Cancelled`로 정리되며 채널이 비웁니다. 두 경우 모두 예외를 그대로 전달하고 이전 Session을 되살리지 않습니다.
- Ability `TryActivate`의 `begin` 콜백에서 `channel.TryStart(...)`를 반환하면 거절·시작 실패·이전 종료 실패가 모두 비용 환급 경로를 탑니다.
- Session이 아닌 배타 행동(점프, 착지 회복 등)과 여러 채널의 원자적 동시 점유는 다루지 않습니다.

## 실패와 정리

- Begin 시 Flow·Behavior·`OnBegun` 중 예외가 발생하면, 시작된 요소를 역순으로 정리하고 `Cancelled` 종료를 확정합니다. `Begin`이 예외를 던진 Flow·Behavior 자신도 정리 대상이라 `End(Cancelled)`를 받으므로, `End`는 시작이 중간에 끝난 상태에서도 안전해야 합니다. 종료는 정리보다 먼저 확정되므로 정리 콜백 안에서 호출한 `End`는 예외 없이 `false`를 반환합니다. 정리 중에도 예외가 나면 AggregateException이 발생할 수 있습니다.
- `Advance`·Command·Signal·`OnCommitted` 중 예외가 발생하면 실패 종료(`Cancelled`)로 수렴하고 예외를 호출자에게 전달합니다. 완료 판정은 `TryComplete()` 안에서 `IsComplete`를 읽을 때만 같은 규칙을 따릅니다. `IsComplete` 프로퍼티를 직접 읽는 중 Flow가 던진 예외는 그대로 전파되며, Session은 자동으로 종료되지 않습니다.
- `OnBegun`·`OnCommitted`는 구독 순서대로 호출하며, 앞선 구독자가 Session을 종료하면 남은 구독자에게는 알리지 않습니다. 남은 구독자는 시작·확정 통지 없이 `OnEnded`만 받을 수 있으므로 `OnEnded`는 시작 통지 없이도 정리할 수 있어야 합니다.
- `End`는 Behavior를 역순으로 정리한 뒤 Flow, 소유 Child, 종료 이벤트를 처리합니다. 외부 구독자와 게임 오브젝트가 만든 부작용까지 자동으로 보상하는 것은 아닙니다.
- 게임 Host는 `ExecutionChannel`로 다루지 않는 실행 Slot과 Animation·Movement·외부 Entity 소유권의 해제를 별도로 보장해야 합니다.

## 관련 문서

- [Gameplay Framework 전체 구조](overview.md)
- [Ability 사용 승인](ability.md)
- [Spatial·Targeting](spatial-targeting.md)
- [연결 가이드](../../guides/action/ability-execution-delivery.md)
