# Motion

Xeri Motion은 Actor 한 명의 운동 기여를 네 층으로 보관하고, Tick마다 하나의 `MotionFrame`으로 합성하는 Model입니다.

## 왜 필요한가

걷기, 대시, 루트 모션, 스턴, 슬로우, 넉백, 바람이 각자 Body를 바꾸면 서로 덮어씁니다. Motion은 기여를 합쳐지는 방식에 따라 층으로 나누고, 모든 입력을 적용한 뒤 Tick마다 한 번 합성합니다. 상태는 고정 용량 값 배열이라 서버 권한이나 클라이언트 예측처럼 Tick마다 Snapshot을 찍어도 할당이 없습니다.

## 언제 사용하는가

- 입력, AI, Ability, 영역 효과가 같은 Actor의 이동을 동시에 바꿀 때
- 스턴·슬로우·넉백을 이동 계산에서 일관되게 처리하고 싶을 때
- 대시나 공격 모션이 끝나면 걷기가 자동으로 이어져야 할 때
- Tick 단위로 상태를 저장하고 되돌려 다시 실행해야 할 때

## 네 층

| 층 | 입력 | 수명 | 합성 |
|---|---|---|---|
| ① 기본 의도 | `MotionIntent` | 1 Tick | 채널별 목표 속도. 없으면 그 채널은 Free |
| ② Override | `MotionOverrideCommand`의 `Begin`·`BeginTrack`·`End` | 지속 시간(초) 또는 종료 입력, Track은 끝에서 종료 | 우선순위가 높은 쪽이 채널을 가립니다. 같은 우선순위는 나중에 시작한 쪽이 위입니다. 어느 채널에서도 가려진 항목은 시간이 흐르지 않습니다. 끝나면 아래 층이 자동으로 복귀합니다 |
| ③ 제어 배율 | `MotionScaleCommand`의 `Add`·`Remove` | 지속 시간 또는 제거 입력 | 채널과 대상 층(기본 의도·Override)별로 Order 순서에 따라 적용합니다. 스턴은 `SET 0`, 슬로우는 `MUL`로 표현합니다 |
| ④ 외부 속도 | `MotionExternalCommand`의 `Set`·`Remove`·`Clear` | 지속 시간, 지수 감쇠 뒤 거의 0이면 종료 | Order 순서로 `SET`·`ADD`·`SUB`를 적용합니다. 같은 키를 다시 설정하면 갱신입니다(영역이 매 Tick 밀 때). 직전 Physics의 차단 법선 성분은 항목마다 제거합니다 |

입력은 층마다 다른 값 타입이라 층에 맞지 않는 필드를 만들 수 없습니다. 층끼리는 순서 의존이 없고(모든 입력을 적용한 뒤 한 번 합성), 같은 층 안에서는 입력 순서대로 적용합니다. 배율과 외부 속도의 연산은 Core `NumericFOperation`이고 규칙은 `NumericFModifier.Modify`와 같습니다.

`MotionStepInput.Impulse`(Tick당 합계)는 그 Tick에만 Body 속도를 한 번 바꿉니다.

## 채널별 합성

운동은 `Planar`, `Elevation`, `Rotation` 세 채널로 나뉩니다. 선속도는 Model의 **Up 축**을 기준으로 Planar(Up에 수직)와 Elevation(Up 방향)으로 분리합니다.

```text
기준 = 가장 위 Override ?? 기본 의도 ?? Free
Set  = 기준 × 제어 배율(기준 층) + 외부 속도
Free = Body 속도 유지 + (이번 외부 속도 − 이미 반영한 외부 속도)를 Impulse로
```

- 3D는 `MotionModel.WorldUp` 또는 원하는 축을 쓰고, 2D 위에서 보기는 `MotionModel.TopDown2DUp`(Z), 2D 옆에서 보기는 `WorldUp`(Y)을 씁니다. 옆에서 보기에서 수평만 의도하면 수직 채널이 Free라 중력이 유지됩니다.
- 스턴(배율 0) 중에도 외부 속도는 더해지므로 넉백으로 밀립니다.
- 각속도는 rad/s입니다. 외부 속도는 선속도만 다룹니다.
- `MotionFrame`에는 가장 위 Override의 `ActionID`와 시작 Tick, 기본 의도가 막힌 채널(`BlockedChannels`)이 들어 있어 표현과 복제에 쓸 수 있습니다.

## 기본 사용

```csharp
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Serializable;

var model = new MotionModel(actorKey, MotionModel.WorldUp);
var dash = new MotionSourceKey(id: 1, generation: 1);
var knockback = new MotionSourceKey(id: 2, generation: 1);

var buffer = new MotionInputBuffer();
buffer.Add(new MotionIntent(MotionChannel.Planar, moveVelocity));
buffer.Add(MotionOverrideCommand.Begin(dash, MotionChannel.Planar, priority: 10, dashVelocity, duration: 0.2f, actionID: 7));
buffer.Add(MotionExternalCommand.Set(knockback, order: 0, NumericFOperation.ADD, hitVelocity, decay: 6f));

// feedback: 직전 Physics의 회전·접지·차단 법선(Host가 채웁니다)
var frame = model.StepLocal(deltaTime, buffer.AsStepInput(), feedback);
buffer.Clear();

motor.TryApply(frame);
```

