# Motor

Xeri Motor는 Motion이 합성한 결과(`MotionFrame`)를 Actor의 Body에 Tick당 한 번 적용하고, Physics Step 뒤의 Body 상태·접촉·지면을 관측하는 경계입니다.

## 왜 필요한가

여러 시스템이 Rigidbody 속도를 직접 바꾸면 합성 결과가 사라집니다. Motor는 Body를 쓰는 곳을 하나로 만들고, 쓰는 시점(Physics Step 사이 한 번)과 관측 수명(어느 Step의 접촉·지면 표본이 유효한지)을 관리합니다. 서버 교정이나 순간이동으로 Body가 바뀌면 이전 관측을 무효화하고 불연속을 기록해 표현 쪽이 판단할 수 있게 합니다.

## 언제 사용하는가

- Motion의 합성 결과를 Rigidbody2D/3D에 적용할 때
- Physics Step 뒤의 실제 접촉과 지면 속도를 다음 Tick의 이동 계산에 되돌릴 때
- 서버가 확정한 Body 상태를 클라이언트에 즉시 적용(교정)해야 할 때

Body를 직접 제어하는 코드와 섞어 쓰는 구성에는 맞지 않습니다. Body 속도를 쓰는 코드는 Motion 입력으로 옮기세요.

## 기본 사용

```csharp
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Game.Motor;

var motor = new Motor<MotorState3D>(actorKey, new RigidbodyMotorBackend3D(rigidbody, probe));
var contacts = new MotorContactReader<MotorState3D, ContactPoint>(motor);
var buffer = new ContactPoint[16];

// Tick마다
motor.TryApply(frame);                       // 합성 결과 적용(다음 BeginPhysicsStep 전 1회)
motor.BeginPhysicsStep();                    // 이전 접촉·지면 관측 무효화
// ... Physics World 소유자가 Step을 정확히 한 번 실행 ...
motor.CompletePhysicsStep(frame.Tick.Index); // 완료했다는 Host의 선언

if (contacts.Read(buffer, out var count) == MotorContactReadStatus.Ready)
{
    // buffer[0..count)가 이번 Step의 접촉
}

var state = motor.ReadCurrentState();
```

2D는 `RigidbodyMotorBackend2D(rigidbody2D)`, `MotorState2D`, `ContactPoint2D`를 같은 방식으로 씁니다. 전체 Tick 흐름은 [Tick 실행 가이드](../../guides/locomotion/run-a-tick.md)를 따르세요.

## 핵심 구조

```text
Motor<TState> ── IMotorBackend<TState> ── RigidbodyMotorBackend2D / 3D
     │                                       (선택) MotorCollisionProbe3D
     ├─ MotorContactReader<TState,TContact>   Step의 실제 접촉 읽기
     ├─ MotorContactFeedbackEvaluator         접촉 법선과 의도·실제 속도로 Blocking·Slide 관측
     └─ MotorGroundSupport<TState> (Adapter)  IGroundChecker 결과를 현재 Step에 연결
```

- `Motor<TState>`는 Actor 하나의 Body 경계입니다. `TState`는 `MotorState2D`나 `MotorState3D`처럼 Body의 현재 상태 값입니다.
- `IMotorBackend<TState>`가 실제 Body 읽기·쓰기를 맡고, 접촉 수명(`IMotorContactBackend`)과 과거 복원(`IMotorHistoricalBackend`)은 Backend가 선택적으로 구현합니다.
- 적용 규칙은 `MotionFrame.ComposeLinear`와 `ComposeAngular` 하나를 두 Backend가 공유합니다.

### 적용 규칙

| 채널 | `Set` | `Free` |
|---|---|---|
| Planar | Up 축에 수직인 속도로 설정 | 현재 Body 속도의 Planar 성분 유지 |
| Elevation | Up 축 방향 속도로 설정 | 현재 Body 속도의 Up 성분 유지(중력 등) |
| Rotation | 각속도(rad/s)로 설정. 2D는 Z 성분을 도/초로 바꿔 씀 | 현재 각속도 유지 |

채널을 적용한 뒤 `Impulse`를 더하고 선속도와 각속도를 **한 번에** 씁니다. `MotionFrame.IsValid`(유한한 값, 단위 Up, 정의된 채널 방식, 유효한 Actor·Tick)를 통과하지 못하거나 계산 결과(축 투영·합산 오버플로 포함)가 유한하지 않으면 어떤 값도 쓰지 않고 `false`를 반환합니다. 2D Backend는 X·Y 선속도와 Z 각속도만 씁니다.

## Physics Step과 관측 순서

`BeginPhysicsStep`과 `CompletePhysicsStep`은 `Physics.Simulate`를 호출하지 않습니다. 실제 물리 Step의 소유자(Unity 자동 실행 또는 Host)가 그 앞뒤에서 호출해야 합니다.

