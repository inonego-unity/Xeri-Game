# Xeri Game Navigation — P4 Core 계약

> Namespace `inonego.Xeri.Game.Navigation`, Assembly `inonego.Xeri.Game`. **목표·경로 수명과 Guidance가 구현됨.** 실제 NavMesh/AI Planner, 이동 정책, Body 제어, 외부 Provider/Replay Adapter는 포함하지 않는다.

## 책임과 경계

- `NavigationController<TPoint>`: Actor별 `SetGoal`, `Cancel`, `Invalidate`, 경로 요청/승인/실패, Waypoint 진행, Owned Snapshot 복원.
- `NavigationPlanRequest<TPoint>`: Actor, Goal Revision, Controller 내 재사용되지 않는 Request ID, 외부 World Revision, Start/Goal.
- `INavigationPlanner<TPoint>`: 사용처 Planner가 `Request(in request)`를 소비한다. Unity NavMesh 등의 특정 구현을 강제하지 않으며 비동기 결과는 사용처가 Controller에 다시 전달한다.
- `NavigationGuidance<TPoint>`: 다음 Waypoint와 **Actor·Goal Revision·PathID·Waypoint Index**를 포함한다. 같은 목표의 재계획이나 Restore 뒤 이전 Guidance의 완료 통지를 구별한다. 방향·속도·가속도/장애물 회피 계산이나 Motor 적용은 게임 Host/Producer의 책임.
- `NavigationSnapshot<TPoint>`: 독립 복사한 Goal·Path·진행 상태. **Physics/Planner 작업/World History는 포함되지 않는다.**

## 기본 호출 순서

```csharp
var navigation = new NavigationController<Vector3>(actor);
navigation.SetGoal(destination);

// Host가 월드 버전과 현재 시작 위치를 제공한다.
var request = navigation.BeginPlan(currentPosition, worldRevision);
planner.Request(request);  // INavigationPlanner<Vector3>를 구현한 소비자

// 외부 Planner가 경로를 계산한 뒤, 비동기 콜백에서도 같은 request를 돌려준다.
var status = navigation.TryAcceptPath(request, path);

if (navigation.TryGetGuidance(out var guidance))
{
    // 이동 정책은 Host가 결정한다. 공개 Motion 요청으로 Motor에 전달한다.
    var nextPoint = guidance.Target;
    // Host: 이 Guidance의 도착이 확인되면 navigation.TryAdvanceWaypoint(guidance);
}

// World Shift / Motor Correction / 장애물 재계획 시점은 Host가 판단한다.
navigation.Invalidate(); // 목표는 유지하고 이전 경로·지연 응답 폐기
```

`NavigationController`는 Actor별 순차 호출을 전제로 하며 **여러 스레드의 동시 접근을 동기화하지 않는다**. 비동기 Planner의 Worker 결과는 외부 Host가 해당 Actor의 실행 스레드로 전달한 뒤 `TryAcceptPath`를 호출해야 한다.

`TryAcceptPath`는 현재 **Actor·Goal Revision·Request ID·World Revision**의 요청만 승인한다. 경로는 `ReadOnlySpan<TPoint>`로 받아 수락 도중 외부 코드를 호출하지 않는다. 배열을 그대로 넘길 수 있고 `null` 배열은 빈 경로로 본다. 오래된 결과는 `Stale`, 빈 경로는 `InvalidPath`다. 빈 경로 처리 이후 같은 요청에 재제출할 수 있으며, `TryRejectPath`는 `Failed`로 표시한다. 목표 변경·Cancel·Invalidate는 이전 계획을 무효화한다. **지연 완료 통지**에는 `TryAdvanceWaypoint(guidance)`를 사용한다. 현재 Actor·Goal Revision·**경로 ID**·Waypoint Index가 모두 같을 때만 진행해 이전 경로의 늦은 완료 및 중복 완료를 거절한다. 기존 인자 없는 `AdvanceWaypoint()`는 **즉시 동기 호출 전용**으로 남아 있으므로 비동기 완료 콜백에 사용하지 않는다. Goal은 Arrived 이후에도 조회 가능하다.

## Owned State와 Correction

```csharp
var snapshot = navigation.Capture();
var result = navigation.Restore(snapshot, currentWorldRevision);
```

`Restore`는 Actor가 다르면 `ActorMismatch`, 캡처 시 World Revision과 현재 World Revision이 다르면 `WorldMismatch`를 반환하고 상태를 바꾸지 않는다. 복원에 성공해도 **진행 중이던 비동기 요청은 다시 발급해야 한다**(`Planning` → `NeedsPlan`). 이미 발급한 Planner 결과는 Request ID로 차단한다. **복원 성공 시 Goal Revision과 경로 ID도 새로 부여**해 복원 전 Guidance의 지연 도착 완료가 현재 경로를 변경하지 않도록 한다. `GoalRevision`은 Restore에서도 줄어들지 않으므로 같은 값이 다른 목표를 가리키지 않는다(`NavigationSnapshot.GoalRevision`은 캡처 당시 기록이며 복원 값이 아니다). 실제 World Snapshot과 History의 일관성은 외부 Host가 보장해야 한다.

Navigation은 Motion 입력을 자동으로 만들거나 Body를 직접 변경하지 않는다. `TryGetGuidance` 결과를 소비한 사용처 Producer가 기본 의도(`MotionIntent`)를 만들고, Motion Model의 합성 결과만 Motor에 적용한다. Navigation 자체에는 Network Clock·전역 Command Bus가 없다.

## 검증과 한계

[Navigation 계약 테스트](../../../../Tests/EDIT/Modules/Locomotion/Navigation/TEST_NavigationController.cs)는 Goal/Cancel, 동기·비동기 Planner 지연 결과, 재계획, Snapshot·World/Actor 격리, 복원 후 Goal Revision 재사용 금지, Guidance 진행 및 기존 Motion→Motor API 연결을 다룬다. 테스트 수와 검증 수준은 [구현 플랜](../../../../../Docs/systems/movement/implementation-plan.md), 할당 측정값은 [성능 수용 계약 §5](../../../../../Docs/systems/movement/network-performance-readiness.md)에 있다.

아직 검증하지 않은 범위는 실제 NavMesh Planner·Game Host의 World Shift/Correction 타이밍 통합, Player/IL2CPP 성능, 외부 Adapter 수용 시험이다.
