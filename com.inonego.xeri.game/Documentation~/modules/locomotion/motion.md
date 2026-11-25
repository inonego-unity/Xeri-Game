# Motion

Xeri Motion은 Actor 한 명의 기본 의도와 사용처가 넣은 Modifier 값을 채널별 Core `MValue`로 접어, Tick마다 하나의 `MotionFrame`을 만드는 Model입니다.

## 왜 필요한가

걷기, 대시, 루트 모션, 스턴, 둔화, 넉백, 바람이 각자 Body를 바꾸면 서로 덮어씁니다. Motion은 모든 기여를 "Base + Order 순 Modifier"라는 한 가지 방식으로 접습니다. 무엇이 무엇을 가리는지는 Order만으로 정해지므로 저지 불가 대시나 스턴 중 넉백 같은 규칙을 Motion에 새 개념을 넣지 않고 표현할 수 있습니다.

Motion은 받은 값을 접기만 합니다. 값을 만드는 일(Track 재생, 감쇠, 벽 방향 성분 제거)과 수명, 효과의 의미는 사용처가 맡습니다.

## 언제 사용하는가

- 입력, AI, Ability, 영역 효과가 같은 Actor의 이동을 동시에 바꿀 때
- 스턴·둔화·넉백·대시의 우선 관계를 데이터(Order)로 정하고 싶을 때
- 대시나 공격 모션이 끝나면 걷기가 자동으로 이어져야 할 때
- Tick 단위로 상태를 저장하고 되돌려 다시 실행해야 할 때

## 핵심 모델

```text
채널(Planar·Elevation·Rotation)마다
  Base = 기본 의도(MotionIntent), 한 Tick 수명
    ↓ Modifier(Order 오름차순, 같은 Order는 나중에 시작한 항목이 뒤)
  Modified = 이 채널의 Set 값 (Planar는 이 결과만 Up에 수직인 평면에 투영)
```

| 개념 | 내용 |
|---|---|
| 채널 값 | `Planar`(`MValue<Vector3>`), `Elevation`(`MValue<float>`, Up 방향 스칼라), `Rotation`(`MValue<Vector3>`, rad/s). Model 내부 상태이며 외부에 노출하지 않음 |
| Modifier 항목 | `MotionModifierCommand`로 적용·종료. 키(`MotionSourceKey.Key`)가 그대로 채널 `MValue`의 Modifier 키 |
| 값 | `MotionChannelValues`(채널, Planar, Elevation, Rotation). 연산과 무관하게 그대로 채널 Modifier의 피연산자 |
| 연산 | Core `NumericFOperation` 전부(`SET`·`ADD`·`SUB`·`MUL`·`DIV`·`MIN`·`MAX`). 벡터는 성분별 |
| 가려짐 | 채널에서 마지막으로 입력을 덮어쓰는(`IModifier.Replaces`, 곧 `SET`) Modifier보다 앞의 항목은 그 채널에서 가려집니다 |
| Up 축 | Planar와 Elevation을 나누는 기준. Step 입력으로 Tick마다 바꿀 수 있고 비우면 유지합니다. Snapshot에 들어갑니다 |

### 명령

| 명령 | 내용 | 예 |
|---|---|---|
| `Apply(source, order, operation, values, actionID)` | 키가 없으면 시작, 같은 세대면 연산과 값을 갱신, 더 큰 세대면 교체 | 대시 `SET`, 넉백 `ADD`, 스턴 `MUL 0` |
| `Impulse(source, order, values)` | 한 Tick만 적용하는 `ADD`. 같은 Tick의 충격은 키마다 하나이고 지속 항목과 같은 키 공간을 씀 | 점프, 순간 밀기 |
| `End(source, keepVelocity)` | 같은 키·세대 항목 종료. `keepVelocity`면 그 항목이 Body에 남긴 속도를 Free 채널에서 되돌리지 않음 | 대시 취소, 스턴 해제, 플랫폼 이탈 |
| `Clear()` | 모든 항목 종료 | 리스폰 |

값은 `MotionChannelValues`로 만듭니다.

| 생성 | 내용 |
|---|---|
| `new MotionChannelValues(channels, planar, elevation, rotation)` | 채널별 값을 직접 지정. Elevation은 Up 방향 크기라 Up을 따라 돎. 켜지 않은 채널의 값은 검사하지 않고 Model이 0으로 바꿔 보관 |
| `MotionChannelValues.Linear(channels, linear, angular)` | 월드 선속도와 각속도. Model이 매 Tick 현재 Up으로 Planar·Elevation을 나누므로 Up이 바뀌어도 월드 방향이 유지됨 |
| `MotionChannelValues.Uniform(channels, value)` | 모든 채널·성분에 같은 값. `MUL` 배율에 씀 |

