# State Machine 구성하기

이 가이드는 프로젝트 Owner에 Xeri `StateMachine<TOwner>`를 연결하고 Update/FixedUpdate 경계에서 상태를 실행하는 기본 구성을 설명합니다.

## 목적

상태의 진입·종료·갱신 순서를 Xeri에 맡기고, 상태 전이 조건과 실제 도메인 동작은 프로젝트 코드가 소유하게 합니다.

## 1. Owner 준비

상태들이 공유할 Runtime 객체를 준비합니다.

```csharp
public sealed class CharacterRuntime
{
    public float Speed { get; set; }
}
```

## 2. State 구현

Owner에 접근하려면 `StateBase<TOwner>`를 상속합니다.

```csharp
public sealed class IdleState : StateBase<CharacterRuntime>
{
    public override void OnEnter()
    {
        Owner.Speed = 0f;
    }
}
```
```csharp
public sealed class MoveState : StateBase<CharacterRuntime>
{
    public override void OnEnter()
    {
        Owner.Speed = 4f;
    }
}
```

## 3. Machine 조립

```csharp
var owner = new CharacterRuntime();
var machine = new StateMachine<CharacterRuntime>(owner);

machine.AddState(new IdleState());
machine.AddState(new MoveState());
machine.MoveTo<IdleState>();
```

등록된 `StateBase<TOwner>`에는 같은 Owner가 자동으로 주입됩니다.

## 4. Tick 연결

`StateMachine`은 Unity lifecycle을 자동으로 소유하지 않습니다. Host가 필요한 update 경계를 연결합니다.

```csharp
private void Update()
{
    machine.Tick();
}

private void FixedUpdate()
{
    machine.FixedTick();
}
```
현재 상태가 `IFixedUpdatable` 또는 `ILateUpdatable`을 구현할 때만 해당 Tick이 전달됩니다.

## 5. 전이 조건은 프로젝트가 소유

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

Xeri는 transition table이나 조건 evaluator를 강제하지 않습니다. 입력, AI, 애니메이션 이벤트 등 프로젝트에 맞는 조건을 사용합니다.

## 주의사항

- 같은 타입을 다시 `AddState()`하면 기존 등록을 교체합니다.
- 미등록 타입으로 `MoveTo<T>()`를 호출하면 전이하지 않습니다.
- 상태 callback 안에서 복잡한 재진입 전이를 중첩하기보다 상위 flow에서 전이 결정을 모으는 편이 추적하기 쉽습니다.

## 관련 문서

- [State Machine](../../foundation/state-machine.md)
- [Game Controller](../../modules/character/controller.md)