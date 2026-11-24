# Locomotion 전체 구조

Locomotion은 Actor의 이동을 **합성 → 적용 → 관측 → 표현**의 흐름으로 나누는 모듈 묶음입니다. 입력, AI, Ability, 넉백, 바람처럼 여러 시스템이 한 Actor를 동시에 움직이더라도 서로 덮어쓰지 않도록 운동 기여를 한곳에서 합칩니다.

## 왜 필요한가

걷기, 대시, 루트 모션, 스턴, 슬로우, 넉백이 각자 Rigidbody 속도를 직접 바꾸면 나중에 쓴 쪽이 앞선 효과를 지웁니다. 예를 들어 걷기 입력이 같은 Tick의 넉백을 덮어쓰거나, 수평 이동이 중력으로 쌓인 수직 속도를 0으로 만듭니다.

Locomotion은 모든 기여를 기본 의도와 Order 순 Modifier로 모아 Tick마다 한 번만 합성해 Body에 씁니다. Body를 바꾸는 곳은 하나이고, 나머지는 값만 만들어 넘깁니다.

## 언제 사용하는가

- 이동에 영향을 주는 시스템이 둘 이상이어서 서로의 효과가 지워지지 않아야 할 때
- 스턴·슬로우·넉백을 Body가 아니라 이동 계산에서 일관되게 처리하고 싶을 때
- 서버와 클라이언트가 같은 입력으로 같은 합성 결과를 내야 할 때
- 애니메이션과 원격 표시가 읽을 이동 상태를 같은 방식으로 만들고 싶을 때

이동이 입력 하나뿐이고 효과가 겹치지 않는 단순한 프로젝트라면 이 모듈 없이 Rigidbody를 직접 다루는 편이 더 단순합니다.

## 핵심 구조

```text
사용처 Producer (입력·AI·Ability·Combat·영역 판정·점프 판정·Network)   ← 확장 지점
        │  MotionStepInput — 기본 의도와 Modifier 명령
        ▼
Motion   MotionModel: 채널별 MValue(Base = 기본 의도, Modifier = SET·ADD·SUB·MUL 항목) → MotionFrame
        ▼
Motor    TryApply(frame) Tick당 1회 → IMotorBackend (Rigidbody2D/3D)    ← 확장 지점
        ▼
[Physics World 소유자가 Step을 1회 실행]
        ▼
관측     MotorContactReader · MotorGroundSupport → 다음 Tick의 MotionFeedback(사용처가 값 계산에 씀)
        ▼
Movement MovementState · MovementFrameSample → Animator·복제·Recorder     ← 확장 지점

Navigation  목표/경로/Guidance → 사용처 Producer가 기본 의도로 변환
```

| 모듈 | 소유하는 것 | 소유하지 않는 것 |
|---|---|---|
| [Motion](motion.md) | 채널별 MValue 접기와 Free 채널 규칙, 입력 검증과 세대 규칙, 항목 상태 조회, Owned Snapshot. Model과 분리된 Track 평가·차단 법선 제거 도구 | 효과의 의미와 Order 배치, 효과 수명·감쇠·Track 재생 시간, 입력 생성 정책, 스탯 계산, Body 적용, 네트워크 중복 제거 |
| [Motor](motor.md) | 합성 결과의 Tick당 1회 적용, 현재 상태 읽기·적용, 선택적 과거 복원, Contact 관측 수명, Correction과 불연속 | Physics World Step 실행, Authority 검증, 게임별 이동 정책 |
| Motor Adapter | 기존 GroundChecker 결과를 Physics 관측 수명에 연결 | 발판 속도 계산·적용, Grounded 상태 머신 |
| [Navigation](navigation.md) | 목표·경로·Guidance, 요청 ID·World Revision·Path ID, Owned Snapshot | Planner·NavMesh, Steering, Body 제어 |
| [Movement](movement.md) | `MovementState` 순수 계산, Step Sample | Animator 연결, Recorder, Event 확정 |
| 사용처(Host·Adapter) | Producer, Physics Step, Feedback 구성, Authority·Network·History·보간, Animator·Presentation 연결 | — |

## 연결 원칙

Locomotion은 **Xeri Core의 범용 기능만 직접 쓰고, 도메인 시스템과는 확장 지점으로만 만납니다.**

