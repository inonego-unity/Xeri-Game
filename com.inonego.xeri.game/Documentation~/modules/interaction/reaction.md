# Reaction

Xeri Reaction은 Signal을 선택적 Guard 판정 뒤 Action Target 실행으로 연결하는 얇은 Runtime binding입니다.

**구분:** 이 Reaction은 `ISignalSource`와 `IActionTarget`의 연결을 의미합니다. 원소 반응·실제 피해/회복 결과·Ability의 Cost/Execution 승인 시스템이 아닙니다. 게임의 해당 경계는 [Gameplay Framework](../action/overview.md)를 참고합니다.

## 왜 필요한가

상호작용, 트리거, UI 이벤트 같은 Signal Source가 프로젝트 도메인 메서드를 직접 호출하기 시작하면 Source가 Door, Quest, UI 같은 구체 타입을 알아야 합니다. Reaction은 Source와 Target 사이에 최소 Context와 선택적 Guard만 두어 사건 발생과 실제 효과를 분리합니다.

## 언제 사용하는가

- Scene/Prefab 안에서 Signal Source와 도메인 Action을 느슨하게 연결할 때
- 같은 Source를 다른 Target 구현으로 교체하고 싶을 때
- 실행 전 조건 검사를 `ICond<ReactionContext>`로 분리하고 싶을 때

주소 기반 메시지 버스, 장시간 비동기 Sequence, 취소 가능한 Workflow가 필요한 경우에는 더 상위 시스템이 필요합니다.

## 기본 사용

`ReactionBinding`은 Source Component와 Target 계약을 연결합니다.

```csharp
reactionBinding.Configure
(
    signalSourceComponent,   // ISignalSource 구현 MonoBehaviour
    guardComponent,          // 없으면 null
    actionTarget             // IActionTarget 구현
);
```

Source가 `OnSignal`을 발생시키면 Guard가 통과한 경우 `IActionTarget.Execute(context)`가 호출됩니다. `UseOffer`와 연결하는 전체 예는 [Use와 Reaction 연결하기](../../guides/interaction/connect-use-and-reaction.md)를 참고합니다.

## 핵심 계약

```text
ISignalSource.OnSignal
        ↓ ReactionContext
ReactionBinding
        ↓ ICond<ReactionContext> (선택)
IActionTarget.Execute
```

`ReactionContext`는 Signal을 발생시킨 `Component`와 실행 주체 `GameObject`를 전달합니다.

## Binding 수명

`ReactionBinding`은 활성화될 때 Source, Guard, Target 계약을 해석하고 Source Signal을 구독합니다. 비활성화되면 구독과 런타임 endpoint를 정리합니다.

`Configure()`로 runtime endpoint를 교체할 수 있으며 새 입력을 먼저 검증한 뒤 기존 연결을 해제합니다.

## 실행 정책

현재 정책은 동기 실행 중 같은 Binding으로 다시 들어오는 Signal을 무시하는 `IgnoreWhileRunning` 형태입니다.

Guard가 false를 반환하거나 예외가 발생하면 Target을 실행하지 않습니다. Target 실행 예외는 Binding 경계에서 로그하고 이후 Signal 수명은 유지합니다.
## 책임 범위

Reaction은 직접 endpoint 연결만 담당합니다. 다음 기능은 포함하지 않습니다.

- 주소 기반 endpoint 탐색
- Registry를 통한 동적 대상 해석
- 비동기 Action 상태
- Sequence와 실행 순서 편집
- 취소 토큰과 장시간 실행 lifecycle

Action Target은 `SerializeReference`로 authoring할 수 있으며 Xeri Picker를 통해 구현 타입을 선택할 수 있습니다.

## Use와 조합

`UseOffer`는 `ISignalSource`를 구현하므로 직접 Reaction Source로 사용할 수 있습니다.

```text
UseOffer.TryUse
→ OnSignal
→ ReactionBinding
→ IActionTarget
```

이 구조에서 Use 계층은 후보 선택과 입력 의미만, Reaction 계층은 실제 도메인 효과 연결만 담당합니다.

## 관련 문서

- [Use System](use.md)
- [확장 계약](https://inonego-unity.github.io/Xeri/docs/concepts/extension-contracts.html)
- [Xeri Game](../index.md)
