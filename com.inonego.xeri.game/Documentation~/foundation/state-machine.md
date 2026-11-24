# State Machine

`StateMachine<TOwner>`는 하나의 Owner를 공유하는 행동 상태들의 **현재 활성 상태와 전이·갱신 순서**를 관리하는 유한 상태 머신(FSM)입니다. 게임 규칙이 전이 시점을 결정하고, State Machine은 선택된 상태의 생명주기 메서드를 호출합니다.

## 구성 요소

| 요소 | 역할 |
|---|---|
| `StateMachine<TOwner>` | Owner, 상태 등록표, `Current`, 전이 순서 및 완료 이벤트 관리 |
| `IState` | `OnEnter`, `OnExit`, `OnUpdate` 행동 계약 |
| `StateBase<TOwner>` | `IState` 기본 구현, Owner 참조와 등록 머신 소유권 관리 |
| `IFixedUpdatable` | 선택적인 `OnFixedUpdate` 호출 계약 |
| `ILateUpdatable` | 선택적인 `OnLateUpdate` 호출 계약 |

이 State는 **현재 실행 중인 행동**입니다. [WorldObjectState](world-object.md)는 Scene 전환 뒤에도 남는 **세계 데이터**로, FSM의 `Current`와 별개입니다.

## StateMachine: Owner와 상태 등록

`new StateMachine<TOwner>(owner)`는 null이 아닌 Owner 인스턴스가 필요합니다. 머신은 Owner를 생성하거나 파괴하지 않으며 같은 인스턴스를 상태에 전달합니다.

| API | 동작 |
|---|---|
| `Owner` | 생성 시 전달한 Owner 조회 |
| `Current` | 현재 활성 `IState`; 활성 상태가 없으면 null |
| `AddState<T>(state)` | `typeof(T)`를 키로 상태 등록; 같은 타입 키는 교체 |
| `GetState<T>()` | 현재 타입 키에 등록된 상태 조회; 없으면 null |
| `MoveTo<T>()` | 등록된 타입으로 전이; 미등록이면 무동작 |
| `MoveTo(IState next)` | 지정 인스턴스로 전이; null이면 현재 상태 종료 |
| `OnStateChanged(previous, next)` | 성공적으로 끝난 전이 후 통지 |
| `ReplaceCurrent(state)` | 파생 머신 전용(`protected`). 전이 생명주기 없이 `Current`만 교체 |
| `Tick()` / `FixedTick()` / `LateTick()` | 현재 상태의 해당 갱신 메서드 실행 |

등록 타입 키는 실제 런타임 타입과 다를 수 있으므로, 조회와 전이에 사용하는 제네릭 타입을 일관되게 선택합니다. 같은 상태 인스턴스를 **서로 다른 타입 키에 중복 등록할 수 없습니다**. 등록 교체는 현재 활성 상태를 자동 종료하지 않습니다.

## StateBase: Owner 및 등록 소유권

`StateBase<TOwner>`는 `IState`를 구현하며 `OnEnter`, `OnExit`, `OnUpdate`의 기본 구현을 제공합니다. `AddState`가 Owner 참조를 주입합니다.

- 하나의 `StateBase<TOwner>` 인스턴스는 하나의 머신에만 등록할 수 있습니다. 다른 머신에서 사용하려면 새 인스턴스를 생성합니다.
- Owner 타입이 다른 `StateBase`는 `IState` 타입으로 전달해도 등록·전이할 수 없습니다.
- `StateBase`로 전이하려면 해당 인스턴스가 **현재 머신의 등록표에 남아 있어야** 합니다. 타입 키가 교체되어 등록에서 제외된 인스턴스로는 새 전이를 시작할 수 없습니다.
- `IState`를 직접 구현한 상태는 `StateBase`의 머신 소유권 제약을 갖지 않으며 `MoveTo(IState)`에 등록 없이 전달할 수 있습니다.

교체 전 상태가 이미 `Current`라면 현재 상태를 그대로 두고, 다음 명시적 전이에서 새 등록을 사용합니다. 같은 인스턴스로의 `MoveTo`는 무동작입니다.

## 전이 생명주기

`MoveTo`가 실제로 다른 상태로 전이할 때의 순서는 다음과 같습니다.

```text
Current = null
→ previous.OnExit()
→ Current = next
→ next.OnEnter()
→ OnStateChanged(previous, next)
```

`next == null`이면 이전 상태를 종료하고 `Current`를 null로 둡니다. `OnStateChanged`는 전이의 완료를 알리는 이벤트이지 조건 판단이나 전이 요청 이벤트가 아닙니다.