| 구분 | 대상 | 방식 |
|---|---|---|
| 직접 사용 | Core의 `MValue`·`Vector3FModifier`·`NumericFModifier`(채널 합성과 `CloneFrom` 복제, `BeginBatch` 묶음 갱신), `NumericFOperation`, `NumericHelper.IsFinite`, `VectorHelper.TryNormalize`, `Lease` | Locomotion 코드가 참조 |
| 확장 지점으로만 | Entity·Actor·Presentation, Action(Ability·Execution·Delivery·Operation), Character Controller(Jumper·GroundSuspension), Fabric Data, Animator, Network | 값과 인터페이스만 정의하고 그 타입을 참조하지 않습니다. 연결 구현은 사용처나 별도 연동 어셈블리가 맡습니다 |

유일한 예외는 Character Controller의 `IGroundChecker`를 쓰는 `MotorGroundSupport`입니다. 이 어댑터는 별도 폴더와 네임스페이스(`inonego.Xeri.Game.Motor.Adapter`)에 있고, Motion과 Motor 본체는 어댑터를 참조하지 않습니다.

Motion의 채널 합성은 Core `MValue`를 그대로 씁니다. 접기 규칙·Order·가려짐(`IModifier.Replaces`)은 Core가 정하고, Motion은 항목의 시간·물리 피드백 계산과 Free 채널 규칙만 더합니다. Snapshot은 `MValue.CloneFrom`으로 하며 Modifier 구성이 같으면 할당이 없습니다. 장비·레벨 같은 영구 스탯은 사용처의 `MValue`로 계산해 기본 의도에 반영하고, 스턴·둔화·가속처럼 겹치고 되돌려야 하는 일시 효과는 Motion `MUL` 항목으로 넣습니다.

## 공통 정책

여러 모듈에 같은 방식으로 적용되는 규칙입니다. 각 모듈 문서는 이 규칙을 반복하지 않고 이곳을 가리킵니다.

### Actor와 입력 항목 식별

- `MotionActorKey(ScopeID, ActorID, Generation)`는 로컬 실행 인스턴스와 Actor 수명을 구분합니다. Network Object ID가 아닙니다. Foundation의 `EntityHandle`과 일부러 분리했는데, `EntityHandle`은 살아 있는 Entity 참조라 Snapshot, 다른 실행 Scope(서버·클라이언트), 직렬화 값으로 쓸 수 없기 때문입니다. Host가 Actor를 만들 때 Key를 정합니다(예: `ScopeID` ← 실행 인스턴스, `ActorID` ← Entity Key, `Generation` ← 등록 Revision).
- `MotionSourceKey(Key, Generation)`는 Actor 안의 Modifier 항목 키입니다. 키는 사용처가 정한 문자열(예: `"Dash"`)이며 그대로 채널 `MValue`의 Modifier 키가 됩니다. 같은 키에 더 큰 Generation이 오면 이전 항목을 대신하고, 작은 Generation은 `StaleGeneration`, 같으면 값과 수명을 갱신합니다(`Updated`). 끝난 항목의 세대는 기억하지 않으므로 늦게 도착한 네트워크 입력의 중복 제거는 Adapter가 맡습니다.

### Tick

- `MotionModel.Step(tick, …)`은 마지막 Tick보다 큰 번호만 받고, `StepLocal(delta, …)`은 다음 번호를 씁니다.
- `delta <= 0`은 상태를 바꾸기 전에 거절합니다. **일시정지(`Time.timeScale = 0`) 중에는 Host가 Step을 호출하지 않습니다.**
- 전역 고정 Tick이나 Command Bus를 강제하지 않습니다. Tick 번호는 Host가 정한 실행 Index이며 네트워크 Tick과 같을 필요가 없습니다.

### 재진입

Motor의 관측·History·Correction 수명을 바꾸거나 그 수명에 의존하는 작업은 Motor 하나당 한 번에 하나만 실행됩니다.

