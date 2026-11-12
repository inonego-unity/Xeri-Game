# AI Group과 Brain

`AIGroup`은 여러 Spawned Entity를 하나의 AI 판단 단위로 묶는 독립 Runtime 집단입니다. `IEntity.Group`, Faction, 공유 인지 상태와 전투 정책은 소유하지 않습니다.

## 왜 필요한가

개별 Entity AI와 여러 Entity를 함께 판단하는 집단 AI는 수명과 입력 단위가 다릅니다. `AIGroup`은 구성원 집합만 소유하고, `EntityBrain`/`GroupBrain`은 판단 실행 경계만 제공해서 실제 perception, tactical policy와 실행 명령을 프로젝트 계층에 남깁니다.

## 언제 사용하는가

- 여러 Spawned Entity를 하나의 squad/formation 판단 단위로 묶을 때
- Entity 단위 Brain과 Group 단위 Brain을 별도로 실행하고 싶을 때
- Group 종료와 Brain Unbind 수명을 명시적으로 연결해야 할 때

단순히 팀 번호만 비교하려는 경우에는 `Entity.Group`이나 Faction 값만으로 충분할 수 있습니다.

## 기본 사용

```csharp
var group = new AIGroup(1001UL);
group.AddMember(entityA);
group.AddMember(entityB);

groupBrain.Bind(group);
groupBrain.Tick(deltaTime);

// 수명 종료
groupBrain.Unbind();
group.Dispose();
```

실제 프로젝트에서는 별도 Runner가 등록된 Brain을 일정한 Tick 순서로 호출하는 구성이 자연스럽습니다. Xeri Brain 자체는 Unity `Update()`를 자동으로 소유하지 않습니다.

## 구성원 계약

구성원은 유효한 Entity Key를 가진 `Spawned` 상태여야 합니다.

```text
Spawned Entity
  + valid Key
      ↓ AddMember
AIGroup
  └─ Key 오름차순 MemberMap
```

같은 객체의 중복 등록은 false를 반환하고, 같은 Key에 다른 객체가 이미 있으면 오류로 거부합니다.

`RemoveMember(entity)`는 Entity의 Key가 이미 해제됐더라도 참조 동일성으로 기존 구성원을 찾을 수 있습니다.

## 수명

`Dispose()`는 terminal 종료입니다.

- 모든 구성원을 제거
- 이후 구성원 변경 금지
- `OnDisposed` callback을 각각 끝까지 호출
- 여러 callback 실패는 집계해 전달

Dispose 이후 `AddMember`, `RemoveMember`, `ClearMembers`는 허용하지 않습니다.

## Brain

`EntityBrain`은 하나의 Spawned Entity, `GroupBrain`은 하나의 유효한 `AIGroup`에 바인딩되어 판단 Tick을 실행하는 추상 경계입니다.

```text
EntityBrain → IReadOnlyEntity
GroupBrain  → IReadOnlyAIGroup
```

둘 다 `Bind` / `Unbind`, 활성 상태와 `Tick(deltaTime)`만 공통화하며 실제 판단 상태, 행동 선택과 명령 형식은 파생 구현이 소유합니다.

Bind hook이 실패하면 Core 바인딩을 되돌리고 `OnUnbound` rollback까지 시도합니다. `GroupBrain`은 바인딩된 AIGroup이 Dispose되면 자동으로 Unbind합니다.

Brain 자체는 Unity Update에 자동 연결되지 않으므로 호출자가 적절한 AI Tick 경계에서 실행합니다.

## 책임 범위

AIGroup은 구성원 집합과 집단 수명만 관리합니다. 다음은 별도 시스템 책임입니다.

- Entity의 `Group` 값
- Faction 관계
- 공유 perception/blackboard
- 공격 대상 선택
- squad formation과 tactical policy

이 구분을 유지하면 하나의 Entity가 게임 규칙상의 Group과 AI 판단 집단을 서로 다른 의미로 가질 수 있습니다.

## 관련 문서

- [Entity와 Spawn 수명](../../foundation/entity-and-spawn.md)
- [Xeri Game](../index.md)
