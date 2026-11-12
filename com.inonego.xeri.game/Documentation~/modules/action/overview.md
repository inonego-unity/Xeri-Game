# Xeri Game — Action 모듈

Xeri Gameplay Framework는 **사용 가능한 능력의 상태**, **한 번의 실행**, **공간과 대상의 해석**, **물리 접촉**, **게임 결과 적용**을 서로 독립적인 계약으로 제공합니다. 특정 게임의 공격 종류, 피해 공식, 애니메이션, 입력·AI 정책을 대신 구현하는 전투 엔진은 아닙니다.

## 언제 사용하는가

- 같은 Ability를 장비·폼·입력 슬롯에 따라 다르게 연결하고, 소유자별 Cooldown·Charge를 유지할 때
- 사용 조건과 비용을 평가한 뒤 하나의 Action을 시작하고 그 시간·입력·종료를 관리할 때
- 위치 출처와 추적 시점을 명시하고 대상 후보를 필터링·점수화할 때
- Physics 2D/3D 접촉 및 Projectile 이동 결과를 프로젝트의 Damage·Heal·Effect 규칙에 전달할 때

## 구성 요소와 책임

| 계층 | 핵심 계약 | 보관·결정하는 것 | 보관하지 않는 것 |
|---|---|---|---|
| Ability 상태·바인딩 | `AbilityInstance<TDefinition>`, `AbilityState`, `AbilityBindings<TDefinition>` | 논리 Ability Identity, 소유자별 지속 값, 현재 Entry 연결 | 한 번의 Action 진행 |
| 사용 선택·승인 | `AbilityUseSelector<...>`, `AbilityUseSelection<...>`, `AbilityActivationPolicy<...>` | 선택한 실행, Requirement·Cost 평가, 비용 소비와 상태 전이 | 프로젝트의 배타 실행 슬롯 |
| Execution | `ExecutionDefinition`, `ExecutionSession`, Flow·Behavior·Trigger | 단일 행동의 시간, Command·Signal, Commit, Child·종료 | 영구 Ability 상태와 게임별 Effect 관리 |
| Spatial | `SpatialRequest<TReference>`, `SpatialSession<TReference>` | 위치·방향·회전 출처, Capture·Follow·Lock | 게임의 위치 히스토리·되감기 |
| Targeting | `SpatialTargetRequirement`, `TargetSelector` | 필수 입력 필드 검증과 대상 후보 선택·순위 | 적·아군·면역·피해 정책 |
| Delivery | `PhysicsDelivery2D/3D`, `ProjectileDelivery2D/3D` | Unity Collider Contact, 쿼리 완전성, 선형 Sweep | Contact의 전투적 의미 |
| Operation | `IGameplayOperation<...>`, `GameplayOperationRunner` | 게임 구현의 작업 호출과 Applied/NoOp/Rejected | 피해량·회복량·상태이상의 내장 규칙 |

주된 게임 계약은 `inonego.Xeri.Game`, Spatial은 `inonego.Xeri.Spatial` 네임스페이스에 있습니다. 모듈들은 필요에 따라 선택적으로 조합하며, 모두를 실행하는 단일 공개 Manager는 없습니다.

## 권장 호출 흐름

~~~text
프로젝트 Owner (지속 AbilityState와 Resource 소유)
    ├─ AbilityBindings.TryResolve(EntryKey)
    │   └─ AbilityActivationContext(Owner, Instigator, Binding, Project)
    ├─ AbilityUseSelector.TrySelect(Context)
    │   └─ AbilityUseSelection (Execution + Policy + 상태 전이)
    ├─ 프로젝트 Host가 배타 슬롯·기존 Action 정책 결정
    ├─ Selection.TryActivate()  ← Requirement/Cost 검사 → Cost.Pay → 상태 전이
    └─ ExecutionDefinition.CreateSession(...) → Session.Begin()
        ├─ Session.Advance(deltaTime) → Flow, Behavior
        ├─ Behavior에서 SpatialSession/TargetSelector 호출
        ├─ PhysicsDelivery 또는 ProjectileDelivery → Contact
        ├─ GameplayOperationRunner → 프로젝트의 실제 Result
        └─ Session.TryComplete() 또는 End(reason)
~~~

선택은 부작용 없이 후보를 결정해야 합니다. `AbilityUseSelection.Evaluate()`는 무소비 사전 검사이며 실행 슬롯 예약이 아닙니다. 프로젝트 Host는 **슬롯 거부·선점, 실제 Session 생성·시작과 정리, 게임별 자원 원자성**을 명시적으로 소유합니다.

### 서로 다른 확정·결과를 혼동하지 않기