### Order 배치 예시

Order의 의미는 사용처가 정합니다. Motion은 숫자만 비교합니다.

| Order(예) | 항목 | 결과 |
|---|---|---|
| 0 | 둔화 `MUL 0.5` | 걷기에만 걸리고, 뒤의 대시가 덮어써 대시에는 걸리지 않음 |
| 10 | 대시 `SET` | 걷기와 앞의 둔화를 가림 |
| 20 | 넉백·바람 `ADD` | 대시에 더해짐 |
| 30 | 대시에도 걸리는 둔화 `MUL` | 대시와 넉백 합에 곱해짐 |
| 40 | 대시를 끊는 넉백 `SET` | 대시를 가리고 현재 동작이 됨 |
| 50 | 스턴 `MUL 0` | 앞의 모든 기여를 멈춤(Set 채널 기준, Free 채널의 `ADD`는 멈추지 못함). 스턴 중에도 밀려야 하는 넉백은 이보다 뒤에 둠 |
| 60 | 저지 불가 대시 `SET` | 넉백·스턴을 모두 가림 |

넉백 저항처럼 특정 기여만 줄여야 하면, 넉백 벡터를 만들 때 사용처가 줄여서 넣습니다. `MUL`은 앞서 접힌 값 전체에 곱해지기 때문입니다. 벡터 `MIN`·`MAX`는 성분별 비교라 속도 크기 제한이 아닙니다.

## 채널 모드와 Free 채널

```text
Set  : 기본 의도가 있거나 보이는 SET이 있는 채널 → 채널 MValue의 Modified
Free : 둘 다 없는 채널 → Body 속도(중력·낙하) 유지
```

- Free 채널에는 보이는 지속 `ADD`·`SUB` 합의 **이번 Tick 변화량**과 충격만 `MotionFrame.Impulse`로 넘깁니다. Set 채널에서 이미 반영한 몫은 기록해 두어 Free로 바뀌어도 다시 더하지 않습니다.
- Free 채널에서 `ADD`·`SUB` 외의 연산은 적용하지 않습니다. Body 속도에 매 Tick 곱하면 지수적으로 줄어들기 때문입니다. 스턴으로 멈추려면 의도를 0으로라도 보냅니다.
- Rotation이 Free면 Body 각속도를 유지합니다. 각속도 증분은 없으므로 이때 Rotation의 `ADD`·`SUB` 항목은 효과 없이 용량만 씁니다.
- 3D는 `MotionModel.WorldUp`, 2D 위에서 보기는 `MotionModel.TopDown2DUp`(Z), 2D 옆에서 보기는 `WorldUp`(Y)을 씁니다.

`MotionFrame.ActionID`와 `ActionStartTick`은 보이는 `SET` 중 가장 뒤 항목의 `actionID`와 시작 Tick입니다. 의미는 사용처가 해석합니다.

## Up 축 바꾸기

중력 방향이 바뀌는 게임(행성, 벽 걷기)은 Tick마다 Up을 넘깁니다.

```csharp
buffer.SetUp(-gravityDirection);        // 또는 new MotionStepInput(intents, modifiers, up)
var frame = model.StepLocal(deltaTime, buffer.AsStepInput());
```

- 영벡터면 현재 Up을 유지합니다. 유한하지 않은 벡터는 상태를 바꾸기 전에 `ArgumentOutOfRangeException`입니다.
- 새 Up은 그 Tick의 의도 분해부터 적용됩니다. Modifier 값은 다시 넣지 않아도 됩니다. `Linear`로 만든 월드 선속도는 새 Up으로 다시 나뉘어 월드 방향이 유지되고, 직접 지정한 Elevation은 Up 방향 크기라 새 Up 방향으로 읽힙니다(Up 기준 부스트 등).
- Free 채널의 반영한 몫은 월드 벡터로 기록하므로 Up이 바뀌어도 가짜 충격이 쌓이지 않습니다.
- Up은 Snapshot과 Step 입력에 함께 들어가므로 재실행이 같은 결과를 냅니다.
- 중력 자체(중력장 정의, `useGravity`를 끄고 힘으로 적용)는 사용처가 합니다.
- Body 자세를 새 Up에 맞추려면 `MotionOrientation.AlignUp(rotation, up, deltaTime)`이 돌려주는 각속도(rad/s)를 Rotation 채널 항목으로 넣습니다. Body 회전은 Unity 규칙(로컬 +Y가 Up, +Z가 Forward)으로 해석하므로, 루트 축이 다른 모델은 오프셋 회전을 곱해 넘깁니다(`rotation * modelToStandard`). 두 축 사이의 최소 회전이라 바라보던 방향은 새 평면으로 옮겨져 유지되고, 정반대 Up이면 Forward를 축으로 돌아 Up만 뒤집습니다. 크기가 1이 아닌 회전은 정규화해 쓰고, 1µs보다 짧은 Tick 간격은 예외입니다.

