# Game Controller

Xeri Game Controller는 캐릭터 이동에 필요한 바닥 감지, 지면 부유 지지, 점프 상태를 서로 분리된 Runtime 객체로 제공합니다.

## 왜 필요한가

하나의 Character Controller가 입력, 바닥 감지, 지면 보정, 점프 허용 상태와 실제 Rigidbody force까지 모두 소유하면 각 기능을 독립적으로 교체하거나 테스트하기 어렵습니다. Xeri는 감지·계산·상태만 작은 Runtime 객체로 나누고 실제 물리 적용과 전체 조합은 프로젝트 Controller에 남깁니다.

## 언제 사용하는가

- Ground detection과 이동 로직을 분리하고 싶을 때
- Floating Capsule 방식의 지면 추종 계산이 필요할 때
- Coyote Jump와 Jump Buffer 상태만 재사용하고 실제 impulse는 프로젝트가 결정할 때
- 2D/3D 물리 차이를 같은 상위 흐름으로 다루고 싶을 때

완성형 이동 Controller가 필요한 경우에는 이 모듈만으로 충분하지 않으며 프로젝트 Motor/Driver가 필요합니다.

## 기본 사용

`Jumper`는 가장 독립적으로 사용할 수 있습니다.

```csharp
var jumper = new Jumper
{
    MaxCount = 1,
    CoyoteJumpDuration = 0.1f,
    JumpBufferDuration = 0.1f,
};

jumper.Init();
jumper.OnJump += (_, _) => ApplyJumpImpulse();

jumper.Trigger();
jumper.FixedTick(fixedDeltaTime, isGrounded);
```

실제 velocity 변경은 `OnJump` 소비자가 담당합니다. Ground Checker와 Suspension도 같은 방식으로 결과를 계산하고 최종 Rigidbody 적용은 프로젝트 Controller가 수행합니다.

## 구성

```text
GroundChecker
    ↓ 승인된 ground sample
GroundSuspension
    ↓ 지지 가속도
Rigidbody 적용은 호출자

Grounded 상태
    ↓
Jumper
    ↓ OnJump
실제 점프 impulse는 호출자
```

각 객체는 감지·계산·상태 계약을 제공하며 하나의 Character Controller 전체를 소유하지 않습니다.

## Ground Checker

`GroundCheckerBase`는 마지막으로 승인된 `GroundCheckSample`을 상태로 보관합니다. 2D/3D 구현은 실제 감지 방식과 Rigidbody 접근을 제공합니다.

`Check(deltaTime)` 흐름은 다음과 같습니다.

```text
Detect
→ ProcessGround
→ Sample 갱신
→ 접지 여부가 바뀐 경우 OnLand / OnLeave
```

같은 바닥을 유지하는 동안에도 거리와 법선은 매 검사마다 갱신됩니다. Ground GameObject만 다른 대상으로 교체됐지만 계속 접지 상태라면 Land/Leave 이벤트를 발생시키지 않습니다.
## Ground Suspension

`GroundSuspension`은 승인된 ground sample과 Rigidbody 상대 운동을 사용해 지면 추종 가속도를 계산합니다. 2D/3D 구현은 Capsule 형상과 물리 API 차이만 제공합니다.

`Init()`에서 원래 Capsule 형상을 보존하고 부유 높이에 맞게 형상을 구성하며, `Release()`에서 원래 형상을 복원합니다.

주요 상태는 다음과 같습니다.

- 최초 접지는 `TargetDistance + ContactOffset` 범위 안에서만 획득합니다.
- 지면 추종 중에는 `MaximumDistance`까지 내려가는 지면을 따라갈 수 있습니다.
- `Detach()` 후에는 실제 하강 착지 범위에 돌아오기 전까지 지면을 재획득하지 않습니다.
- 설정의 최소 surface alignment를 만족하지 않는 표면은 지지하지 않습니다.

`Sample()`은 계산 결과를 반환할 뿐 실제 Rigidbody에 힘을 적용하지 않습니다.

## Jumper

`Jumper`는 점프 횟수, Coyote Jump와 Jump Buffer 상태를 관리합니다. 물리 Tick에서 호출자가 전달한 접지 상태를 기준으로 갱신합니다.

```text
Trigger
→ Jump Buffer 시작
→ FixedTick
→ 접지/Coyote 상태 확인
→ TryJump
→ Count 감소 + OnJump
```

실제 velocity 또는 impulse 변경은 `OnJump` 소비자가 담당합니다.

## 책임 경계

- Ground Checker: 바닥 감지와 승인된 표본 상태
- Ground Suspension: 표본에서 지지 가속도 계산
- Jumper: 점프 요청과 허용 상태
- Consumer: Rigidbody force/velocity, 입력 해석, 전체 이동 composition

## 관련 문서

- [Timer](https://inonego-unity.github.io/Xeri/docs/modules/utility/timer.html)
- [Xeri Game](../index.md)
