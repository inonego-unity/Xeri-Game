# State Machine

Xeri State Machine은 Owner를 공유하는 상태 인스턴스를 등록하고 현재 상태의 진입·갱신·종료를 관리하는 범용 FSM입니다.

## 왜 필요한가

상태마다 `Update()` 분기와 진입/종료 처리를 직접 작성하면 상태 수가 늘수록 이전 상태 정리와 현재 상태 실행이 섞이기 쉽습니다. State Machine은 현재 상태 하나와 `OnEnter/OnExit/OnUpdate` 호출 순서를 고정하되, 전이 조건이나 그래프 정책은 프로젝트에 남깁니다.

## 언제 사용하는가

- 캐릭터, 도구, 작업 흐름처럼 상호 배타적인 현재 상태가 하나일 때
- 상태들이 같은 Owner를 공유해야 할 때
- Update/Fixed/Late update 경계를 명시적으로 호출하고 싶을 때

복잡한 조건 그래프, 계층 상태, 병렬 상태가 핵심이라면 더 전문적인 FSM/Behavior 시스템이 적합할 수 있습니다.

## 기본 사용

```csharp
var machine = new StateMachine<Character>(character);

machine.AddState(new IdleState());
machine.AddState(new MoveState());
machine.MoveTo<IdleState>();

// 호출자가 원하는 update 경계에 연결한다.
machine.Tick();
machine.FixedTick();
```

`StateBase<TOwner>` 파생 상태는 `AddState()` 시 Owner를 자동으로 전달받습니다.

## 핵심 모델

```text
Owner
  ↓
StateMachine<TOwner>
  ├─ 등록된 State 집합
  └─ Current
       ├─ OnEnter
       ├─ OnUpdate
       ├─ OnFixedUpdate (선택)
       ├─ OnLateUpdate  (선택)
       └─ OnExit
```

`AddState<T>()`로 등록한 상태가 `StateBase<TOwner>`이면 같은 Owner가 자동 주입됩니다.

## 상태 전이

`MoveTo<T>()`는 등록된 타입의 상태로 이동하고, `MoveTo(IState)`는 직접 인스턴스를 지정합니다. 같은 인스턴스로의 전이는 무시됩니다.

전이 순서는 다음과 같습니다.

```text
previous.OnExit()
→ Current 교체
→ next.OnEnter()
→ OnStateChanged(previous, next)
```

현재 상태를 `null`로 이동하는 것도 허용됩니다.
## 갱신 경계

- `Tick()`은 `Current.OnUpdate()`를 호출합니다.
- `FixedTick()`은 현재 상태가 `IFixedUpdatable`일 때만 호출합니다.
- `LateTick()`은 현재 상태가 `ILateUpdatable`일 때만 호출합니다.

State Machine 자체는 Unity `Update`에 연결되지 않습니다. 호출자가 자신의 Runtime tick 경계에서 필요한 메서드를 호출합니다.

## 책임 범위

### 담당하는 것

- 상태 인스턴스 등록과 타입 기반 조회
- 현재 상태 전이
- Owner 주입
- Update/Fixed/Late update 전달

### 담당하지 않는 것

- 전이 조건 자동 평가
- 상태 그래프나 transition table
- 상태 인스턴스 자동 생성
- Unity lifecycle 자동 연결

## 제약과 주의사항

- 동일 타입을 다시 등록하면 기존 상태가 교체됩니다.
- 등록되지 않은 타입으로 `MoveTo<T>()`를 호출하면 아무 동작도 하지 않습니다.
- 상태 훅에서 재진입 전이를 복잡하게 중첩하면 호출 순서가 사용처 정책에 의존하므로 피합니다.

## 관련 문서

- [State Machine 구성하기](../guides/foundation/create-state-machine.md)
- [Xeri Game](../modules/index.md)
- [Entity와 Spawn 수명](entity-and-spawn.md)
