# State Machine 구성하기

이 가이드는 게임 Owner에 `StateMachine<TOwner>`를 붙이고 Unity 갱신 루프에 연결하는 방법을 설명합니다. **전이 조건은 게임이 판단하고, 머신은 상태 생명주기를 실행**합니다.

## 1. Owner 정의

여러 상태가 읽거나 변경해야 하는 Runtime 객체를 Owner로 정합니다. 머신은 Owner를 생성하지 않으며 null Owner를 허용하지 않습니다.

```csharp
public sealed class CharacterRuntime
{
    public float Speed { get; set; }
}
```

## 2. 상태 구현

Owner를 사용하는 행동 상태는 `StateBase<TOwner>`를 상속합니다. `AddState`에 등록할 때 Owner가 자동으로 설정됩니다.

```csharp
public sealed class IdleState : StateBase<CharacterRuntime>
{
    public override void OnEnter()
    {
        Owner.Speed = 0f;
    }
}

public sealed class MoveState : StateBase<CharacterRuntime>
{
    public override void OnEnter()
    {
        Owner.Speed = 4f;
    }
}
```

`OnExit()`는 상태가 소유하는 임시 자원을 해제할 때, `OnUpdate()`는 활성 상태의 프레임 행동을 처리할 때 구현합니다. Fixed/Late 갱신이 필요하면 `IFixedUpdatable` / `ILateUpdatable`을 별도로 구현합니다.

## 3. 상태 등록과 초기 전이

```csharp
var owner = new CharacterRuntime();
var machine = new StateMachine<CharacterRuntime>(owner);

machine.AddState(new IdleState());
machine.AddState(new MoveState());
machine.MoveTo<IdleState>();
```

`AddState<T>()`는 타입별 슬롯에 인스턴스를 등록하며 같은 슬롯의 재등록은 교체입니다. 다른 머신에서 상태 인스턴스를 공유하지 않습니다. 상태가 등록되지 않은 `MoveTo<T>()`는 무동작이므로 필요한 상태는 초기화할 때 모두 등록합니다.

## 4. Unity 업데이트 연결

`StateMachine`은 `MonoBehaviour`가 아닙니다. 아래 예시의 `machine`은 게임 Host가 위 단계에서 생성해 보관한 필드라고 가정합니다.

```csharp
private void Update()
{
    machine.Tick();
}

private void FixedUpdate()
{
    machine.FixedTick();
}

private void LateUpdate()
{
    machine.LateTick();
}
```

`Tick()`은 현재 상태의 `OnUpdate`를 호출합니다. `FixedTick()`과 `LateTick()`은 해당 선택적 인터페이스를 구현한 상태에만 전달됩니다.

## 5. 전이 조건 연결

```csharp
if (hasMoveInput)
{
    machine.MoveTo<MoveState>();
}
else
{
    machine.MoveTo<IdleState>();
}
```

`hasMoveInput`은 게임 입력·AI·규칙이 계산한 값입니다. State Machine은 입력이나 자동 조건 그래프를 제공하지 않습니다. 같은 인스턴스로의 전이는 아무 일도 하지 않으며 `MoveTo((IState)null)`은 활성 상태를 종료합니다.

## 6. 종료와 오류 처리

- `OnExit`가 실패하면 `Current`는 null로 남습니다.
- `OnEnter`가 실패하면 해당 상태의 `OnExit`를 호출해 부분 생성 자원을 정리하고 `Current`를 비웁니다.
- 전이가 확정된 뒤 `OnStateChanged` 구독자가 실패해도 완료된 전이는 되돌리지 않습니다.
- `OnEnter`, `OnExit`, `OnStateChanged`에서 `MoveTo` 또는 `AddState`를 다시 호출하지 않습니다. 다음 전이는 Host의 후속 단계에서 요청합니다.

행동 종료와 World 상태 복원은 구분합니다. Scene 재방문 시 행동을 다시 재생하지 않고 지속 결과를 표현하려면 [WorldObject와 지속 상태](../../foundation/world-object.md)의 Store·Binding을 사용합니다.

관련 문서: [State Machine 계약](../../foundation/state-machine.md) · [Foundation](../../foundation/index.md)
