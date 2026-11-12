# Physics Query

Xeri Physics Query는 Unity의 2D/3D Raycast, Overlap, Cast NonAlloc 호출을 공통 Volume 계약으로 조립하는 보조 시스템입니다.

## 왜 필요한가

Box/Sphere/Capsule마다 Unity Physics 호출 시그니처가 달라지면 같은 감지 로직이 형상별 분기로 반복되기 쉽습니다. Xeri는 형상과 Pose를 `PhysicsVolume2D/3D` 값으로 묶고, reusable buffer와 NonAlloc 호출을 공통 경계로 제공합니다.

## 언제 사용하는가

- 같은 시스템이 Box/Sphere/Capsule query를 교체 가능하게 지원해야 할 때
- 매 frame GC를 피하기 위해 NonAlloc buffer를 재사용할 때
- Layer/Trigger/filter 정책과 형상 표현을 분리하고 싶을 때

단발성 Raycast 하나만 필요한 경우에는 Unity API를 직접 호출하는 편이 더 단순합니다.

## 기본 사용

```csharp
var volume = PhysicsVolume3D.CreateSphere
(
    transform.position,
    radius: 2f
);

var results = new Collider[32];
int count = PhysicsQuery3D.Overlap
(
    in volume,
    results,
    targetLayerMask
);

for (var i = 0; i < count; i++)
{
    Collider hit = results[i];
    // 프로젝트 필터/선택 정책 적용
}
```

Query는 결과 개수만 반환하므로 buffer overflow 가능성과 hit 후처리는 호출자가 관리합니다.

## 핵심 구조

```text
PhysicsVolume2D / PhysicsVolume3D
    + direction / distance
    + reusable result buffer
    + LayerMask / Trigger policy
                ↓
         PhysicsQuery2D / 3D
                ↓
          Unity NonAlloc API
```

Volume은 Box, Sphere/Circle, Capsule 등의 형상과 위치·크기·회전을 값으로 표현합니다.

## NonAlloc 경계

호출자가 결과 배열을 소유하고 재사용합니다. Query는 새 결과 컬렉션을 생성하지 않고 Unity `NonAlloc` API가 기록한 개수를 반환합니다.

3D 기준 주요 진입점은 다음과 같습니다.

- `Raycast()`
- `Overlap(in PhysicsVolume3D, Collider[])`
- `Cast(in PhysicsVolume3D, direction, distance, RaycastHit[])`

2D도 같은 역할을 해당 Unity 2D Physics 계약으로 제공합니다.
## 입력 검증

- 결과 buffer가 null이면 예외입니다.
- 거리는 0 이상이어야 하며 NaN을 허용하지 않습니다.
- 방향 벡터는 유한해야 합니다.
- 영벡터 방향은 예외 대신 결과 0건으로 처리합니다.

Capsule은 local Y축 기준으로 두 반구 중심을 계산한 뒤 Unity Capsule API에 전달합니다.

## 책임 범위

Physics Query는 호출 모양과 검증을 공통화하지만 다음 정책은 그대로 호출자에게 남깁니다.

- 결과 buffer 크기와 overflow 대응
- LayerMask 구성
- Trigger 포함 여부
- 결과 정렬, 가장 가까운 hit 선택
- 자기 Collider 제외와 게임 도메인 필터

## 관련 문서

- [Game Controller](../character/controller.md)
- [Xeri Game](../index.md)