- `TryApply`는 결과의 Actor가 Motor의 Actor와 다르거나 Backend가 거절하면 `false`입니다. Physics Step 중이거나 이미 적용했다면 `InvalidOperationException`입니다.
- `BeginPhysicsStep`을 진행 중인 Step에서 다시 호출하면 예외이고 기존 관측을 덮어쓰지 않습니다. Backend의 `BeginContactStep`이 예외를 던지면 Step을 시작한 것으로 확정하지 않아 다시 시도할 수 있습니다.
- `CompletePhysicsStep`은 `BeginPhysicsStep` 없이 호출하면 예외입니다. 다만 실제로 Physics가 진행됐는지는 검증하지 못합니다. 같은 `FixedUpdate` 콜백에서 Begin과 Complete를 연달아 호출해 Physics가 끝났다고 가정하지 마세요.
- `ObservedPhysicsStep`과 `PhysicsObservationGeneration`으로 관측의 유효성을 판단합니다. `BeginPhysicsStep`과 성공한 교정·복원은 기존 관측을 지우고 Generation을 올립니다. `None`은 관측 없음 표식이라 `PhysicsObservationGeneration`의 첫 번호는 1입니다. 같은 Step 번호를 다시 써도 이전 표본을 재사용하지 않습니다.

## 접촉 읽기

접촉은 `MotorContactReader`가 읽고, Motor는 표본의 시작(`BeginPhysicsStep`)과 무효화(교정·복원)만 관리합니다.

| 결과 | 의미 |
|---|---|
| `Ready` | 현재 Step의 접촉 관측이 끝났습니다. `count`가 0이면 접촉이 없었다는 유효한 결과입니다 |
| `Unsupported` | Backend가 지금 접촉을 제공하지 않습니다(예: 3D에서 Probe가 없거나 비활성) |
| `NotObserved` | 아직 유효한 Step이 없습니다. 교정 직후, 읽는 도중 관측 세대 변경, 새 Step 없이 센서만 다시 켠 경우입니다 |

- 접촉 배열은 **호출자 소유**입니다. 반복해서 재사용하세요. Backend가 해당 접촉 형식을 제공하지 않으면 Reader를 만들 때 `ArgumentException`입니다(예: 2D Motor에 `ContactPoint`).
- **2D**는 `Rigidbody2D.GetContacts`로 현재 접촉을 직접 읽습니다. Backend가 접촉을 보관하지 않아 수명 메서드는 비어 있고, 유효성은 Motor의 Step 수명이 보장합니다.
- **3D** Rigidbody에는 접촉을 직접 조회하는 API가 없어서 같은 GameObject의 활성 `MotorCollisionProbe3D`가 충돌 콜백(`OnCollisionEnter/Stay`)으로 접촉을 모읍니다. Probe는 선택 사항이며, 없으면 접촉 읽기는 `Unsupported`입니다. 한 Step에 최대 32개를 보관하고 `ContactCount`와 `Truncated`로 절단을 확인할 수 있습니다. Probe를 비활성화하면 저장된 접촉을 비우고 관측 세대를 올려, 새 Step 없이 다시 켜도 오래된 접촉을 내보내지 않습니다.
- `MotorContactFeedbackEvaluator`는 접촉 법선과 의도·실제 속도에서 **첫 차단 법선 하나**를 골라 Blocking, Slide, 속도 오차를 계산합니다. 관측용이며 충돌 Solver가 아닙니다. 속도가 유한하지 않으면 `ArgumentOutOfRangeException`입니다.

Host는 이 관측으로 다음 Tick의 `MotionFeedback`(회전·접지·차단 법선)을 채웁니다. 효과를 소유한 사용처는 `MotionFeedback.RemoveBlocked`로 넉백 같은 값에서 벽으로 향하는 성분을 빼고, 그 결과를 보관해 Modifier 값으로 넣습니다.

## 지면 지원

`MotorGroundSupport<TState>`(`inonego.Xeri.Game.Motor.Adapter`)는 기존 `IGroundChecker`의 `Check`와 `GetGroundPointVelocity` 결과를 재계산 없이 현재 Step에 연결합니다.

```csharp
using inonego.Xeri.Game.Motor.Adapter;

var support = new MotorGroundSupport<MotorState3D>(motor, groundChecker);

// CompletePhysicsStep 뒤에
if (support.CheckAndObserve(step, deltaTime, worldPoint) && support.TryRead(out var ground))
{
    // ground.IsOnGround, ground.GroundPointVelocity
}
```

