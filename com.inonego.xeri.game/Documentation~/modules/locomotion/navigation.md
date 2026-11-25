# Navigation

Xeri Navigation은 Actor별 목표와 경로의 수명을 관리하고, 소비자에게 다음 Waypoint를 안내하는 계약입니다. 경로 계산과 이동은 하지 않습니다.

## 왜 필요한가

경로 계산은 비동기이거나 여러 프레임에 걸치는 일이 많습니다. 그 사이 목표가 바뀌거나 교정으로 위치가 달라지면, 늦게 도착한 결과나 완료 통지가 현재 경로를 망칩니다. Navigation은 목표 Generation, 요청 ID, 경로 ID로 오래된 결과를 구별해 거절합니다.

## 언제 사용하는가

- AI나 입력이 "저기로 가라"는 목표를 주고, 외부 Planner(NavMesh 등)가 경로를 계산할 때
- Planner 결과가 비동기로 돌아오는데 오래된 결과를 안전하게 버려야 할 때
- 경로 진행 상태를 저장하고 복원해야 할 때

경로가 고정된 직선 이동처럼 수명 문제가 없다면 이 모듈 없이 목표 지점만 `MotionIntent`로 넣어도 충분합니다.

## 기본 사용

```csharp
using inonego.Xeri.Game.Navigation;

var navigation = new NavigationController<Vector3>(actorKey);
navigation.SetGoal(destination);

// Host가 월드 Revision(1부터 발급하는 Core `Revision`, `None`은 거절)과 현재 시작 위치를 제공합니다.
var request = navigation.BeginPlan(currentPosition, worldRevision);
planner.Request(request);   // INavigationPlanner<Vector3>를 구현한 소비자

// 외부 Planner가 경로를 계산한 뒤, 비동기 콜백에서도 같은 request를 돌려줍니다.
var status = navigation.TryAcceptPath(request, path);

if (navigation.TryGetGuidance(out var guidance))
{
    // 이동 정책은 Host가 정합니다. Motion 입력으로 바꿔 Motor에 전달합니다.
    var nextPoint = guidance.Target;
    // 도착이 확인되면:
    navigation.TryAdvanceWaypoint(guidance);
}

// World Shift, Motor 교정, 장애물 재계획 시점은 Host가 판단합니다.
navigation.Invalidate();    // 목표는 유지하고 이전 경로와 지연 응답을 폐기
```

`NavigationController`는 Actor별 순차 호출을 전제로 하며 여러 스레드의 동시 접근을 동기화하지 않습니다. 비동기 Planner의 Worker 결과는 Host가 해당 Actor의 실행 스레드로 전달한 뒤 `TryAcceptPath`를 호출해야 합니다.

## 핵심 구조

| 타입 | 역할 |
|---|---|
| `NavigationController<TPoint>` | Actor별 `SetGoal`, `Cancel`, `Invalidate`, 경로 요청·승인·실패, Waypoint 진행, Owned Snapshot 복원 |
| `NavigationPlanRequest<TPoint>` | Planner에 넘기는 요청: Actor, Goal Generation, 재사용되지 않는 Request ID, World Revision, Start와 Goal |
| `INavigationPlanner<TPoint>` | 사용처 Planner가 `Request(in request)`로 요청을 소비하는 계약. 특정 NavMesh 구현을 강제하지 않습니다 |
| `NavigationGuidance<TPoint>` | 다음 Waypoint와 Actor, Goal Generation, Path Generation, Waypoint Index |
| `NavigationSnapshot<TPoint>` | 독립 복사한 Goal, Path, 진행 상태 |

`TPoint`는 `unmanaged` 형식이면 되므로 `Vector2`, `Vector3`, 격자 좌표를 같은 방식으로 씁니다.

## 경로 결과 처리

`TryAcceptPath`는 현재 Actor, Goal Generation, Request ID, World Revision의 요청만 승인합니다.

