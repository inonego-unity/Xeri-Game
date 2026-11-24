# Board

Xeri Board는 2D/3D 좌표 공간에 객체를 배치하고, 객체에서 다시 위치를 조회할 수 있는 양방향 보드 모델입니다. 모델과 tile view 생성·회수는 별도 계층으로 분리됩니다.

## 왜 필요한가

보드형 게임에서 좌표→객체 조회와 객체→현재 좌표 조회를 각자 관리하면 이동·제거 시 두 자료구조가 쉽게 어긋납니다. Xeri Board는 Space와 Placeable 위치를 함께 관리하고, 시각 표현은 별도 View Binder에 맡겨 모델과 Scene 표현을 분리합니다.

## 언제 사용하는가

- Grid/Tile 기반으로 객체 배치와 역방향 위치 조회가 모두 필요할 때
- 같은 좌표 안에 여러 Index 슬롯을 둘 때
- Board 모델 변경에 맞춰 Tile View를 생성·회수하고 싶을 때

NavMesh나 자유 좌표 월드처럼 고정 Board 좌표가 핵심이 아니라면 별도 공간 모델이 더 적합합니다.

## 기본 사용

```csharp
var board = new Board2D<string>(4, 4);

board.Place(new Vector2Int(1, 2), 0, "unit-a");
string placed = board[new Vector2Int(1, 2), 0];
var point = board["unit-a"];

board.Remove("unit-a");
```

`Place()`는 같은 객체가 기존 위치에 있으면 먼저 제거한 뒤 새 Point에 등록하므로 한 Board 안에서 중복 위치를 만들지 않습니다.

## 핵심 모델

```text
Board
├─ spaceMap : Vector → Space
└─ pointMap : Placeable → Point(Vector, Index)
```

`BoardBase<TVector, TIndex, TSpace, TPlaceable>`는 좌표별 `Space`와 배치된 객체의 역방향 위치를 함께 관리합니다.

`Board2D`와 `Board3D`는 유효 좌표 범위와 초기 Space 생성 방식을 제공합니다.

## Space와 Point

하나의 Vector에는 `BoardSpace`가 있고, Space 내부에서 `TIndex`로 여러 객체 위치를 구분할 수 있습니다.

```text
Vector
  ↓
BoardSpace
  ├─ Index A → Placeable
  └─ Index B → Placeable
```

`Point`는 `(Vector, Index)`를 묶으며, Board는 Placeable을 key로 현재 Point를 역조회합니다.

## 배치와 이동

`Place()`는 대상 객체가 이미 다른 위치에 있으면 기존 위치에서 먼저 제거한 뒤 새 위치에 등록합니다. 따라서 하나의 Board 안에서 같은 Placeable이 두 Point에 동시에 등록되지 않습니다.
## 이벤트

Board는 다음 변경 이벤트를 제공합니다.

- `OnAddSpace`, `OnRemoveSpace`: 좌표 공간 추가/제거
- `OnPlace`, `OnRemove`: 객체 배치/제거

호출자가 `invokeEvent = false`를 사용하면 모델 상태만 변경하고 이벤트를 생략할 수 있습니다.

## View 바인딩

`BoardViewBinder`는 Board의 Space 이벤트를 tile view 생성·회수로 변환합니다.

```text
Board.OnAddSpace
    ↓
BoardViewBinder
    ↓
BoardTileFactory.CreateTile
    ↓
BoardTileViewMap.Register
```

`Bind()` 시 현재 Board의 모든 Space를 다시 읽어 tile map을 구성하고, `Unbind()` 시 이벤트 구독을 해제한 뒤 모든 tile view를 회수합니다.

Tile 생성 뒤 map 등록이나 view hook이 실패하면 생성한 tile을 factory로 반환하고 예외를 전달합니다.

## 책임 범위

- Board는 좌표·Space·Placeable 관계를 소유합니다.
- View는 좌표를 표시 위치로 변환하고 tile 표현을 소유합니다.
- `BoardViewBinder`는 모델 이벤트와 tile 수명을 연결합니다.
- 게임 규칙상 어떤 객체를 어디에 놓을 수 있는지는 `BoardSpace.CanPlace()`와 상위 도메인 정책이 결정합니다.

## 제약과 주의사항

- Space를 제거하면 그 Space의 Placeable도 함께 Board에서 제거됩니다.
- `RemoveSpaceAll()`과 `RemoveAll()`은 컬렉션을 순회하면서 공통 제거 계약을 사용합니다.
- Board 모델에 Scene object 수명이나 프로젝트별 grid rule을 직접 넣지 않습니다.

## 관련 문서

- [Xeri Game](../index.md)
- [Serializable Collections](https://inonego-unity.github.io/Xeri/docs/modules/serialization/collections.html)
