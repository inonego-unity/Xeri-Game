# Use와 Reaction 연결하기

Xeri Use 시스템은 “무엇이 사용 가능한 후보인지”를 탐색하지 않습니다. 프로젝트 Scanner가 후보를 `UseController`에 공급하고, 입력 Adapter가 `TryUse()`를 호출하며, `UseOffer`의 Signal은 `ReactionBinding`을 통해 실제 Action으로 연결합니다.

## 목적

후보 탐색, 후보 선택, 입력, Prompt 표시, 실제 도메인 효과를 서로 분리해 Trigger/Raycast/Input 방식이 달라도 같은 Use/Reaction 계약을 재사용합니다.

## 전체 구조

```text
Scanner
  ↓ AddOffer / RemoveOffer
UseController
  ↓ CurrentOffer
Input Adapter ── TryUse(instigator)
  ↓
UseOffer.OnSignal
  ↓
ReactionBinding
  ↓ optional Guard
IActionTarget.Execute
```

이 구조를 사용하면 Trigger, Raycast, 화면 중앙 탐색 등 후보 탐색 방식을 Xeri Game와 분리할 수 있습니다.

## 1. UseController와 Offer 준비

상호작용을 선택하는 Host에 `UseController`를 두고, 실제 상호작용 대상에는 `UseOffer`를 배치합니다.

`UseOffer`에는 기본 Prompt, Priority, Anchor와 사용 가능 상태가 있습니다. 여러 Offer가 동시에 후보가 되면 높은 Priority가 우선하고, 같은 Priority에서는 선택 정책을 사용할 수 있습니다.

## 2. 프로젝트 Scanner에서 후보 공급

Scanner는 자신의 탐색 방식으로 `UseOffer`를 찾은 뒤 Controller에 추가하거나 제거합니다.

```csharp
private void OnOfferEntered(UseOffer offer)
{
    useController.AddOffer(offer);
}

private void OnOfferExited(UseOffer offer)
{
    useController.RemoveOffer(offer);
}
```

Scanner가 비활성화되거나 탐색 기준이 사라지면 자신이 추가한 후보를 모두 제거해야 합니다.
## 3. 입력에서 TryUse 호출

입력 시스템 자체는 프로젝트가 소유합니다. 입력이 수행됐을 때 현재 사용 주체를 `instigator`로 전달합니다.

```csharp
public void OnInteract(GameObject instigator)
{
    useController.TryUse(instigator);
}
```

`instigator`는 고정된 UI Host보다 실제 행동 주체를 전달하는 편이 좋습니다. Reaction Action이 누가 사용했는지 판단할 수 있기 때문입니다.

## 4. Reaction Action 작성

`UseOffer`는 실제 문 열기, 대화 시작 같은 도메인 로직을 직접 알지 않습니다. `IActionTarget` 구현이 효과를 담당합니다.

```csharp
using System;
using inonego.Xeri.Game;

[Serializable]
public sealed class SetFlagAction : IActionTarget
{
    public void Execute(ReactionContext context)
    {
        ProjectFlags.Set("sample.used", true);
    }
}
```

`ReactionBinding`에 `UseOffer`를 Source로, 필요한 경우 `ICond<ReactionContext>` Guard를, Action Target을 연결합니다.

## 5. Prompt는 이벤트를 구독한다

Prompt Presenter는 `UseController.OnPromptShow`와 `OnPromptHide`를 구독해서 UI만 갱신합니다. 후보 탐색이나 실제 사용 실행을 Presenter가 소유하지 않습니다.
## 수명 정리

- Scanner가 제거되면 자신이 등록한 Offer를 Controller에서 제거합니다.
- 입력 Adapter가 비활성화되면 입력 이벤트 구독만 해제합니다.
- `ReactionBinding`은 OnEnable/OnDisable에서 Source Signal을 Bind/Unbind합니다.
- Prompt Presenter는 Controller 이벤트 구독을 자신의 표시 수명과 대칭으로 관리합니다.

## 관련 문서

- [Use System](../../modules/interaction/use.md)
- [Reaction](../../modules/interaction/reaction.md)
- [Xeri 통합 패턴](https://inonego-unity.github.io/Xeri/docs/concepts/integration-patterns.html)