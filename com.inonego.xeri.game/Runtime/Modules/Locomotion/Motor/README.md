# Xeri Game Motor — Runtime 사용 기준

> Tick 합성 결과(`MotionFrame`) 적용 + Body State + Contact/Feedback + 선택적 GroundChecker Support 연결 어댑터. Namespace `inonego.Xeri.Game.Motor`, Assembly `inonego.Xeri.Game`. Full Prediction/Replay/Adapter 통합은 포함하지 않는다.

**기준 문서:** [Motor 목표 계약](../../../../../Docs/systems/movement/motor-contract.md) · [공통 정책(설계 §3)](../../../../../Docs/systems/movement/game-module-design.md) · [구현 현황](../../../../../Docs/systems/movement/implementation-plan.md). **코드와 테스트가 현재 구현의 증거**이며 목표 문장은 구현 사실이 아니다.

## 구현된 책임

| API·타입 | 현재 동작 |
| --- | --- |
| `Motor<TState>` | Actor 단위 Body 경계. `TryApply(in MotionFrame)`을 Physics Step 사이 한 번 받음, Current State 읽기·적용, 선택적 History, 관측 수명 |
| `IMotorBackend<TState>` | 실제 Body Read/Apply와 합성 결과 적용은 Backend 책임 |
| `IMotorKinematicState` | `MotorState2D/3D`의 월드 선속도·회전 읽기. `MovementState` 계산에 사용 |
| `RigidbodyMotorBackend2D` | 합성 결과의 X·Y 선속도와 Z 각속도(rad/s → 도/초). Up 축으로 위에서 보기·옆에서 보기 구분, `ContactPoint2D` |
| `RigidbodyMotorBackend3D` | 합성 결과의 Up 축 기준 Planar/Elevation/Rotation, **선택적** `MotorCollisionProbe3D`의 `ContactPoint` |
| `IMotorHistoricalBackend<TState>` | 과거 Owned State용 **선택적 인터페이스**. 내장 Rigidbody2D/3D는 미구현 |
| `MotorContactReader<TState,TContact>` | 한 Motor의 실제 Contact를 정해진 형식으로 읽는 관측 객체. 형식은 생성 시 Backend와 대조 |
| `IMotorContactObservationRevision` | **선택적 접촉 관측 세대**. 3D Probe 재활성화 후 새 Physics Step 없이 이전 관측을 `Ready`로 보고하지 않음 |
| `MotorContactFeedbackEvaluator` | 접촉 Normal·의도/실제 속도의 Blocking·접선 Slide **관측**, Physics Solver가 아님 |
| `Adapter.MotorGroundSupport<TState>` | **연결 어댑터.** 기존 `IGroundChecker.Check`·`GetGroundPointVelocity` 결과를 재계산 없이 현재 Step에 연결. Motion·Motor 본체는 참조하지 않으며 Host가 이 관측으로 `MotionFeedback`을 채움 |

**폴더 구성:** `Motor/`(`Motor`·`MotorSnapshot`), `Backend/`(Backend 계약 인터페이스), `Contact/`(접촉 읽기 `MotorContactReader`·관측 `MotorContactFeedback`), `2D/`(Rigidbody2D Backend·`MotorState2D`), `3D/`(Rigidbody3D Backend·`MotorCollisionProbe3D`·`MotorState3D`), `Adapter/`(Character Controller 연결). 네임스페이스는 폴더와 관계없이 `inonego.Xeri.Game.Motor` 하나다.

## 순서가 중요한 사용법

~~~text
1. Host가 MotionActorKey로 MotionModel/Motor 및 Backend를 만든다.
2. MotionModel.Step(tick, inputs, feedback) 또는 StepLocal → MotionFrame
3. Motor.TryApply(frame)                      // 다음 BeginPhysicsStep 전 한 번
4. Motor.BeginPhysicsStep()                  // 이전 Contact/Support 무효화
5. Unity 또는 Host가 실제 Physics World Step을 실행한다. // Motor는 실행하지 않음
6. Motor.CompletePhysicsStep(stepIndex)
7. Motor.ReadCurrentState(), MotorContactReader.Read(buffer, out count)
8. 선택적으로 MotorGroundSupport.CheckAndObserve(stepIndex, delta, worldPoint)
   → TryRead(out sample)
9. Host가 Contact·Support로 다음 Tick의 MotionFeedback을 만들고, 결과를 소비하거나 권한 있는 Current State를 보정한다.
~~~

**주의:** Unity 자동 Physics PlayMode에서는 **Begin → `WaitForFixedUpdate` → 실제 Contact → Complete** 순서를 검증했다. 이것이 실제 Game Host의 자동 연결을 구현했다는 뜻은 아니다. Begin/Complete를 동일 `FixedUpdate`에 나란히 호출한다고 Physics 완료가 보장되지는 않는다. Host는 물리 World Step 전후 시점을 직접 보장해야 한다.

