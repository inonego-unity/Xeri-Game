# Xeri Game Motion — 운동 합성 Model

> Actor 하나의 운동 기여를 네 층으로 보관하고 Tick마다 하나의 `MotionFrame`으로 합성한다. Body 적용·Contact·Ground Support는 [Motor Runtime](../Motor/README.md), 이동 상태·Sample은 [Movement Runtime](../Movement/README.md)이 맡는다. 입력을 만드는 Producer(입력·AI·Ability·Combat·영역 판정·Network)는 사용처 소유이며 이 모듈은 그 타입을 참조하지 않는다.

## 왜 필요한가

걷기·대시·루트 모션·스턴·슬로우·넉백·바람 같은 여러 시스템이 한 Actor를 동시에 움직일 때, 각자 Body를 직접 바꾸면 서로 덮어쓴다(예: 걷기가 넉백을 지움). Model은 기여를 **어떻게 합쳐지는지**에 따라 층으로 나누고 Tick마다 한 번 합성한다. 상태는 고정 용량 값 배열이라 서버 권한·클라이언트 예측의 Tick별 Snapshot을 할당 없이 찍을 수 있다.

## 네 층

| 층 | 입력 | 수명 | 합성 |
| --- | --- | --- | --- |
| ① Intent | `MotionIntent` | 1 Tick | 채널별 목표 속도. 없으면 그 채널은 Free |
| ② Override | `MotionOverrideCommand.Begin`·`BeginTrack`·`End` | 지속 시간(초) 또는 종료 입력. Track은 끝에서 종료 | 우선순위가 높은 쪽이 채널을 가림. 같은 우선순위는 나중에 시작한 쪽. 어느 채널에서도 가려지면 시간이 흐르지 않음. 끝나면 아래 층이 자동 복귀 |
| ③ 제어 배율 | `MotionScaleCommand.Add`·`Remove` | 지속 시간 또는 제거 | 채널·대상 층(Intent/Override)별로 Order 순서 적용. 연산은 Core `NumericFOperation`, 규칙은 `NumericFModifier.Modify`와 같음. 스턴=`SET 0`, 슬로우=`MUL` |
| ④ 외부 속도 | `MotionExternalCommand.Set`·`Remove`·`Clear` | 지속 시간, 지수 감쇠 후 거의 0이면 종료 | Order 순서로 `SET`·`ADD`·`SUB`. 같은 키 재입력은 갱신(영역의 매 Tick 갱신용). 직전 Physics의 차단 법선 성분은 항목마다 제거 |

`MotionStepInput.Impulse`(Tick당 합계)는 그 Tick에만 Body 속도를 한 번 바꾼다.

입력은 층마다 다른 값 타입이라 층에 맞지 않는 필드를 만들 수 없다. 층끼리는 순서 의존이 없고(모든 입력을 적용한 뒤 한 번 합성), 같은 층 안에서는 Span 순서대로 적용한다.

### 채널별 합성

~~~text
기준 = 가장 위 Override ?? Intent ?? Free
Set  = 기준 × 제어 배율(기준 층) + 외부 속도
Free = Body 속도 유지 + (이번 외부 속도 − 이미 반영한 외부 속도)를 Impulse로
~~~

- 선속도는 Model의 **Up 축**으로 Planar·Elevation을 나눈다. Up 축과 차단 법선은 Core `VectorHelper.TryNormalize`(double 계산)로 정규화해 실행 환경(Mono·.NET)과 관계없이 같은 판정을 낸다. 3D는 `WorldUp` 또는 원하는 축, 2D 위에서 보기는 `TopDown2DUp`(Z), 2D 옆에서 보기는 `WorldUp`(Y)이다. 옆에서 보기에서 수평만 의도하면 수직은 Free라 중력이 유지된다.
- 스턴(배율 0) 중에도 외부 속도는 더해지므로 넉백으로 밀린다.
- 각속도는 rad/s다. 외부 속도는 선속도만 다룬다.
- `MotionFrame`에는 가장 위 Override의 `ActionID`·시작 Tick과 기본 의도가 막힌 채널(`BlockedChannels`)이 들어 있어 표현·복제에 쓴다.

## 사용 예

```csharp
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Serializable;

var model = new MotionModel(actor, MotionModel.WorldUp);
var dash = new MotionSourceKey(id: 1, generation: 1);
var knockback = new MotionSourceKey(id: 2, generation: 1);

var buffer = new MotionInputBuffer();
buffer.Add(new MotionIntent(MotionChannel.Planar, moveVelocity));
buffer.Add(MotionOverrideCommand.Begin(dash, MotionChannel.Planar, priority: 10, dashVelocity, duration: 0.2f, actionID: DashAction));
buffer.Add(MotionExternalCommand.Set(knockback, order: 0, NumericFOperation.ADD, hitVelocity, decay: 6f));

// feedback: 직전 Physics의 회전·접지·차단 법선(Host가 채움)
var frame = model.Step(tick, buffer.AsStepInput(), feedback);
buffer.Clear();
motor.TryApply(frame);
```