- 대상: `ApplyCurrentState`, `BeginPhysicsStep`, `CompletePhysicsStep`, `MotorContactReader.Read`, `TryCaptureHistorical`, `RestoreHistorical`, `MotorGroundSupport.CheckAndObserve`·`TryRead`, `MovementFrameSample.Capture`
- 이 작업의 Backend·Checker 콜백 안에서 이런 작업을 다시 시작하면 `InvalidOperationException`으로 거절되고, 거절된 작업은 Motor 상태를 바꾸지 않습니다.
- `ReadCurrentState`와 `TryApply`는 수명에 참여하지 않습니다.
- Motion `Step`과 Navigation `TryAcceptPath`는 `ReadOnlySpan` 입력을 받아 처리 도중 외부 코드를 호출하지 않습니다.

### Restore와 수명 값

- **시뮬레이션 상태**는 Snapshot 시점으로 되돌립니다. 예: Motion의 `LastTick`·Modifier 항목·채널 `MValue`·Free 채널에 이미 반영한 지속 ADD·SUB, Navigation의 Goal·Path·Waypoint·World Revision, Motor Body.
- **발급 토큰**은 되돌리지 않고 새로 발급합니다. 예: Motion `StepVersion`, Navigation `GoalRevision`·Request ID·Path ID, Motor `PhysicsObservationVersion`·`DiscontinuityVersion`. 복원 전에 만든 결과나 Guidance가 복원 후의 상태를 바꾸지 못하게 하기 위해서입니다.
- Motor `HistoryGeneration`은 `ApplyCurrentState`에서만 올라갑니다. `RestoreHistorical`은 같은 History 안의 이동입니다.
- 각 Restore는 자기 Owned State만 다룹니다. 여러 모듈을 함께 되돌리는 Aggregate Restore는 Host가 맡습니다.

### Host Step 순서

한 Actor의 Tick 하나는 Host가 정해진 순서로 호출합니다. 이 순서를 대신 실행하는 묶음 객체는 없습니다. 단계별 코드는 [Tick 실행 가이드](../../guides/locomotion/run-a-tick.md)에 있습니다.

```text
1. feedback = 직전 관측(회전·접지·차단 법선)으로 MotionFeedback 구성, 사용처가 값 계산에 사용
2. frame = MotionModel.StepLocal(delta, input)  또는 Step
3. Motor.TryApply(frame)                                  // 다음 BeginPhysicsStep 전 1회
4. Motor.BeginPhysicsStep()
5. [Physics World 소유자가 정확히 한 번 Step 실행]
6. Motor.CompletePhysicsStep(frame.Tick.Index)
7. MotorContactReader.Read / MotorGroundSupport.CheckAndObserve(...)
8. 필요하면 MovementFrameSample.Capture(model, motor, frame, isGrounded, ...)
```

### Body 수명

Rigidbody를 파괴하기 전에 Host가 해당 Model과 Motor 사용을 멈춥니다. 파괴 뒤에 호출하면 Unity의 `MissingReferenceException`이 그대로 발생합니다. 리스폰은 새 Generation의 `MotionActorKey`로 새 Model과 Motor를 만듭니다.

## 확장 지점

새 타입을 만들기 전에 아래 계약 중 하나로 연결할 수 있는지 먼저 확인하세요.

| 확장 지점 | 방향 | 형태 | 연결 예 |
|---|---|---|---|
| `MotionStepInput` | 사용처 → Model | `MotionIntent`와 `MotionModifierCommand`(`Apply`·`Impulse`·`End`·`Clear`). 값·수명·의미·Order는 사용처가 정합니다 | 입력·AI, Ability, 넉백 판정, 영역 밀기·감쇠·루트 모션(같은 세대 매 Tick 갱신), 점프 판정(Impulse), 발판, Network 입력 복제 |
| `MotionInputBuffer`와 Core `Lease` | Model → 사용처 | 해제하면 종료 입력을 넣는 Lease | 사다리·탑승처럼 오래 유지하는 Modifier |
| `MotionFeedback` | Motor 관측 → 사용처 | 회전·접지·차단 법선(최대 4개). Track 평가와 `RemoveBlocked`에 씀 | 내장 Contact 관측, 사용처의 지면 판정 |
| `MotionTrack` | 사용처 도구 | 불변 로컬 변위·Yaw 데이터와 구간 속도 평가 | 데이터 테이블, AnimationClip을 굽는 도구 |
| `MotionOrientation` | 사용처 도구 | Body 자세를 새 Up에 맞추는 각속도 | 중력 방향이 바뀌는 게임(행성, 벽 걷기) |
| `IMotorBackend<TState>` | 사용처 → Motor | 기존 인터페이스 | Kinematic Body, 서버 전용 Body |
| `MovementState` | Movement → 사용처 | 읽기 전용 값, 순수 계산 | Animator 연결, 원격 표시, Replay |
| Snapshot 값 | 양방향 | `MotionModelSnapshot`, Motor·Navigation Snapshot | 서버 교정, 기록 |