## 사용처가 맡는 값과 수명

Motion에는 지속 시간, 감쇠, Track 재생, 가려질 때 정지 옵션이 없습니다. 효과를 소유한 쪽이 자기 시간과 값을 갖고 결과만 넣습니다.

| 할 일 | 사용처의 방법 |
|---|---|
| 지속 시간 | Status·Ability의 Core `Timer`로 재고 끝나면 `End` |
| 감쇠 | 매 Tick `value * Mathf.Exp(-rate * delta)`를 계산해 같은 세대 `Apply` |
| 루트 모션 | 재생 시간을 갖고 `MotionTrack.EvaluateVelocity`로 평가해 같은 세대 `Apply`. 끝나면 `End` |
| 벽 방향 성분 제거 | `MotionFeedback.RemoveBlocked(velocity)`의 결과를 자기 값으로 보관해 다시 넣음. 보관하므로 벽에서 떨어져도 되살아나지 않음 |
| 가려진 동안 정지 | `TryGetModifier(source, out state)`의 `IsVisible`을 보고 자기 Timer·재생 시간을 멈춤. 보임 여부는 마지막 Step 기준이라 다음 Tick부터 반영됨 |

같은 세대 `Apply`는 시작 순서와 시작 Tick을 유지하므로 매 Tick 값을 바꿔도 Order 안의 순서와 `ActionStartTick`이 바뀌지 않습니다. 이 경로는 할당이 없습니다.

### 이동 플랫폼

- 플랫폼 위에서는 플랫폼 기준 상대 속도를 의도로, 지면 접점 속도(`MotorGroundSupport`의 `GroundPointVelocity`)를 스턴보다 뒤 Order의 `ADD` 항목으로 넣고 매 Tick 같은 세대로 갱신합니다. 둔화·스턴은 상대 속도에만 걸리고 플랫폼 속도는 그대로 남습니다.
- 떠날 때는 `End(source, keepVelocity: true)`로 끝냅니다. 의도 없는 Free 채널에서도 플랫폼에서 받은 속도가 Body에 남아 관성이 유지됩니다. 그냥 `End`하면 그만큼 되돌리는 충격이 걸립니다.
- 공중에서도 의도를 보내면 그 채널은 Set이라 의도가 속도를 정합니다. 이때 관성을 유지하려면 의도를 Body 속도에서 이어서 계산합니다.
- 회전하는 플랫폼의 각속도는 지면 관측에 아직 없습니다.

## 루트 모션 Track

`MotionTrack`은 Actor 기준 로컬 누적 변위와 Yaw를 일정한 간격으로 담은 불변 데이터입니다. Motion Model은 Track을 모릅니다. 사용처가 재생 시간과 재생 속도를 갖고 Tick마다 평가합니다.

```csharp
attackTrack.EvaluateVelocity(playTime, playTime + deltaTime * rate, deltaTime, feedback.Rotation, out var linear, out var angular);

var values = MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Rotation, linear, angular);
buffer.Add(MotionModifierCommand.Apply(new MotionSourceKey("Attack", 1), 10, NumericFOperation.SET, values, actionID: 3));

playTime += deltaTime * rate;
```

- Track 변위는 Body 회전으로 월드에 놓아 Tick 속도로 바꾸고, Yaw 변화는 Body Up(회전의 로컬 +Y) 축 각속도가 됩니다.
- Track 데이터와 평가는 Body 로컬 규칙(+Y 위, +Z 앞)을 따릅니다. 2D 위에서 보기(Up이 Z)처럼 Body의 위가 로컬 +Y가 아닌 구성에는 맞지 않습니다.
- Animator 없이 평가되므로 서버에서도 같은 결과가 나옵니다.
- Track을 만드는 일(AnimationClip 굽기, 데이터 테이블 조회)과 Track 선택은 사용처가 합니다.