### Current-State Correction과 History는 별개

- `ReadCurrentState` / `ApplyCurrentState(in state)`는 **현재** Body Pose·Velocity. `ApplyCurrentState`는 이전 Motor History Generation·Contact/Support 관측을 무효화하고 `DiscontinuityVersion`/`LastDiscontinuity`를 갱신한다. 원격 Actor Authority/Session 매핑은 **호출 전 Host가 검증**해야 한다.
- `TryCaptureHistorical` / `RestoreHistorical`은 Backend가 `IMotorHistoricalBackend`를 구현했을 때만 과거 Owned Motor State에 적용된다. **캡처·복원 콜백 안의 Correction·Restore는 재진입으로 거절된다. 복원이 성공한 경우에만 관측을 무효화하고 불연속을 기록한다.** History 세대는 `ApplyCurrentState`에서만 올라가므로 복원 뒤에도 같은 History의 다른 Snapshot으로 다시 복원할 수 있다. 버려진 미래 Snapshot 정리는 Host 책임이다. `MotorSnapshot`은 Wire State나 World/Contact History가 아니다. Fake Historical Backend 계약 테스트를 통과했다고 Unity Rigidbody Full Rollback을 지원하는 것은 아니다.
- `Motor.TryApply`는 결과의 Actor와 실제 Actor를 비교하지만, 네트워크 권한이 있는지 **인증하지 않는다**. Physics Step 중이거나 다음 `BeginPhysicsStep` 전에 다시 적용하면 `InvalidOperationException`이다.
- **Body 수명:** Rigidbody가 파괴된 뒤 Motor를 계속 호출하면 Unity의 `MissingReferenceException`이 그대로 나간다. Actor를 파괴하기 전에 Host가 해당 Motor·Model 사용을 멈추고, 리스폰은 새 Generation의 `MotionActorKey`로 새 Model·Motor를 만든다.

### Contact와 Ground Support

- 진행 중인 Physics Step에서 `BeginPhysicsStep`을 재호출하면 `InvalidOperationException`을 반환하며 기존 Step을 덮어쓰지 않는다. Contact Backend의 Begin이 실패한 경우 진행 중인 Step으로 확정하지 않아 재시도할 수 있다. **초기화 콜백 안의 Correction·중첩 Begin은 재진입으로 거절되며, 이때 Step은 시작되지 않는다.** 진행 중 Correction으로 취소된 Step의 `CompletePhysicsStep`은 거절한다. 실제 Physics Step 수행 여부는 여전히 Host가 보장한다.
- Contact는 `MotorContactReader`로 읽는다. Motor는 Contact 표본의 시작(`BeginPhysicsStep`)과 무효화(Correction·Restore)만 관리한다.

  ```csharp
  var contacts = new MotorContactReader<MotorState2D, ContactPoint2D>(motor);
  var buffer = new ContactPoint2D[16];

  // Physics Step 완료 뒤
  if (contacts.Read(buffer, out var count) == MotorContactReadStatus.Ready) { /* buffer[0..count) */ }
  ```

  Backend가 그 Contact 형식을 제공하지 않으면 **생성 시** `ArgumentException`이다(예: 2D Motor에 `ContactPoint`). `Read`는 **호출자 소유 배열**에 기록한다. `Ready`는 현재 Step 관측이 가능함을, `NotObserved`는 아직 유효한 Step이 없음을, `Unsupported`는 Backend가 지금 Contact를 제공하지 않음(예: 3D Probe 없음·비활성)을 뜻한다. `Ready + count=0`은 **정상적으로 관측한 Step에서 접촉이 없다는 뜻**이다. 센서가 해당 Step에서 활성화되지 않았거나 관측 세대가 달라졌다면 `NotObserved`로 구분한다.