## 실행 역할별 사용

| 역할 | 하는 일 | Locomotion이 제공하는 것 |
|---|---|---|
| Offline | 싱글플레이·에디터 | 전체 흐름 |
| Server (Authority) | 권한 시뮬레이션, 렌더링 없음 | 전체 흐름, Animator 없이 Track 평가, 복제용 값 |
| Owner Client (Predicted) | 먼저 실행하고 서버 상태로 교정 | 재실행 가능한 Model, Snapshot과 Restore |
| Remote Client (Replica) | 다른 Actor 표시 | Model 없이 복제 값으로 `MovementState.Create` |
| Replay | 기록 재생 | 입력과 상태의 값 형식 |

**예측 가능 여부는 Backend가 정합니다.** 내장 Rigidbody2D/3D Backend는 Actor 하나만 물리를 다시 실행할 수 없어서 교정(`ApplyCurrentState`)과 표현 스무딩으로 대신합니다. Model 상태(Modifier 항목과 채널 `MValue`)는 어느 Backend에서든 서버와 정확히 맞출 수 있습니다. 정적 월드 기준의 Body 예측이 필요하면 `IMotorBackend`로 Kinematic Backend를 붙여야 하며, 내장 구현은 없습니다.

```text
교정(Adapter 실행): 서버 상태(Tick T) 도착 → 로컬 기록과 비교
  → 다르면 MotionModel.Restore(T) + Motor.ApplyCurrentState(서버 Body)
  → T+1부터 현재까지 기록된 입력으로 Model을 다시 실행
```

## 책임 범위

### 담당하는 것

- 운동 기여의 채널별 합성과 Tick당 한 번의 Body 적용
- Body 현재 상태의 읽기·교정, Physics Step 관측 수명, 접촉·지면 관측 연결
- 목표와 경로의 수명 토큰, Waypoint Guidance
- 표현·복제가 읽는 이동 상태 값

### 담당하지 않는 것

- 입력 해석, AI 판단, 경로 계산(Planner·NavMesh), Steering
- 실제 Physics World Step 실행과 World History
- 네트워크 Authority 검증, Object ID 매핑, 직렬화, 보간
- Animator·Presentation 연결, Recorder, 이벤트 확정과 중복 제거
- 게임별 걷기·점프·탑승 정책과 상태 머신, 충돌 Solver, 범용 롤백 엔진

## 제약과 주의사항

- 실제 Physics World Step은 Unity나 Host 중 **한 소유자**만 실행합니다. Motor의 `BeginPhysicsStep`·`CompletePhysicsStep`은 관측 창만 관리하고 시뮬레이션을 실행하지 않습니다.
- 모든 운동 기여는 기본 의도나 Modifier 명령으로 Model에 들어가야 합니다. 다른 시스템이 Body 속도를 직접 바꾸면 합성 결과와 어긋납니다.
- 내장 Rigidbody2D/3D Backend는 과거 Physics World의 복원과 결정론적 전체 예측을 지원하지 않습니다.
- `MovementFrameSample`의 `Confirmed`는 Host가 부여한 분류이며 네트워크 Authority를 인증한다는 뜻이 아닙니다.
- Navigation의 `Capture`·`Restore`는 Goal과 Path만 다루고 Planner 작업, World Snapshot, Ability 상태는 복원하지 않습니다.

## 관련 가이드

- [Tick 실행 가이드](../../guides/locomotion/run-a-tick.md)

## 관련 문서

- [Motion](motion.md) · [Motor](motor.md) · [Navigation](navigation.md) · [Movement](movement.md)
- [Game Controller](../character/controller.md) — GroundChecker·Jumper
- [Physics Query](../world/physics-query.md)
- [Xeri Game 구조와 소유권](../../concepts/architecture.md)