- 입력별 결과가 필요하면 층별 결과 버퍼 `MotionStepStatuses`를 넘긴다(`Accepted`, `InvalidInput`, `StaleGeneration`, `Duplicate`, `NotFound`, `CapacityReached`, `UnknownTrack`).
- 오래 유지하는 Override는 `MotionInputBuffer.AcquireOverride`가 Core `Lease`를 돌려준다. 해제하면 종료 입력을 버퍼에 넣는다.
- 루트 모션은 `MotionTrack`(로컬 누적 변위·Yaw 샘플)을 `MotionTrackLibrary`에 넣어 Model 생성 시 넘기고 `MotionOverrideCommand.BeginTrack`으로 재생한다. Track 변위는 `MotionFeedback.Rotation`으로 월드에 놓는다. Animator 없이 평가되므로 서버에서도 같은 결과다.

**폴더 구성:** `Motion/`(`MotionModel`·`MotionModelSnapshot`), `Input/`(층별 입력 값·`MotionInputBuffer`), `Frame/`(합성 결과 `MotionFrame`·관측 `MotionFeedback`), `Track/`(`MotionTrack`), `Common/`(채널·식별자·Tick). 네임스페이스는 폴더와 관계없이 `inonego.Xeri.Game.Motion` 하나다.

## 책임과 제약

- **Model 하나는 `MotionActorKey` 하나**를 소유한다. ScopeID는 로컬 실행 인스턴스(서버·클라이언트) 구분이지 Network Object ID가 아니다.
- **입력은 층별 `ReadOnlySpan` 값 묶음(`MotionStepInput`, ref struct)**이고 Step 중 외부 코드를 호출하지 않는다. ref struct라 필드로 보관하거나 `[UnityTest]` 반복기 안에서 만들 수 없으므로 Host는 `MotionInputBuffer`에 모았다가 Step 직전에 `AsStepInput()`으로 묶는다. Track Library도 생성 시 복사한 불변 데이터다.
- **Tick**: `Step`은 마지막 Tick보다 큰 번호만, `StepLocal`은 다음 번호를 쓴다. `delta <= 0`(일시정지의 `Time.deltaTime = 0` 포함)은 거절한다. 일시정지 중에는 Host가 Step을 호출하지 않는다.
- **세대**: 같은 슬롯 ID에 더 큰 Generation이 오면 이전 항목을 대신하고, 작은 Generation은 `StaleGeneration`이다. 끝난 항목의 세대는 기억하지 않으므로 늦게 도착한 네트워크 입력의 중복 제거는 Adapter 책임이다.
- **용량**: 층별 상한은 생성 시 정한다(기본 16). 넘으면 `CapacityReached`로 거절하고 기존 항목은 그대로다.
- **Snapshot**: `Capture()`/`Capture(snapshot)`/`Restore(snapshot)`. 같은 용량 Snapshot을 재사용하면 할당이 없다. 기본 의도는 1 Tick 수명이라 넣지 않는다. 다른 Actor·다른 용량은 `ArgumentException`이다.
- **StepVersion**은 Step과 Restore마다 오르는 발급 토큰이라 Restore로 되돌리지 않는다. 같은 Tick을 재실행한 결과와 이전 결과를 구분한다.
- **결정성**: 같은 빌드·같은 입력이면 같은 결과다. 플랫폼 간 비트 결정성은 약속하지 않는다.
- Core `MValue`·`XOrdered`·`Timer`를 상태 저장소로 쓰지 않는다(Tick마다 Snapshot을 찍는 구조에서 객체 복제·이벤트 연쇄가 생김). 이동 속도 스탯은 사용처의 `MValue`로 계산해 배율 입력으로 넣는다.

## 검증

[`TEST_MotionModel.cs`](../../../../Tests/EDIT/Modules/Locomotion/Motion/TEST_MotionModel.cs)가 층 합성, 대시 복귀, 가려진 Override 시간 정지, 스턴 중 넉백, 배율 순서·대상 층, 넉백 감쇠, 외부 속도 순서·갱신, Free 채널 변화량, 벽 법선 항목별 제거, Track 평가, 입력 검증, Snapshot 재실행 일치를 검증한다. 실제 Physics를 거친 흐름은 [`TEST_MovementScenario.cs`](../../../../Tests/PLAY/Modules/Locomotion/Movement/TEST_MovementScenario.cs)에 있다. 테스트 수와 검증 수준은 [구현 플랜](../../../../../Docs/systems/movement/implementation-plan.md), 공통 정책은 [시스템 설계 §3](../../../../../Docs/systems/movement/game-module-design.md)에 있다.