- 완료된 Step에만 Checker를 실행하고, 접지가 참일 때만 지점 속도를 한 번 읽습니다. 지면 속도를 선속도와 각속도로 다시 합성하거나 Body에 자동으로 더하지 않습니다.
- 같은 Step과 World Point를 다시 요청하면 Checker를 다시 실행하지 않고 캐시된 표본을 씁니다. 같은 Step에서 **다른 Point**를 요청하면 `false`이며, 다른 Point를 측정하려면 새 Step이 필요합니다.
- Checker가 없으면 `HasChecker=false`, `IsOnGround=false`, 지면 속도 0인 정상 표본입니다.
- Checker 이벤트나 Owner 조회 콜백 안에서 교정이나 관측을 다시 요청하면 `InvalidOperationException`입니다. 관측한 Checker의 GameObject가 바뀌면 그 Step의 표본을 영구히 무효화하므로, A에서 B로 갔다가 A로 돌아와도 이전 속도가 되살아나지 않습니다. 새 Owner의 관측은 다음 Step부터 시작합니다.
- Checker 초기화·해제, 같은 프레임에 다른 시스템이 `Check`를 중복 호출하지 않게 하는 조율, 이동 플랫폼 속도를 물려받을지 같은 정책은 Host가 정합니다. 물려받는 연결 방법은 [Motion의 이동 플랫폼](motion.md#이동-플랫폼)에 있습니다.

## 현재 상태 교정과 과거 복원

- `ApplyCurrentState(in state)`는 권한 있는 Body의 현재 Pose와 속도를 **지금** 적용합니다. 성공하면 이전 Motor History 세대, 접촉·지면 관측이 무효화되고 `DiscontinuityRevision`과 `LastDiscontinuity`가 갱신됩니다. 외부 패킷의 Actor와 Session 매핑은 호출 전에 Host가 검증합니다.
- `TryCaptureHistorical`과 `RestoreHistorical`은 Backend가 `IMotorHistoricalBackend`를 구현했을 때만 과거 Owned State에 적용됩니다. 복원 결과는 `MotorRestoreStatus`(`Restored`, `Unsupported`, `ActorMismatch`, `StaleHistory`, `BackendRejected`)로 구분합니다. 콜백 안의 교정·복원은 재진입으로 거절되고, 실패한 복원은 불연속을 기록하지 않습니다.
- `MotorSnapshot<TState>`는 Actor, 외부 Step, History 세대, Owned State를 담은 값이며 네트워크 전송 포맷이나 World 기록이 아닙니다.
- `DiscontinuityRevision`과 `LastDiscontinuity`는 읽을 수 있는 상태이지 이벤트가 아닙니다. 표현이나 Navigation이 값을 보고 자기 캐시를 무효화합니다. Motor의 교정이 Navigation을 자동으로 무효화하지는 않습니다.

## 소유권과 수명

- Host가 Actor, Model, Motor, Backend를 만들고 파괴합니다. Rigidbody를 파괴하기 전에 Motor와 Model 사용을 멈춰야 합니다. 파괴 뒤 호출은 Unity의 `MissingReferenceException`입니다.
- `MotorCollisionProbe3D`는 Rigidbody와 같은 GameObject에 있어야 하며, 다른 오브젝트면 Backend를 만들 때 `ArgumentException`입니다.
- 리스폰은 새 Generation의 `MotionActorKey`로 새 Motor를 만듭니다.

## 책임 범위

### 담당하는 것

- 합성 결과의 Tick당 한 번 적용, 현재 상태 읽기·교정
- Physics Step 관측 수명, 접촉 읽기와 Blocking·Slide 관측
- 선택적 과거 Owned State 복원, 불연속 기록
- 기존 GroundChecker 결과의 관측 연결

### 담당하지 않는 것

- Physics World Step 실행, World·Contact History
- 네트워크 Authority 인증(`TryApply`는 Actor 일치만 확인합니다)
- 이동 플랫폼 속도 계산, Grounded 상태 머신, 점프, 충돌 Solver
- 내장 Rigidbody2D/3D의 과거 Physics World 복원과 결정론적 전체 예측

## 제약과 주의사항

- 한 Tick에서 `TryApply`는 Physics Step 사이에 한 번만 받습니다.
- 내장 Rigidbody2D/3D Backend는 `IMotorHistoricalBackend`를 구현하지 않습니다. 서버 교정은 `ApplyCurrentState`로 합니다.
- 3D에서 접촉이 필요하면 Probe를 붙이세요. 잠든 Rigidbody는 충돌 콜백이 오지 않을 수 있어 정지 접촉이 빠질 수 있습니다.
- `SupportsHistoricalRestore`는 인터페이스 존재만 알려 줍니다. 실제 복원은 위의 `MotorRestoreStatus` 중 하나가 됩니다.

## 관련 가이드

- [Tick 실행 가이드](../../guides/locomotion/run-a-tick.md)

## 관련 문서

- [Locomotion 전체 구조](overview.md) — 재진입, Restore, Host Step 순서
- [Motion](motion.md) — 합성 결과
- [Movement](movement.md) — Step Sample
- [Game Controller](../character/controller.md) — GroundChecker
- [Physics Query](../world/physics-query.md)
