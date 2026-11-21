# Xeri Game Movement — 이동 상태와 Step Sample

> Namespace `inonego.Xeri.Game.Movement`, Assembly `inonego.Xeri.Game`. **실제 Network/Replay Adapter·Recorder·Animator 연결을 구현하지 않는다.** 표현·복제·기록이 읽는 값을 만드는 경계다.

## 공개 계약

- `MovementState`: 한 Tick의 이동 상태 값. 월드·로컬(Body 회전 기준) 선속도, Planar 속력, 수직 속도, 접지 여부, 가장 위 Override의 동작 ID·시작 Tick·경과 Tick, 기본 의도가 막힌 채널, 마지막 Body 불연속 종류.
  - `MovementState.Create(...)`는 **Model 없이 호출하는 순수 계산**이다. 원격 Actor는 복제된 Body 선속도·회전과 `(동작 ID, 시작 Tick)`으로 서버와 같은 값을 재구성한다. 복제 값이 범위 밖이거나(음수 Tick, 현재 Tick보다 늦은 시작 Tick, 미정의 채널·불연속 종류), 유한하지 않거나, 속도·회전 크기가 오버플로하면 `ArgumentOutOfRangeException`이다. 애니메이션 파라미터를 따로 복제하지 않는다.
  - Animator 파라미터 이름·블렌드 트리 연결은 사용처가 이 값을 읽어서 한다(예: Presentation Provider). 이 모듈은 Animator·Presentation·Entity를 참조하지 않는다.
  - 서버는 Animator를 돌리지 않아도 된다. 이동에 영향을 주는 루트 모션은 Motion Track으로 평가되고, 클라이언트는 `현재 Tick − 시작 Tick`으로 재생 위치를 맞춘다.
- `MovementFrameSample<TState>`(값 형식): Model의 마지막 `MotionFrame`, Motor 현재 Body State, Discontinuity 버전, `MovementState`를 묶는다. `TState`는 `IMotorKinematicState`(`MotorState2D/3D`)여야 한다.
- `MovementSampleConfirmation.Unconfirmed/Confirmed`: **외부 Host가 명시적으로 지정**한다. Core는 서버 권한·Event Confirm/Reject를 인증하지 않는다.

## 사용 예

```csharp
var frame = model.Step(tick, inputs, feedback);
motor.TryApply(frame);
motor.BeginPhysicsStep();
// Unity/Host Physics Step
motor.CompletePhysicsStep(frame.Tick.Index);

var sample = MovementFrameSample<MotorState2D>.Capture
(
    model, motor, frame, isGrounded, MovementSampleConfirmation.Unconfirmed
);

animatorBinder.Apply(sample.State);       // 사용처 코드
var confirmed = sample.WithConfirmation(MovementSampleConfirmation.Confirmed);
```

**필수 불변 조건:** Model·Motor·결과의 Actor가 같아야 하고, 결과가 Model의 **마지막 Step**(Tick Index·StepVersion)이어야 하며, Motor Physics Step이 완료돼야 한다. **`Motor.CompletePhysicsStep`에 넘긴 번호는 Motion Tick Index와 같아야 한다**(`frame.Tick.Index`). 번호가 다르면 그 사실을 알리는 별도 메시지의 예외로 거절한다. 전체 호출 순서는 [시스템 설계 §3의 Host Step 순서](../../../../../Docs/systems/movement/game-module-design.md)에 있다. Restore 이전 결과·다른 Actor 결과·완료 전·Correction 직후 캡처는 예외다. **캡처는 Motor 작업 구간에서 Body를 읽으므로 조회 콜백 안의 Motor Correction은 재진입으로 거절되고, 조회 중 Model Restore는 조회 뒤 한 번 하는 수명 검증에서 거절된다.** Sample은 값 형식이라 보관·확정 사본에 할당이 없고 이후 Correction과 무관하다. 외부 Recorder/History Window가 저장 기간을 관리한다.

이 Sample은 **Current-State·합성 결과의 공개 Export**이지 Deterministic Physics Rollback이나 Gameplay Event 확정 Ledger가 아니다. `NavigationController.Capture()`의 Owned Navigation Snapshot은 필요할 때 별도로 보관한다.

## 검증

[Movement Step Sample EditMode 테스트](../../../../Tests/EDIT/Modules/Locomotion/Movement/TEST_MovementFrameSample.cs)는 출처 검증, Restore 전 결과 거절, Tick과 Physics Step 번호 불일치 거절, 조회 콜백 재진입 거절, Model Restore·Motor Correction·Navigation 무효화 복합 전환, 동작 ID·시작 Tick 전달, 원격 재구성 일치와 로컬 속도 계산을 다룬다. **Motor Correction이 Navigation을 자동 무효화하지는 않는다.** 테스트 수와 검증 수준은 [구현 플랜](../../../../../Docs/systems/movement/implementation-plan.md)에 있다. 실제 Provider/Recorder/Replay·Player 성능은 미검증이다.
