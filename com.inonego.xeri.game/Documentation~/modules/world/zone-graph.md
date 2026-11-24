# Zone Graph

`ZoneGraph`는 안정 ID를 가진 `Zone`과 두 Zone을 연결하는 `ZoneLink` collection으로 공간 topology를 표현하는 직렬화 모델입니다.

## 왜 필요한가

공간 연결 관계와 Actor의 현재 위치·이동 상태를 한 객체에 넣으면 정적 topology와 runtime cursor가 함께 변합니다. `ZoneGraph`는 "어떤 Zone이 어떤 Zone과 연결되는가"만 보존해서 Scene 로딩, pathfinding, Actor 이동 정책이 같은 topology를 독립적으로 소비할 수 있게 합니다.

## 언제 사용하는가

- Room/Area/Stage 구역 연결을 안정 ID 기반 graph로 보관할 때
- 직렬화된 topology를 runtime lookup으로 검증·조회해야 할 때
- 여러 시스템이 같은 Zone 연결 관계를 공유하지만 각자의 현재 위치 상태는 따로 가져야 할 때

실제 좌표 기반 pathfinding graph나 NavMesh 자체를 대체하는 용도는 아닙니다.

## 기본 사용

직렬화 또는 authoring으로 구성된 Graph는 사용 전에 검증하고 ID로 조회합니다.

```csharp
graph.Validate();

if (graph.TryGetZone("zone.lobby", out Zone lobby))
{
    // 프로젝트 runtime에서 현재 Zone이나 Scene을 연결한다.
}

if (graph.TryGetDestination(link, lobby, out Zone destination))
{
    // 이동 가능 후보로 사용한다.
}
```

Graph는 이동 주체의 현재 Zone을 저장하지 않으므로 Actor/Session별 cursor는 별도 Runtime에 둡니다.

## 핵심 구조

```text
ZoneGraph
├─ Zones
│  └─ ZoneID → Zone lookup
└─ ZoneLinks
   └─ endpoint ID ↔ endpoint ID
```

Graph는 serialized 배열을 원본으로 보관하고 필요할 때 runtime lookup을 재구성합니다.

## 검증

`Validate()` 또는 내부 registry 재구성 시 다음을 확인합니다.

- Zone은 null일 수 없음
- ZoneID는 비어 있으면 안 됨
- ZoneID 중복 금지
- Link는 null일 수 없음
- Link 양 끝점은 서로 달라야 함
- Link의 두 endpoint ID가 모두 Zones에 존재해야 함

유효하지 않은 topology는 부분 lookup으로 사용하지 않고 예외로 거부합니다.
## 조회

- `TryGetZone(zoneID)`: 안정 ID로 Zone 조회
- `ContainsLink(link)`: 이 Graph에 등록된 Link인지 확인
- `TryGetDestination(link, source)`: Link의 반대쪽 목적지 Zone 조회

Graph에 등록되지 않은 Link나 source endpoint와 관계없는 Link는 목적지 조회에 실패합니다.

## 책임 범위

Zone Graph는 topology만 소유합니다. 다음 상태는 포함하지 않습니다.

- Actor의 현재 Zone
- Zone 사이 이동 진행 상태
- Unity Scene load/unload
- Stage/Quest 진행
- pathfinding cost와 route 정책

따라서 프로젝트는 ZoneGraph를 정적 공간 관계로 사용하고, 이동하는 주체의 cursor/state는 별도 Runtime에서 관리합니다.

## 관련 문서

- [Xeri Game](../index.md)