| 호출·결과 | 의미 |
|---|---|
| `AbilityUseSelection.TryActivate()` | 사용 조건 확인 후 Cost 소비·선택 상태 전이. 선택은 성공·실패와 무관하게 1회 시도 |
| `ExecutionSession.TryCommit()` | 이미 시작된 Session의 내부 Commit 이벤트. Ability Cost와 무관 |
| `DeliveryContact2D/3D` | Collider와 접촉 지오메트리. 실제 Hit·Damage·Kill 판정이 아님 |
| `GameplayOperationResult.Applied()` | 프로젝트 Operation이 적용됨. 수치·사망·피해 분류를 의미하지 않음 |
| `ExecutionSession.End(reason)` | Session과 그 소유 Child의 실행 수명 종료. 별도 Owner에 인수된 월드 객체까지 자동 제거하지 않음 |

`UseController`의 주변 상호작용 후보 선택은 위 Ability 사용 승인과 다른 시스템입니다. [Use System](../interaction/use.md)에서 별도로 다룹니다.

## 상태와 수명

**지속 상태.** `AbilityState`의 Cooldown·Charge·`Values`는 논리 Owner가 보관하고 `Advance(deltaTime)`로 갱신합니다. 저장·복원 및 Owner 교체 정책은 프로젝트가 정합니다. 현재 Binding 변경이 이미 시작된 실행의 Identity를 다시 선택하지는 않습니다.

**실행 상태.** `ExecutionDefinition`은 재사용 가능한 설정이고, 각 `CreateSession`은 별도 Flow·Behavior Runtime을 생성합니다. Session은 한 번만 `Begin()`할 수 있습니다. 완료를 감지한 Host가 `TryComplete()`를 호출하거나 종료 이유를 지정해 `End(reason)`해야 합니다.

**대상과 접촉.** Spatial의 관측 실패는 `SpatialSample.HasPosition/HasDirection/HasRotation`으로 분리됩니다. Targeting 후보·필터·점수는 프로젝트가 제공합니다. Delivery는 검색 버퍼가 포화되면 `IsComplete` 또는 `IsSaturated`로 불완전성을 보고합니다. 불완전한 결과를 확정적인 부재나 최근접 접촉으로 해석하지 마세요.

**실행 이후 결과.** Projectile·지속 Effect·월드 오브젝트의 소유권을 Session에서 별도 Owner로 넘겼다면, 그 Owner가 Tick·수거·종료를 담당합니다. `ExecutionSession.AttachChild()`는 정말로 부모 종료와 함께 끝나야 하는 자식 Session에만 사용합니다.

## 실패 경계

| 시점 | Xeri가 수행하는 처리 | 프로젝트가 책임질 부분 |
|---|---|---|
| Requirement 또는 Cost 평가 거부 | 비용을 소비하지 않고 거부 결과 반환 | 사용자 피드백, 재시도·대체 Action |
| 여러 Cost의 `Pay()` 중 예외 | 예외 전달, 이미 지불한 비용은 자동 환급하지 않음 | 외부 자원 원자성·환급 규칙 |
| 실행 `Begin()` 중 Flow·Behavior·Observer 실패 | 시작된 실행 요소를 역순 정리하며 Cancelled 종료 | 이미 발생한 외부 효과와 Host 슬롯 정리 |
| 실행 `Advance()`·Command·Signal 중 예외 | Session 실패 종료·예외 전달 | 입력 차단, 월드 작업 정리 |
| Spatial 대상 소실·물리 검색 불완전 | 유효성·완전성의 관측 결과 제공 | 재조회, 불발/유예, 게임별 적합성 판정 |
| Operation `Rejected` / `NoOp` | 프로젝트가 반환한 상태 전달 | 실제 피해·면역·수치 처리 |

## 상세 문서

- [Ability — 상태·바인딩·활성화](ability.md): Definition·Instance, Layer, Cooldown·Charge, 선택·비용 계약
- [Execution — Flow·Behavior·Trigger](execution.md): Session 생성, 시간 진행, Command·Signal, Commit·종료
- [Spatial·Targeting](spatial-targeting.md): 위치 출처·시간 모드, 필드 요구, 대상 점수화
- [Delivery·Gameplay Operation](delivery-operation.md): 2D/3D 접촉·버퍼·Projectile·결과 적용
- [사용 가이드 — Ability에서 Operation까지 연결](../../guides/action/ability-execution-delivery.md): API를 Host 정책과 조합하는 실제 순서
- [Entity·Actor·Spawn](../../foundation/entity-and-spawn.md) / [소유권과 수명](https://inonego-unity.github.io/Xeri/docs/concepts/ownership-and-lifetime.html): 독립적인 논리 소유권과 Scene 수명

이 문서는 **현재 공개 Runtime 계약**을 설명합니다. 프로젝트별 공격·스킬 규칙, 실전 Host 승인 정책, 저장 포맷과 테스트 실행 내역은 이 API의 일부가 아닙니다.