- 오래된 결과는 `Stale`, 빈 경로는 `InvalidPath`입니다. 빈 경로를 받은 뒤에도 같은 요청으로 다시 제출할 수 있습니다.
- 경로는 `ReadOnlySpan<TPoint>`로 받으며 수락 도중 외부 코드를 호출하지 않습니다. 배열을 그대로 넘길 수 있습니다.
- `TryRejectPath`는 Planner의 실패를 `Failed`로 기록합니다.
- 목표 변경, `Cancel`, `Invalidate`는 이전 계획을 무효화합니다. `Goal`은 도착(`Arrived`) 이후에도 조회할 수 있습니다.

## Waypoint 진행

**지연 완료 통지에는 `TryAdvanceWaypoint(guidance)`를 사용합니다.** 현재 Actor, Goal Generation, Path Generation, Waypoint Index가 모두 같을 때만 진행하므로, 이전 경로의 늦은 완료와 중복 완료를 거절합니다. 인자 없는 `AdvanceWaypoint()`는 같은 호출 흐름에서 즉시 도착을 판정할 때만 쓰세요. 비동기 완료 콜백에 쓰면 오래된 완료를 통과시킬 수 있습니다.

## Owned State와 Correction

```csharp
var snapshot = navigation.Capture();
var result = navigation.Restore(snapshot, currentWorldRevision);
```

- `Restore`는 Actor가 다르면 `ActorMismatch`, 캡처 시 World Revision과 현재 값이 다르면 `WorldMismatch`를 반환하고 상태를 바꾸지 않습니다. 현재 World Revision이 `None`이면 `ArgumentOutOfRangeException`입니다. 캡처 당시 요청한 적이 없어 World Revision이 `None`이었다면 비교하지 않습니다.
- 복원에 성공해도 **진행 중이던 비동기 요청은 다시 발급해야 합니다**(`Planning`이었다면 `NeedsPlan`으로 돌아갑니다). 이미 발급한 Planner 결과는 Request ID로 차단합니다.
- 복원 성공 시 Goal Generation과 Path Generation를 새로 발급해 복원 전 Guidance의 늦은 완료가 현재 경로를 바꾸지 못하게 합니다. `GoalGeneration`은 복원에서도 줄어들지 않습니다.
- 실제 World Snapshot과 History의 일관성은 Host가 보장합니다. Motor의 교정이 Navigation을 자동으로 무효화하지 않으므로, 교정이나 World Shift를 감지하면 Host가 `Invalidate`를 호출합니다.

## 소유권과 수명

Controller는 Actor 하나가 소유하며 `MotionActorKey`로 만듭니다. 요청 ID, 경로 ID, Goal Generation은 단조 증가하는 수명 토큰이고 Restore에서도 되돌리지 않습니다.

## 책임 범위

### 담당하는 것

- 목표와 경로의 수명, 오래된 결과와 완료 통지의 거절
- 다음 Waypoint 안내와 진행
- Goal과 Path의 Owned Snapshot

### 담당하지 않는 것

- 경로 계산(Planner, NavMesh), 비동기 Job
- Steering, 도착 판정, 장애물 회피, 속도와 방향 계산
- Body 변경과 Motion 입력 생성 — `TryGetGuidance` 결과를 받은 사용처가 `MotionIntent`를 만듭니다
- 네트워크 Clock과 전역 Command Bus

## 제약과 주의사항

- 한 Actor의 Navigation은 한 스레드에서 순차 호출합니다.
- Guidance의 완료 통지는 반드시 그 Guidance 값으로 `TryAdvanceWaypoint`를 호출하세요.
- Snapshot은 Planner 작업과 World, Ability 상태를 포함하지 않습니다.

## 관련 문서

- [Locomotion 전체 구조](overview.md) — Restore와 수명 값 정책
- [Motion](motion.md) — Guidance를 `MotionIntent`로 바꿔 넣는 쪽
- [Movement](movement.md)