### 전이 중 제약

`OnExit`, `OnEnter`, `OnStateChanged` 실행 중에는 재진입 `MoveTo`와 `AddState`를 거절합니다. 등록표와 현재 전이 대상이 전이 도중 바뀌지 않도록 하는 계약입니다. 새 상태 등록이나 후속 전이는 콜백 밖의 Host/Flow에서 수행합니다.

### 실패 처리

| 실패 지점 | `Current` 및 후속 처리 |
|---|---|
| `previous.OnExit()` | 이미 null로 비운 `Current`를 유지하고 예외 전달 |
| `next.OnEnter()` | `Current`를 null로 비우고 `next.OnExit()`로 부분 정리 시도 |
| 진입 실패와 부분 정리 동시 실패 | 두 오류를 `AggregateException`으로 전달 |
| `OnStateChanged` 구독자 | 이미 성공한 전이는 유지; 다른 구독자도 호출 시도 후 오류 전달 |

완료 이벤트 구독자 한 명이 실패해도 나머지 구독자는 호출합니다. 예외가 하나면 단일 예외, 여러 개면 `AggregateException`을 전달합니다. 실패를 자동으로 재시도하거나 이전 상태로 롤백하지는 않습니다.

## 외부 확정 상태 복원

저장된 값이나 권한 주체가 확정한 값에 맞춰 머신의 활성 상태를 정해야 하는 소비처는 `ReplaceCurrent(state)`를 사용합니다. 이 메서드는 `protected`이므로 파생 머신이 자신의 복원 계약으로 감쌉니다.

```csharp
public sealed class RestorableMachine<TOwner> : StateMachine<TOwner>
where TOwner : class
{
    public RestorableMachine(TOwner owner) : base(owner)
    {
        // NONE
    }

    public void Restore(IState state) => ReplaceCurrent(state);
}
```

`ReplaceCurrent`는 `OnExit`·`OnEnter`·`OnStateChanged`를 실행하지 않으므로 과거 전이의 효과가 반복되지 않습니다. 전이 중에는 거절하며, `StateBase`는 `MoveTo`와 같이 현재 머신의 등록표에 남아 있어야 합니다. `null`을 전달하면 종료 처리 없이 활성 상태를 비웁니다.

어떤 상태가 활성이었는지의 식별 방식, 상태 객체 내부 데이터의 저장 형식, 복원 직후 필요한 자원 연결은 소비처가 정합니다. 머신은 상태 데이터를 직렬화하지 않습니다.

## Tick: 갱신 실행 책임

| 호출 | 대상 |
|---|---|
| `Tick()` | `Current?.OnUpdate()` |
| `FixedTick()` | `Current`가 `IFixedUpdatable`일 때 `OnFixedUpdate()` |
| `LateTick()` | `Current`가 `ILateUpdatable`일 때 `OnLateUpdate()` |

머신 자체는 Unity `MonoBehaviour`가 아니며 Update/FixedUpdate/LateUpdate에 자동으로 연결되지 않습니다. Host가 필요한 프레임 경계에서 호출합니다.

## 사용 예시

```csharp
var machine = new StateMachine<CharacterRuntime>(character);
machine.AddState(new IdleState());
machine.AddState(new MoveState());

machine.MoveTo<IdleState>();
// 입력이나 게임 규칙에 의해:
machine.MoveTo<MoveState>();

// Host의 Update/FixedUpdate에서 각각:
machine.Tick();
machine.FixedTick();
```

`CharacterRuntime`, `IdleState`, `MoveState`는 게임이 구현하는 Owner와 `StateBase<CharacterRuntime>` 파생 상태입니다. 전체 선언과 Unity 루프 연결은 [State Machine 구성 가이드](../guides/foundation/create-state-machine.md)를 참고합니다.

## 시스템 경계

State Machine은 전이 조건 자동 평가, 그래프/계층 상태, 상태 인스턴스 자동 생성, Unity 이벤트 자동 연결, Save/Load, Replay 또는 Prediction을 제공하지 않습니다. 복원이 필요한 소비처는 위의 `ReplaceCurrent` 확장 지점으로 직접 구성합니다. 상태가 World 데이터를 변경해야 한다면 **게임 로직이** 별도 Store·Binding 참조를 통해 수행합니다.

관련 문서: [Foundation](index.md) · [WorldObject와 지속 상태](world-object.md) · [State Machine 구성 가이드](../guides/foundation/create-state-machine.md)