## 기본 사용

```csharp
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Serializable;

var model = new MotionModel(actorKey, MotionModel.WorldUp);
var buffer = new MotionInputBuffer();

var dash = new MotionChannelValues(MotionChannel.Planar, dashVelocity);
var stun = MotionChannelValues.Uniform(MotionChannel.All, 0f);
var knockback = MotionChannelValues.Linear(MotionChannel.Planar | MotionChannel.Elevation, hitVelocity);

buffer.Add(new MotionIntent(MotionChannel.Planar, moveVelocity));
buffer.Add(MotionModifierCommand.Apply(new MotionSourceKey("Dash", 1), 10, NumericFOperation.SET, dash, actionID: 7));
buffer.Add(MotionModifierCommand.Apply(new MotionSourceKey("Knockback", 1), 20, NumericFOperation.ADD, knockback));
buffer.Add(MotionModifierCommand.Apply(new MotionSourceKey("Stun", 1), 50, NumericFOperation.MUL, stun));

var frame = model.StepLocal(deltaTime, buffer.AsStepInput());
buffer.Clear();

motor.TryApply(frame);
```

`MotionStepInput`은 ref struct라서 필드로 보관하거나 `[UnityTest]` 반복기 안에서 만들 수 없습니다. Host는 `MotionInputBuffer`에 입력을 모았다가 Step 직전에 `AsStepInput()`으로 묶습니다.

## 입력 결과 확인

입력별 결과가 필요하면 `MotionStepStatuses`를 넘깁니다.

```csharp
var statuses = new MotionInputStatus[commands.Length];
var frame = model.StepLocal(deltaTime, new MotionStepInput(modifiers: commands), new MotionStepStatuses(modifiers: statuses));
```

결과는 `Accepted`, `Updated`, `InvalidInput`, `StaleGeneration`, `NotFound`, `CapacityReached` 중 하나입니다.

- 같은 키에 더 큰 Generation은 이전 항목을 대신하고(`Accepted`), 작은 Generation은 `StaleGeneration`입니다.
- 같은 키·같은 Generation의 `Apply`는 시작 순서와 시작 Tick을 유지한 채 연산과 값을 갱신합니다(`Updated`).
- 같은 Generation으로 채널·Order·충격 여부를 바꾸면 `InvalidInput`입니다. 구조를 바꾸려면 Generation을 올려 새로 시작합니다.
- 용량(기본 16)을 넘는 새 항목은 `CapacityReached`로 거절되고 기존 항목은 그대로입니다.
- 충격은 지속 항목과 같은 키를 씁니다. 같은 세대면 `InvalidInput`이고, 더 큰 세대의 충격은 같은 키의 지속 항목을 대신한 뒤 Step이 끝나면 사라지므로 지속 효과가 되살아나지 않습니다. 충격에는 지속 항목과 다른 키를 씁니다.

## 항목 상태 조회

`TryGetModifier(source, out MotionModifierState state)`는 같은 키·세대 항목의 Order, 연산, 값, `ActionID`, 시작 Tick, 충격 여부, 마지막 Step 기준 보임 여부를 값으로 돌려줍니다. Model 내부의 `MValue`와 Modifier 인스턴스는 노출하지 않습니다. 충격 항목은 Step이 끝나면 사라지므로 Step 뒤에는 찾을 수 없습니다.

## 오래 유지하는 Modifier

사다리나 탑승처럼 해제 시점을 다른 코드가 정하는 Modifier는 `MotionInputBuffer.AcquireModifier`가 Core `Lease`를 돌려줍니다. Lease를 해제하면 종료 명령이 버퍼에 들어가고, 다음 Step에서 항목이 끝납니다. 충격 명령은 Lease로 받지 않습니다.

## Snapshot과 Restore

`Capture()`, `Capture(snapshot)`, `Restore(snapshot)`으로 항목 메타데이터와 채널 `MValue`를 함께 저장하고 되돌립니다.

