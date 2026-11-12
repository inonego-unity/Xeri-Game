# Use System

Xeri Use System은 외부 Scanner가 공급한 상호작용 후보 중 현재 사용할 `UseOffer`를 선택하고, 입력을 Signal로 전달하는 Runtime 연결 계층입니다.

**구분:** 이 Use System은 Scanner가 공급한 **상호작용 후보**를 선택하는 시스템입니다. 스킬의 상태 기반 사용 선택·Requirement·Cost 승인과는 별개이며, 그 구조는 [Gameplay Framework](../action/overview.md)를 참고합니다.

## 왜 필요한가

상호작용 시스템이 Physics 탐색, 입력 구독, 후보 우선순위, Prompt 표시와 실제 효과 실행을 한 Component에 모으면 프로젝트마다 재사용하기 어렵고 테스트 경계도 흐려집니다. Xeri Use는 후보 선택과 사용 전달만 맡고 탐색·입력·표시·도메인 효과를 분리합니다.

## 언제 사용하는가

- Trigger, Raycast 등 서로 다른 Scanner가 같은 상호작용 선택 규칙을 공유할 때
- 후보 우선순위와 Prompt 대상을 한곳에서 관리하고 싶을 때
- 플레이어 입력뿐 아니라 AI나 테스트 코드도 같은 `TryUse` 경계를 사용해야 할 때

후보가 항상 하나이고 별도 탐색/표시 분리가 필요 없다면 직접 호출이 더 단순할 수 있습니다.

## 기본 사용

프로젝트 Scanner가 후보를 공급하고 입력 Adapter가 실제 사용 주체를 넘깁니다.

```csharp
useController.AddOffer(offer);

// 입력 수행 시
useController.TryUse(instigator);

// Scanner 범위를 벗어나면
useController.RemoveOffer(offer);
```

실제 효과는 `UseOffer.OnSignal`을 `ReactionBinding`이나 별도 구독자가 처리합니다. 전체 조합은 [Use와 Reaction 연결하기](../../guides/interaction/connect-use-and-reaction.md)를 참고합니다.

## 핵심 흐름

```text
Scanner
  ↓ AddOffer / RemoveOffer
UseController
  ↓ 후보 선택
CurrentOffer
  ↓ TryUse(instigator)
UseOffer.OnSignal
  ↓
ReactionBinding / Action Target
```

Scanner와 실제 InputAction 구독은 Xeri Use Controller가 소유하지 않습니다.

## Offer 선택

`UseController`는 사용 가능한 Offer만 비교합니다.

1. `Priority`가 높은 후보 우선
2. 같은 Priority이면 선택적 `IUseOfferSelectionPolicy`의 낮은 score 우선
3. policy가 없으면 먼저 선택된 후보 유지

동적 선택 정책이 있으면 `Update()`에서 Context 변화를 반영해 CurrentOffer를 다시 계산합니다.
## Prompt와 수명

CurrentOffer가 바뀌면 이전 Prompt를 먼저 숨기고 `OnCurrentOfferChange`를 발행한 뒤 새 Prompt를 표시합니다.

Controller가 비활성화되면 Offer 목록 자체는 보존하지만 외부 이벤트 구독과 CurrentOffer/Prompt만 정리합니다. 다시 활성화되면 후보를 재구독하고 선택을 복원합니다.

## UseOffer

Offer는 다음 정보를 제공합니다.

- Prompt text
- Priority
- 사용 가능 여부
- World Anchor
- 사용 성공 시 `ISignalSource.OnSignal`

Offer 자체는 Door, NPC 같은 도메인 API를 실행하지 않습니다.

## 책임 범위

- 후보 집합과 CurrentOffer 선택
- Prompt 표시 전환 이벤트
- 사용 입력을 Offer에 전달

Physics/Raycast 탐색, Input System 구독, 실제 도메인 효과는 Scanner/Driver/Reaction 계층에서 처리합니다.

## 관련 문서

- [Reaction](reaction.md)
- [Xeri Game](../index.md)
