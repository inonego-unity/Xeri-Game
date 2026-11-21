# Movement

Xeri Movement는 애니메이션, 원격 표시, Replay가 읽는 Actor의 이동 상태를 값으로 만드는 경계입니다. Animator나 네트워크를 직접 다루지 않습니다.

## 왜 필요한가

이동에 따라 애니메이션 파라미터(속력, 접지, 동작)를 정하려면 Model, Motor, 접지 관측을 한곳에서 읽어야 합니다. 원격 Actor는 Model을 실행하지 않으므로 복제된 Body 값만으로 서버와 같은 상태를 다시 계산할 수 있어야 합니다. Movement는 이 계산을 순수 함수로 제공해, 애니메이션 파라미터를 따로 복제하지 않아도 됩니다.

## 언제 사용하는가

- 서버나 로컬 Actor의 이동 결과를 Animator, 이펙트, 기록에 넘길 때
- 원격 클라이언트가 복제된 속도와 회전, 동작 ID로 이동 상태를 재구성할 때
- 한 Tick의 합성 결과와 Body 상태를 묶어 외부 Recorder가 읽게 할 때

## 핵심 구조

- `MovementState`: 한 Tick의 이동 상태 값입니다. 월드 선속도와 Body 회전 기준 로컬 선속도, Planar 속력, 수직 속도, 접지 여부, 보이는 맨 위 SET 항목의 동작 ID·시작 Tick·경과 Tick, 마지막 Body 불연속 종류를 담습니다. 스턴 같은 상태는 사용처가 소유하므로 담지 않습니다.
- `MovementFrameSample<TState>`: Model의 마지막 `MotionFrame`, Motor의 현재 Body 상태, 불연속 번호, `MovementState`를 묶은 값 형식입니다. `TState`는 `IMotorKinematicState`(`MotorState2D`, `MotorState3D`)여야 합니다.
- `MovementSampleConfirmation`: `Unconfirmed`와 `Confirmed`. **외부 Host가 명시적으로 지정**합니다.

## 기본 사용

```csharp
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Game.Motor;
using inonego.Xeri.Game.Movement;

var frame = model.StepLocal(deltaTime, input);
motor.TryApply(frame);
motor.BeginPhysicsStep();
// Physics World 소유자가 Step 실행
motor.CompletePhysicsStep(frame.Tick.Index);

var sample = MovementFrameSample<MotorState2D>.Capture
(
    model, motor, frame, isGrounded, MovementSampleConfirmation.Unconfirmed
);

animatorBinder.Apply(sample.State);   // 사용처 코드
var confirmed = sample.WithConfirmation(MovementSampleConfirmation.Confirmed);
```

### 원격 Actor의 재구성

`MovementState.Create`는 Model 없이 호출하는 순수 계산입니다. 원격 Actor는 복제된 Body 선속도와 회전, `(동작 ID, 시작 Tick)`으로 서버와 같은 값을 만듭니다.

```csharp
var state = MovementState.Create
(
    actorKey, tickIndex, up, linearVelocity, rotation, isGrounded,
    actionID, actionStartTick, lastDiscontinuity
);
```

Up을 Tick마다 바꾸는 게임은 Up도 복제 값에 넣어 그 Tick의 Up으로 호출합니다. 복제된 값이 들어오는 진입점이라 입력을 검사합니다. 음수 Tick, 현재 Tick보다 늦은 동작 시작 Tick, 미정의 불연속 종류, 단위가 아닌 Up, 유한하지 않거나 영인 회전, 유한하지 않거나 크기가 오버플로하는 속도는 `ArgumentOutOfRangeException`입니다. 단위가 아닌 회전은 정규화해 씁니다.

## 호출 조건

`Capture`가 성공하려면 다음이 모두 맞아야 합니다.

- Model, Motor, 합성 결과의 Actor가 같습니다.
- 합성 결과가 Model의 **마지막 Step**(Tick Index와 `StepVersion`)입니다.
- Motor의 Physics Step이 완료돼 있고, **`CompletePhysicsStep`에 넘긴 번호가 `frame.Tick.Index`와 같습니다.**

어긋나면 `InvalidOperationException`이며 번호 불일치는 별도 메시지로 알려 줍니다. Model Restore 이전의 결과, 다른 Actor의 결과, Physics Step 완료 전, 교정 직후의 캡처가 모두 이에 해당합니다.

캡처는 Motor 작업 구간에서 Body를 읽으므로 조회 콜백 안의 Motor 교정은 재진입으로 거절되고, 조회 중 Model Restore는 조회 뒤 한 번 하는 수명 검증에서 거절됩니다. Sample은 값 형식이라 보관과 확정 사본에 할당이 없고 이후의 교정과 무관합니다.

## 애니메이션 연결

- **이동 → 애니메이션:** 사용처가 `MovementState`(로컬 속도, 속력, 접지, 동작 ID, 시작 Tick, 불연속 종류)를 Animator 파라미터로 옮깁니다. Animator 이름과 블렌드 트리 연결은 사용처의 일입니다.
- **서버 동기화:** 복제하는 값은 `(동작 ID, 시작 Tick, 재생 속도)`이고, 클라이언트는 `현재 Tick − 시작 Tick`으로 재생 위치를 맞춥니다.
- **애니메이션 → 이동:** 이동에 영향을 주는 루트 모션은 사용처가 [Motion Track](motion.md#루트-모션-track)으로 평가해 Modifier 값으로 넣습니다. 서버는 Animator를 돌리지 않아도 같은 결과를 냅니다.

## 책임 범위

### 담당하는 것

- `MovementState`의 순수 계산과 입력 검증
- 한 Tick의 합성 결과, Body 상태, 이동 상태를 묶는 값 형식 Sample

### 담당하지 않는 것

- Animator·Presentation·Entity 연결 (이 모듈은 이들을 참조하지 않습니다)
- Recorder와 History Window, Replay 구동
- 이벤트 확정, 중복 제거, 네트워크 Authority 판단
- 결정론적 Physics Rollback과 Gameplay Event 장부

## 제약과 주의사항

- `Confirmed`는 외부 Host가 부여한 분류입니다. Movement가 서버 권한이나 이벤트 확정을 인증한다는 뜻이 아닙니다.
- Sample은 현재 상태와 합성 결과의 공개 Export입니다. Physics Rollback이 아닙니다.
- Navigation의 Owned Snapshot은 필요하면 `NavigationController.Capture()`로 따로 보관하세요. Sample에 포함되지 않습니다.
- 저장 기간은 외부 Recorder나 History Window가 관리합니다.

## 관련 문서

- [Locomotion 전체 구조](overview.md) — Host Step 순서, 재진입 정책
- [Motion](motion.md) · [Motor](motor.md) · [Navigation](navigation.md)
- [Tick 실행 가이드](../../guides/locomotion/run-a-tick.md)
