# WorldObject 지속 상태 구성하기

이 가이드는 Scene의 문 객체를 예시로 **World 데이터와 Scene 표현의 독립 수명**을 구성합니다. `WorldObjectStateStore`는 현재 데이터 참조를 유지하고, `WorldObjectSceneBinding`은 로드된 Scene 객체에 상태를 연결합니다.

## 1. 지속 데이터 스키마 정의

문이 열렸다는 **결과**만 보존합니다. GameObject·Animator·보상 지급 여부 판단 같은 Scene 동작을 상태 타입에 넣지 않습니다.

```csharp
using System;
using inonego.Xeri.Game;

[Serializable]
public sealed class DoorState : WorldObjectState
{
    public bool IsOpen;

    public DoorState(string id) : base(id)
    {
    }
}
```

`ID`는 같은 Store 안에서 유일하고 안정적인 문자열이어야 합니다. 서로 다른 Scene에서 같은 논리 문을 표현하려면 같은 ID를 부여합니다. 한 Store에서 같은 ID를 가진 두 표현 객체를 **동시에 연결할 수는 없습니다.**

## 2. Scene 표현 구현

`IWorldObject`의 필수 책임은 ID 제공, State 참조 연결, 연결 해제입니다. 여기서는 외부에서 확정한 현재 상태를 적용하는 선택적 `IWorldObjectCurrentStateReceiver`도 함께 구현합니다.

```csharp
using System;
using inonego.Xeri.Game;
using UnityEngine;

public sealed class DoorView : MonoBehaviour, IWorldObject, IWorldObjectCurrentStateReceiver
{
    [SerializeField] private string objectID;
    [SerializeField] private GameObject closedPanel;

    private DoorState current;

    public string ID => objectID;

    public void BindState(WorldObjectState state)
    {
        ValidateCurrentState(state);
        current = (DoorState)state;
        RenderCurrent();
    }

    public void UnbindState()
    {
        // 부분 Bind 실패 뒤에도 호출될 수 있으므로 반복 호출이 안전해야 한다.
        current = null;
    }

    public void ValidateCurrentState(WorldObjectState next)
    {
        if (next is not DoorState || !StringComparer.Ordinal.Equals(next.ID, ID))
        {
            throw new ArgumentException("문 ID 또는 상태 타입이 일치하지 않습니다.", nameof(next));
        }
    }

    public void ApplyCurrentState(WorldObjectState next)
    {
        // WorldObjectSceneBinding이 검증한 현재 상태만 적용한다.
        current = (DoorState)next;
        RenderCurrent();
    }

    public void RenderCurrent()
    {
        if (closedPanel != null && current != null)
        {
            closedPanel.SetActive(!current.IsOpen);
        }
    }
}
```

`BindState`, `UnbindState`, `ApplyCurrentState`에서는 열기 보상 지급·퀘스트 진행·사운드 재생 같은 **게임플레이 효과를 재발생시키지 않습니다**. 표현 객체를 상태 값에 맞춰 구성하는 데만 사용합니다.

## 3. Store와 Scene Binding 구성

게임 Host는 Scene 밖의 수명에 Store를 두고, Scene마다 Binding을 생성하거나 연결이 끝난 Binding을 재사용합니다.

```csharp
var store = new WorldObjectStateStore();
var sceneBinding = new WorldObjectSceneBinding(store);

// doorViews는 로드된 Scene에서 수집한 IEnumerable<IWorldObject>.
// 이 예시에서는 DoorView만 포함한다.
sceneBinding.Bind(doorViews, obj => new DoorState(obj.ID));
```

- Store에 ID가 있으면 **기존 DoorState 인스턴스**를 연결합니다.
- Store에 ID가 없으면 Factory를 호출하고, 연결에 성공해야 새 State를 Store에 추가합니다.
- Factory는 생성만 담당합니다. 외부 데이터를 수정하거나 VFX를 실행하지 않습니다.
- ID가 누락되거나 중복되면 오류를 반환합니다. **빈 컬렉션을 Bind하면 `Unbound` 상태 그대로**입니다.

## 4. 게임 로직에서 값 변경

상태 변경 권한은 게임 Domain이 소유합니다. `Store`가 반환하는 것은 복제본이 아닌 Live State입니다.

```csharp
if (store.TryGetState("door_01", out var value) && value is DoorState door)
{
    door.IsOpen = true;

    // 현재 Scene 표현은 게임의 해당 객체에 명시적으로 갱신을 요청한다.
    // 예: 연결된 DoorView에 RenderCurrent() 호출
}
```

State 필드 변경은 자동으로 모든 View에 통지되지 않습니다. Host 또는 게임 로직이 현재 표현을 갱신할 시점을 결정합니다. FSM을 쓰는 경우에는 FSM이 문의 열기 행동을 수행하고 Domain이 지속 결과를 변경할 수 있지만, 두 시스템은 자동 연결되지 않습니다.

## 5. Scene 종료·재진입

```csharp
// Scene의 GameObject를 파괴하기 전에:
sceneBinding.Unbind();

// 같은 Store를 유지하고 다른 Scene의 DoorView를 준비한 뒤:
sceneBinding.Bind(nextDoorViews, obj => new DoorState(obj.ID));
```

이전 Scene에서 바뀐 DoorState 참조를 그대로 새 Scene에 전달합니다. 해제 시 Store 데이터는 제거되지 않습니다. 다른 Scene을 동시에 운영할 때에는 Scene마다 다른 Binding을 만들되, **같은 Store·ID를 중복 예약하지 않습니다**.

`UnbindState()`가 실패한 경우 Binding은 `CleanupPending`이 되고 예약한 ID를 유지하므로 `Unbind()`로 정리를 재시도합니다. 이 단계가 끝나기 전에는 새 Scene에 같은 ID를 연결할 수 없습니다.

## 6. 외부에서 확정된 현재 상태 적용

이미 승인·확정된 상태를 외부에서 받았다면 게임 Host가 권한과 대상 범위를 검증한 후 다음 API를 사용할 수 있습니다.

```csharp
sceneBinding.ApplyCurrentState(new DoorState("door_01") { IsOpen = false });
```

`ValidateCurrentState`가 실패하면 기존 연결을 유지합니다. 적용 중 예외가 나면 일부 View 변경이 남을 수 있으므로 Binding은 `CleanupPending`이 되고, **Store에는 실패한 새 State를 확정하지 않습니다**. Host는 `Unbind()` 후 새 Scene 객체로 재구성하는 정책을 준비해야 합니다.

`ApplyCurrentState`는 **확정된 현재 값의 적용**이지 Save 직렬화, 서버 Authority, Historical Restore, Prediction, Replay 기능이 아닙니다.

## 관련 문서

- [WorldObject 계약과 실패 처리](../../foundation/world-object.md)
- [State Machine](../../foundation/state-machine.md)
- [Entity Actor와 World Scene 조립](compose-entity-actor-runtime.md)
- [Foundation](../../foundation/index.md)
