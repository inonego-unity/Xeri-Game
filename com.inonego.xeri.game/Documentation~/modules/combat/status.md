# Status

Xeri Status는 대상 하나에 걸린 상태(표식·버프·디버프·시간제 스택)의 **수명 규칙**을 관리합니다. 스택, 스택별 만료, 갱신, 횟수 소비, 출처별 분리를 맡고, 상태가 **무엇을 하는지**(피해 증폭, 폭발, 행동 제한)는 프로젝트가 변경 알림과 조회로 처리합니다.

## 언제 사용하는가

- 적에게 시전자별 표식을 남기고 표식 수로 스킬을 강화할 때
- 스택마다 따로 만료되는 버프(예: 스킬마다 5초 스택)를 쓸 때
- "다음 N회 공격 강화"처럼 횟수로 소모되는 버프를 쓸 때

면역·저항·정화 분류, 주기 피해 Tick, 원소 반응은 이 모듈이 다루지 않습니다. 주기 피해는 Execution Session으로 구성합니다.

## 기본 사용

```csharp
var mark = new StatusSpec
(
    new StatusKey("Mark"),
    maxStacks: 3,
    duration: 5f,                      // null이면 시간으로 끝나지 않음
    StatusStacking.SharedRenew,
    StatusSourcing.PerOrigin
);

var statuses = new StatusContainer<GameOrigin>();
statuses.OnChanged += (container, change) => presenter.Show(change);

statuses.Apply(mark, new GameOrigin(casterKey));   // Added / Stacked / Renewed / Rejected
int count = statuses.CountStacks(mark.Key);

statuses.Advance(deltaTime);                        // Ability State와 같은 Tick에서 진행
```

`GameOrigin`은 프로젝트가 정하는 출처 값입니다. `IEquatable<T>`를 구현해야 하며, 저장하려면 Unity 직렬화가 가능해야 합니다.

## 적용 규칙

| `StatusStacking` | Max 미만 재적용 | Max에서 재적용 |
|---|---|---|
| `Independent` | 새 스택만 새 시간으로 추가 | `StatusOverflow.Reject`면 거절, `ReplaceEarliest`면 가장 먼저 끝날 스택을 밀어냄 |
| `SharedRenew` | 스택 추가, 모든 스택 시간 재시작 | 스택 수 유지, 모든 스택 시간 재시작(`Renewed`) |
| `SharedKeep` | 스택 추가, 새 스택은 기존 남은 시간에 함께 끝남 | 거절 |

- `StatusSourcing.PerOrigin`은 출처마다 따로, `Shared`는 Key마다 하나를 둡니다. `Shared`의 출처는 가장 최근 적용자입니다.
- 같은 Key인데 동작에 쓰이는 규칙 값이 다른 Spec으로 적용하면 `ArgumentException`입니다. 지속 시간이 없을 때의 `Duration`, `Independent`가 아닐 때의 `Overflow`는 비교하지 않습니다. Status는 적용 시점 Spec의 복사본을 보관합니다.
- `Consume`은 가장 먼저 끝날 스택부터 줄입니다.

## 변경 알림과 시간

- 변경은 상태 변경이 끝난 뒤 `OnChanged` 하나로 보냅니다. `StacksRemoved`는 Status가 남는 감소, `Removed`는 Status가 사라지는 변경입니다.
- `Advance` 진행 중에는 외부 코드를 호출하지 않습니다. 한 번의 `Advance`에서 여러 스택이 끝나면 남은 시간, 같으면 적용 순서대로 알립니다.
- 알림 안에서 `Apply`·`Remove`를 호출할 수 있습니다. 구독자 하나가 실패해도 나머지에게 보낸 뒤 예외를 전달합니다.
- 내부 임시 목록은 Unity `ListPool`에서 빌려 쓰므로 갱신 적용(`Renewed`)·거절·소비·만료를 반복해도 목록을 할당하지 않습니다. 새 Status·새 스택을 만들 때와, 구독자가 있을 때 알림마다 호출 목록을 만들 때는 할당합니다.

## Revision·복제·저장

- `Revision`은 적용·소비·제거 같은 이산 변경마다 오릅니다. 시간 만료는 시간에서 결정되므로 올리지 않습니다.
- `CloneFrom`은 인스턴스와 구독을 유지한 채 알림 없이 복제합니다.
- Unity 직렬화는 Spec 복사본, 출처, 남은 시간, 적용 순번을 보존하고 복원 뒤 `Revision`을 올립니다.

## Ability와 연결

Ability는 Status를 참조하지 않습니다. Operation 결과를 받은 프로젝트 Rule이 `Apply`를 호출하고, 표식 수 같은 사용 조건은 프로젝트가 `IAbilityActivationRequirement<TSpec>`을 구현해 연결합니다.
