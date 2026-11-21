# Locomotion Tick 실행

Actor 한 명의 이동 Tick을 Host에서 실행하는 순서를 설명합니다. Motion으로 합성하고, Motor로 적용하고, Physics Step 뒤 관측을 다음 Tick에 되돌립니다.

## 목적

Locomotion에는 이 순서를 대신 실행하는 묶음 객체가 없습니다. 호출 순서와 번호 계약을 Host가 지켜야 하므로, 한 Tick의 최소 흐름을 한곳에 정리합니다.

## 전제 조건

- Rigidbody가 있는 Actor와 Actor를 식별할 `MotionActorKey`
- Physics World Step을 실행하는 소유자 하나(Unity 자동 실행 또는 Host의 수동 실행)
- 3D에서 접촉이 필요하면 Rigidbody와 같은 GameObject의 `MotorCollisionProbe3D`
- 지면 속도가 필요하면 기존 `IGroundChecker`([Game Controller](../../modules/character/controller.md))

## 최종 결과

Tick마다 합성 결과가 Body에 한 번 적용되고, Physics Step의 접촉과 지면이 관측되며, 그 결과가 다음 Tick의 `MotionFeedback`이 됩니다. 필요하면 표현과 기록이 읽을 `MovementFrameSample`도 만들어집니다.

## 절차

### 1. Actor 구성

Actor를 만들 때 Model, Motor, Backend, 입력 버퍼를 함께 만듭니다.

```csharp
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Game.Motor;
using inonego.Xeri.Game.Motor.Adapter;
using inonego.Xeri.Game.Movement;
using inonego.Xeri.Serializable;

var actorKey = new MotionActorKey(scopeID: 1, actorID: 10, generation: 1);

var model = new MotionModel(actorKey, MotionModel.WorldUp);
var motor = new Motor<MotorState3D>(actorKey, new RigidbodyMotorBackend3D(rigidbody, probe));
var contacts = new MotorContactReader<MotorState3D, ContactPoint>(motor);
var support = new MotorGroundSupport<MotorState3D>(motor, groundChecker);   // 선택
var buffer = new MotionInputBuffer();
var contactBuffer = new ContactPoint[16];
var feedback = MotionFeedback.None;
long tick = 0;
```

### 2. 입력 모으기

이번 Tick의 운동 기여를 입력 버퍼에 넣습니다. 입력을 만드는 코드는 Body를 직접 바꾸지 않습니다.

```csharp
buffer.Add(new MotionIntent(MotionChannel.Planar, moveVelocity));
buffer.Add(MotionScaleCommand.Add(stunKey, MotionChannel.All, MotionLayer.Intent, order: 0, NumericFOperation.SET, 0f, duration: 1f));
```

### 3. 합성

```csharp
var frame = model.StepLocal(deltaTime, buffer.AsStepInput(), feedback);
buffer.Clear();
```

`StepLocal`은 다음 Tick 번호를 씁니다. 외부 Tick 번호를 쓰려면 `Step(new MotionTick(index, delta), ...)`를 호출하세요. 일시정지 중에는 호출하지 않습니다.

### 4. 적용과 Physics Step

```csharp
if (!motor.TryApply(frame))
{
    // 다른 Actor의 결과이거나 Backend가 거절한 프레임입니다.
}

motor.BeginPhysicsStep();
// Physics World 소유자가 정확히 한 번 Step을 실행합니다.
motor.CompletePhysicsStep(frame.Tick.Index);
```

`CompletePhysicsStep`에는 `frame.Tick.Index`를 넘기세요. [Movement Sample](../../modules/locomotion/movement.md)이 이 번호가 같은지 검사합니다.

### 5. 관측

```csharp
var isGrounded = false;

if (support.CheckAndObserve(frame.Tick.Index, deltaTime, rigidbody.position) && support.TryRead(out var ground))
{
    isGrounded = ground.IsOnGround;
}

if (contacts.Read(contactBuffer, out var count) == MotorContactReadStatus.Ready)
{
    // contactBuffer[0..count)에서 차단 법선을 골라 feedback에 담습니다.
}
```

### 6. 다음 Tick의 Feedback

관측한 회전, 접지, 차단 법선으로 다음 Tick의 `MotionFeedback`을 만듭니다.

```csharp
var state = motor.ReadCurrentState();
feedback = new MotionFeedback(state.Rotation, isGrounded);

for (var index = 0; index < count; index++)
{
    feedback = feedback.WithBlockingNormal(contactBuffer[index].normal);
}
```

### 7. Sample 만들기 (선택)

```csharp
var sample = MovementFrameSample<MotorState3D>.Capture
(
    model, motor, frame, isGrounded, MovementSampleConfirmation.Unconfirmed
);
```

## 소유권과 정리

- Host가 Actor를 만들 때 위 객체를 함께 만들고 Actor를 파괴하기 전에 사용을 멈춥니다.
- Rigidbody 파괴 뒤에는 Model과 Motor를 호출하지 않습니다. 리스폰은 새 Generation의 `MotionActorKey`로 객체를 새로 만듭니다.
- 오래 유지하는 Override를 `AcquireOverride`로 얻었다면 Lease를 해제하는 쪽이 종료 시점을 정합니다.

## 문제 해결

| 증상 | 확인할 것 |
|---|---|
| `TryApply`가 `InvalidOperationException` | Physics Step 중이거나 같은 Step 사이에 이미 적용했습니다. Tick당 한 번, `BeginPhysicsStep` 전에 호출하세요 |
| `TryApply`가 `false` | 다른 Actor의 프레임이거나 `MotionFrame.IsValid`를 통과하지 못했습니다 |
| 접촉 읽기가 `Unsupported` | 3D에서 Probe가 없거나 비활성입니다 |
| 접촉 읽기가 `NotObserved` | 교정 직후이거나 새 Step 없이 Probe를 다시 켰습니다. 다음 Step 뒤에 읽으세요 |
| `Capture`가 번호 불일치 예외 | `CompletePhysicsStep`에 `frame.Tick.Index`와 다른 번호를 넘겼습니다 |
| 수평 이동이 중력을 지움 | 옆에서 보는 2D는 `MotionModel.WorldUp`, 위에서 보는 2D는 `TopDown2DUp`을 쓰고 수직 채널은 입력하지 않아야 Free로 유지됩니다 |
| 넉백이 벽에서 되살아남 | 직전 Physics의 접촉 법선을 `MotionFeedback.WithBlockingNormal`로 넘겼는지 확인하세요 |

## 관련 문서

- [Locomotion 전체 구조](../../modules/locomotion/overview.md)
- [Motion](../../modules/locomotion/motion.md) · [Motor](../../modules/locomotion/motor.md) · [Movement](../../modules/locomotion/movement.md)