- **접촉을 복사하는 도중 Physics Correction 또는 센서 관측 세대가 바뀌면 `NotObserved`로 거절한다.** 3D Contact는 Rigidbody와 같은 GameObject의 **활성** `MotorCollisionProbe3D`가 필요하다. Probe가 비활성화되면 버퍼를 비우고 그동안 전달되는 Unity 충돌 콜백도 무시해, 다시 활성화해도 오래된 Contact를 내보내지 않는다. **Probe의 비활성화 시 관측 세대를 증가**시키므로 새 Physics Step 없이 재활성화만 하면 `NotObserved`다. 한 Step 최대 32개이며 `ContactCount`/`Truncated`로 내부 절단을 확인한다. 실제 Unity PlayMode에서 다중 Collider 충돌 48개로 상한과 5개짜리 호출 배열 복사를 검증했다. 호출 배열이 작은 경우 복사 수는 적지만 Probe 내부 `Truncated`와는 별개다. 2D는 `Rigidbody2D.GetContacts`를 사용한다.
- `MotorContactFeedbackEvaluator`는 **첫 차단 Normal 하나**를 선택해 실제 Slide 성분을 계산한다. 의도·실제 속도가 유한하지 않으면 `ArgumentOutOfRangeException`이다. 여러 면 충돌의 최적 Solver나 지면 운동 정책이 아니다.
- `MotorGroundSupport`는 완료된 Step에만 Checker를 실행한다. Checker 이벤트가 같은 Support의 관측이나 Motor Correction을 다시 요청하면 **재진입으로 `InvalidOperationException`을 던지며 같은 Step의 Checker를 중복 실행하지 않는다**. 같은 Step/World Point 재요청은 중복 Check하지 않으며 다른 Point로 재관측하려면 새 Step이 필요하다. **관측된** Checker GameObject 변경은 해당 Step 표본을 영구 무효화하므로 A→B→A 재연결로 이전 속도가 부활하지 않는다. **Owner 조회 콜백 안의 Correction도 같은 이유로 거절된다.** 실제 Ground Collider A→B 전환은 다음 Physics Step에서 재검사하고 새 Point Velocity를 읽는다. Groundless Actor는 `HasChecker=false, IsOnGround=false, GroundPointVelocity=0`인 정상 표본이다. **Checker가 파괴된 Owner의 관리 참조를 계속 반환해도(실제 `GroundChecker`가 `GameObject`를 필드로 보관하는 구조) Owner 조회는 Unity 파괴 수명 기준으로 한 경로에서만 판정하므로, Check 도중 Owner가 파괴되면 그 Step의 표본을 확정하지 않는다.** 다음 Step은 Owner 없는 Groundless 표본이 된다.
- `GetGroundPointVelocity(worldPoint)`를 다시 선/각속도로 합성하거나 Motor Body에 자동 더하지 않는다. 이동 플랫폼·Jump·Grounded State Machine은 외부 게임 정책이다.

## 내장 Backend의 지원 범위

| 기능 | Rigidbody2D | Rigidbody3D |
| --- | --- | --- |
| Current State Read/Apply | 지원 | 지원 |
| 합성 결과 적용 | X·Y 선속도, Z 각속도 | Up 축 기준 Planar·Elevation, 각속도 |
| Contact Read | `ContactPoint2D` | 활성 Probe가 있을 때만 `ContactPoint` |
| Historical World Restore | **미지원** | **미지원** |

적용 규칙(Set/Free/Impulse)은 `MotionFrame.ComposeLinear/ComposeAngular` 하나를 두 Backend가 공유한다. `MotionFrame.IsValid`(유한한 값, 단위 Up, 정의된 채널 방식, 유효한 Actor·Tick)를 통과하지 못하거나 계산 결과(축 투영·합산 오버플로 포함)가 유한하지 않으면 **어떤 값도 쓰지 않고 `false`**다. 3D `ApplyCurrentState`의 회전은 성분이 유한해도 크기 제곱이 오버플로하면 거절한다. 스턴·속박·넉백은 Body가 아니라 Model 층에서 합성되며 Backend는 Constraint를 따로 다루지 않는다. Restore는 `MotorRestoreStatus`로 ActorMismatch/Unsupported/StaleHistory/BackendRejected/Restored를 구분한다.

## 검증과 한계

테스트 수와 검증 수준은 [구현 플랜](../../../../../Docs/systems/movement/implementation-plan.md)의 "현황과 검증"에만 있다. 재진입·수명 정책은 [시스템 설계 §3](../../../../../Docs/systems/movement/game-module-design.md)에 있다. 관련 테스트: [Motor EditMode](../../../../Tests/EDIT/Modules/Locomotion/Motor/TEST_Motor.cs), [Support EditMode](../../../../Tests/EDIT/Modules/Locomotion/Motor/TEST_MotorGroundSupport.cs), [2D/3D Physics PlayMode](../../../../Tests/PLAY/Modules/Locomotion/Motor/TEST_MotorPhysics.cs), [Support PlayMode](../../../../Tests/PLAY/Modules/Locomotion/Motor/TEST_MotorGroundSupport.cs), [이동 시나리오 PlayMode](../../../../Tests/PLAY/Modules/Locomotion/Movement/TEST_MovementScenario.cs).

**아직 확인하지 않은 범위:** 실제 Game Host의 자동 Physics 시점, 복잡한 Collider/World/Support 교체 조합, 대규모 다중 Actor·Unity Player/IL2CPP·Motor GC/CPU, 외부 Provider/Replay Adapter P5. 다른 환경에서 재현할 때는 [인수인계의 Unity 환경 조건](../../../../../Docs/systems/movement/motor-handoff.md)을 먼저 확인한다.