- 채널 값은 Core `MValue.CloneFrom`으로 복사합니다. 같은 Snapshot을 재사용하고 Modifier 구성이 같으면 할당이 없습니다.
- Snapshot 뒤 항목이 생기거나 끝났다면 Restore가 채널 Modifier 목록을 다시 만들며, 이때는 할당이 있습니다.
- 항목이 시작될 때 Modifier를 새로 만들고 끝날 때(`End`, `Clear`, 충격 만료) 채널 목록에서 빼므로 시작과 종료에 할당이 있습니다. 같은 세대 값·연산 갱신, 유지 중인 항목의 Step, `TryGetModifier`는 할당이 없습니다.
- 기본 의도는 한 Tick 수명이라 Snapshot에 넣지 않습니다. `StepGeneration`은 Step과 Restore마다 오르는 발급 토큰이라 되돌리지 않습니다.
- 사용처가 가진 시간과 값(Timer, Track 재생 시간, 감쇠 중인 값)은 Motion Snapshot에 들어가지 않습니다. 함께 되돌리려면 사용처가 자기 상태를 복제합니다(Core `Timer`·`Value` 계열의 `CloneFrom`).
- 다른 Actor나 다른 용량의 Snapshot은 `ArgumentException`입니다.

## 소유권과 수명

- Model 하나는 `MotionActorKey` 하나를 소유합니다. `ScopeID`는 로컬 실행 인스턴스(서버·클라이언트) 구분이며 Network Object ID가 아닙니다.
- 채널 `MValue`와 그 Modifier는 Model이 소유하고 외부에 노출하지 않습니다. Body에 적용할 값은 `MotionFrame`에서 읽습니다.
- 끝난 항목의 세대는 기억하지 않으므로 늦게 도착한 네트워크 입력의 중복 제거는 Adapter가 맡습니다.

## 실패와 거절

- 잘못된 Tick(`delta <= 0`, 마지막 Tick 이하의 번호)과 입력보다 작은 결과 버퍼는 상태를 바꾸기 전에 예외로 거절합니다.
- 명령 값 검증 실패(키 없음, 정의되지 않은 채널·연산, 켠 채널의 유한하지 않은 값, 같은 세대의 채널·Order·충격 여부 변경)는 예외가 아니라 `InvalidInput` 입력별 결과입니다. 나머지 입력은 계속 적용됩니다.
- 합성 결과가 Body에 적용될 수 있는 형태인지는 `MotionFrame.IsValid`로 확인할 수 있습니다. Motor의 Backend는 유효하지 않은 프레임을 거절합니다.
- 각 값이 유한해도 합이 넘치면(1e38 수준) 프레임이 유효하지 않게 됩니다. Free 채널은 반영한 몫 기록도 함께 넘치므로 그 항목을 끝낸 다음 Tick까지 한 번 더 유효하지 않은 프레임이 나옵니다. 값의 범위는 사용처가 제한합니다.

## 책임 범위

### 담당하는 것

- 채널별 `MValue` 접기와 Planar 투영, Free 채널 규칙
- 입력 검증과 세대 규칙, 용량, 항목 상태 조회
- Snapshot과 Restore
- Step별 Up 축과 그에 맞춘 Free 채널 증분
- Model과 분리된 도구: `MotionTrack` 평가, `MotionFeedback.RemoveBlocked`, `MotionOrientation.AlignUp`

### 담당하지 않는 것

- 효과의 의미와 Order 배치(스턴인지, 대시를 끊는지 등), 스킬 사용 차단, 스턴 상태 표시
- 효과 수명, 감쇠, Track 재생 시간, 가려질 때 정지, 벽 방향 성분 제거를 적용할지
- 이동 속도 스탯과 넉백 저항 계산(사용처가 값에 반영합니다)
- Body 적용([Motor](motor.md)의 일)
- 네트워크 중복 제거와 Authority 검증

## 제약과 주의사항

- 같은 빌드와 같은 입력이면 같은 결과를 냅니다. 플랫폼 간 비트 단위 결정성은 약속하지 않습니다.
- 일시정지(`Time.timeScale = 0`) 중에는 Step을 호출하지 마세요. `delta <= 0`은 거절됩니다.
- 사용처의 시간도 Host가 주는 Tick 간격으로만 진행하세요. 재실행은 한 프레임에 여러 Tick을 몰아서 돌립니다.

## 관련 가이드

- [Tick 실행 가이드](../../guides/locomotion/run-a-tick.md)

## 관련 문서

- [Locomotion 전체 구조](overview.md) — 공통 정책과 확장 지점
- [Motor](motor.md) — 합성 결과 적용
- [Movement](movement.md) — 합성 결과가 만드는 이동 상태