`MotionStepInput`은 ref struct라서 필드로 보관하거나 `[UnityTest]` 반복기 안에서 만들 수 없습니다. Host는 `MotionInputBuffer`에 입력을 모았다가 Step 직전에 `AsStepInput()`으로 묶습니다.

## 입력 결과 확인

입력별 결과가 필요하면 층별 결과 버퍼 `MotionStepStatuses`를 넘깁니다.

```csharp
var statuses = new MotionInputStatus[overrides.Length];
var frame = model.StepLocal
(
    deltaTime, new MotionStepInput(overrides: overrides), feedback,
    new MotionStepStatuses(overrides: statuses)
);
```

결과는 `Accepted`, `InvalidInput`, `StaleGeneration`, `Duplicate`, `NotFound`, `CapacityReached`, `UnknownTrack` 중 하나입니다. 층별 용량(기본 16)을 넘는 입력은 `CapacityReached`로 거절되고 기존 항목은 그대로입니다.

## 오래 유지하는 Override

사다리나 탑승처럼 해제 시점을 다른 코드가 정하는 Override는 `MotionInputBuffer.AcquireOverride`가 Core `Lease`를 돌려줍니다. Lease를 해제하면 종료 입력이 버퍼에 들어가고, 다음 Step에서 Override가 끝납니다.

## 루트 모션 Track

`MotionTrack`은 Actor 기준 로컬 누적 변위와 Yaw를 일정한 간격으로 담은 불변 데이터입니다. `MotionTrackLibrary`에 TrackID로 등록해 Model을 만들 때 넘기고 `MotionOverrideCommand.BeginTrack`으로 재생합니다. Track 변위는 `MotionFeedback.Rotation`으로 월드에 놓습니다. Animator 없이 평가되므로 서버에서도 같은 결과가 나옵니다. Track을 만드는 일(AnimationClip 굽기, 데이터 테이블 조회)은 사용처가 합니다.

## Snapshot과 Restore

`Capture()`, `Capture(snapshot)`, `Restore(snapshot)`로 층 상태를 저장하고 되돌립니다. 같은 용량의 Snapshot을 재사용하면 할당이 없습니다. 기본 의도는 1 Tick 수명이라 Snapshot에 넣지 않습니다. 다른 Actor나 다른 용량의 Snapshot은 `ArgumentException`입니다. `StepVersion`은 Step과 Restore마다 오르는 발급 토큰이라 되돌리지 않으며, 같은 Tick을 다시 실행한 결과와 이전 결과를 구분합니다.

## 소유권과 수명

- Model 하나는 `MotionActorKey` 하나를 소유합니다. `ScopeID`는 로컬 실행 인스턴스(서버·클라이언트) 구분이며 Network Object ID가 아닙니다.
- Track Library는 생성할 때 복사한 불변 데이터이고, Step 중 외부 코드를 호출하지 않습니다.
- 같은 슬롯 ID에 더 큰 Generation이 오면 이전 항목을 대신하고, 작은 Generation은 `StaleGeneration`입니다. 끝난 항목의 세대는 기억하지 않으므로 늦게 도착한 네트워크 입력의 중복 제거는 Adapter가 맡습니다.

## 실패와 거절

- 잘못된 Tick(`delta <= 0`, 마지막 Tick 이하의 번호), 유한하지 않은 Impulse, 입력보다 작은 결과 버퍼는 상태를 바꾸기 전에 예외로 거절합니다.
- 층별 입력의 값 검증 실패(유한하지 않은 속도, 잘못된 채널, 알 수 없는 Track 등)는 예외가 아니라 `InvalidInput`·`UnknownTrack` 같은 입력별 결과입니다. 나머지 입력은 계속 적용됩니다.
- 합성 결과가 Body에 적용될 수 있는 형태인지는 `MotionFrame.IsValid`로 확인할 수 있습니다. Motor의 Backend는 유효하지 않은 프레임을 거절합니다.

## 책임 범위

### 담당하는 것

- 네 층 합성, 입력 검증과 세대 규칙, 층별 용량
- 지속 시간과 지수 감쇠, 가려진 Override의 시간 정지, 자동 복귀
- 벽 법선 성분의 항목별 제거, Track 평가
- 고정 용량 Owned Snapshot

### 담당하지 않는 것

- 입력을 만드는 정책(입력·AI·Ability·Combat·영역 판정·Network)
- 이동 속도 스탯 계산(사용처가 계산해 배율 입력으로 넣습니다)
- Body 적용([Motor](motor.md)의 일)
- 네트워크 중복 제거와 Authority 검증

## 제약과 주의사항

- 같은 빌드와 같은 입력이면 같은 결과를 냅니다. 플랫폼 간 비트 단위 결정성은 약속하지 않습니다.
- 일시정지(`Time.timeScale = 0`) 중에는 Step을 호출하지 마세요. `delta <= 0`은 거절됩니다.
- Core의 `MValue`, `XOrdered`, `Timer`는 상태 저장소로 쓰지 않습니다. 이유는 [전체 구조](overview.md#연결-원칙)를 참고하세요.

## 관련 가이드

- [Tick 실행 가이드](../../guides/locomotion/run-a-tick.md)

## 관련 문서

- [Locomotion 전체 구조](overview.md) — 공통 정책과 확장 지점
- [Motor](motor.md) — 합성 결과 적용
- [Movement](movement.md) — 합성 결과가 만드는 이동 상태
