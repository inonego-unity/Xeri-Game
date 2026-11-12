# Xeri Spatial·Targeting — 공간 관측과 대상 선택

**Spatial**은 위치와 조준의 출처·관측 시점을 표현하고, **Targeting**은 입력 필드의 존재와 후보 우선순위를 판정합니다. 두 계층 모두 피해 대상의 게임적 의미와 실제 접촉·적용은 결정하지 않습니다.

Spatial API는 `inonego.Xeri.Spatial`, Targeting API는 `inonego.Xeri.Game` 네임스페이스에 있습니다.

## Spatial: 어디서·언제 관측할 것인가

| API | 계약 |
|---|---|
| `SpatialSource<TReference>` | `None` / `Point(Vector3)` / `FromReference(TReference)` |
| `SpatialRequest<TReference>` | Source, `SpatialAimMode`, 명시적 Direction |
| `ISpatialSourceProvider<TReference>.TryResolve` | 외부 참조의 위치 및 선택적 회전 공급 |
| `SpatialSession<TReference>` | `Begin(time, origin)`·`Evaluate(time, origin)`·`Current`·`IsLocked` |
| `SpatialSample` | `HasPosition`·`Position`, `HasDirection`·`Direction`, `HasRotation`·`Rotation` |

### 출처와 Aim

- `SpatialSource.Point(worldPosition)`는 지정된 월드 지점을 사용합니다.
- `SpatialSource.FromReference(reference)`는 외부 `ISpatialSourceProvider<TReference>`에 현재 위치·회전 질의를 위임합니다. 참조 기반 요청에서 Provider는 필수입니다.
- `SpatialAimMode.None`은 방향을 계산하지 않습니다.
- `SpatialAimMode.Explicit`은 `SpatialRequest.Direction`으로 지정한 방향을 유효한 단위 벡터로 정규화합니다.
- `SpatialAimMode.TowardSource`는 `sourcePosition - origin`으로 조준 방향을 계산합니다.

Position·Direction·Rotation은 각각 독립적인 유효성 플래그를 갖습니다. 예를 들어 참조를 해석하지 못해도 Explicit Direction은 유효할 수 있고, 위치가 있어도 무효한 방향이면 `HasDirection == false`가 됩니다. 무효 입력을 임의로 조준 방향·위치로 치환하지 마세요.

### 시간 정책

| `SpatialTimeMode` | 동작 |
|---|---|
| `Capture` | `Begin` 시점에 한 번 관측하고 고정 |
| `Follow` | 이후 서로 다른 시각의 `Evaluate`마다 새로 관측 |
| `LockAtTime` | 설정한 `lockTime`에 도달할 때 새로 관측하고 그 값을 고정 |

~~~csharp
using inonego.Xeri.Spatial;
using UnityEngine;

// 한 지점을 이번 실행 시작 시점에 포착합니다.
var request = new SpatialRequest<object>(
    SpatialSource<object>.Point(new Vector3(4f, 0f, 2f)),
    SpatialAimMode.TowardSource
);
var spatial = new SpatialSession<object>(
    request,
    SpatialTimeMode.Capture
);

spatial.Begin(time: 0f, origin: Vector3.zero);
SpatialSample sample = spatial.Current;
if (sample.HasPosition && sample.HasDirection)
{
    // sample.Position / sample.Direction을 현재 실행에 사용합니다.
}
~~~

`Begin`에는 0 이상의 유한 시간을 전달하며 새 관측 상태로 시작합니다. `Evaluate` 시각은 이전 시각보다 작아질 수 없고, 동일 시각이면 다시 관측하지 않습니다. 시간 되감기나 서버 Rewind가 필요하면 **프로젝트의 별도 기록 Provider**가 필요합니다. `SpatialSession` 자체는 과거 위치를 저장하지 않습니다.

## Targeting: 입력 요구와 후보 선택

### 필수 필드 판정

`SpatialTargetField`는 [Flags] 형태의 `Direction`·`Position`·`Reference`입니다. `SpatialTargetRequirement(requiredAll, requiredAny)`는 다음을 각각 검사합니다.

- `RequiredAll`: 지정한 모든 필드가 존재해야 함
- `RequiredAny`: 지정한 필드 가운데 최소 하나가 있어야 함. `None`이면 추가 요구 없음
- `IsSatisfiedBy(fields)`: 실제 제공된 필드와 위 두 조건을 비교

이 검사는 **입력 필드가 표시됐는지**만 확인합니다. Provider 조회 성공 여부, 유효한 Quaternion이나 실제 타깃의 생존·적합성을 대신 보장하지 않습니다. 그 검증은 Spatial/게임 후보 공급자와 분리하세요.

### 후보 선택 API

| API | 입력과 결과 | 정책 |
|---|---|---|
| `TargetSelector.TrySelect` | `IEnumerable<TCandidate>`에서 하나를 `out selected`로 반환 | 기본 낮은 Score 우선; 후보 없음·전부 제외 시 false |
| `TargetSelector.SelectAll` | Filter 통과 후보를 `IList<TCandidate>`에 추가 | 발견 순서; 기존 항목은 지우지 않음 |
| `TargetSelector.SelectTop` | 상위 `maxCount`개를 순위순으로 결과 목록에 추가 | 점수 계산 완료 후 추가 |
| `ITargetScorer<TCandidate>` | `TryScore(in candidate, out float score)` | 평가 불가능한 후보를 false로 제외 |
| `TargetSelection<TCandidate,TScoreComparer,TTieBreaker>` | 호출자 순회 중 최적 하나를 누적하는 값 타입 | 비교기와 동점 규칙을 값 타입으로 주입 |

~~~csharp
using System.Collections.Generic;
using inonego.Xeri.Game;

// 후보와 적합성은 게임이 제공합니다. 낮은 거리 점수를 우선합니다.
List<float> candidateDistances = new() { 9f, 2f, 5f };
if (TargetSelector.TrySelect(
    candidateDistances,
    distance => distance,
    out float closestDistance))
{
    // closestDistance == 2f
}

var top = new List<float>();
TargetSelector.SelectTop(candidateDistances, 2, distance => distance, top);
// top에는 2f, 5f가 순서대로 추가됩니다.
~~~

`TrySelect`·`SelectTop`은 `Func<TCandidate,float>` 또는 `ITargetScorer<TCandidate>`를 받으며 선택적 `IFilter<TCandidate>`, `IComparer<float>`, `IComparer<TCandidate>`를 지원합니다. NaN Score와 실패한 Scorer의 후보는 제외합니다. 기본 점수는 오름차순이고, 동점에서 `tieBreaker`를 제공하면 그 비교를 따릅니다. 별도 동점 비교기가 없다면 일반적으로 발견 순서가 유지됩니다.

`SelectAll`과 `SelectTop`은 **destination에 추가**하며 자동 중복 제거·초기화는 하지 않습니다. `SelectAll`은 열거 도중 예외가 발생하면 이미 추가된 결과를 복구하지 않습니다. `SelectTop`은 후보 평가를 완료한 뒤 결과를 추가하지만, destination의 `Add` 자체가 실패하는 경우까지 원자성을 보장하지는 않습니다.

`TargetSelection<...>`은 아주 많은 후보를 직접 순회하면서 현재 최적 하나만 유지하려는 경우에 사용합니다. `TryConsider(in candidate, score)`는 NaN을 거부하며 `HasValue`·`Selected`·`Score`로 결과를 확인합니다. 재사용 시에는 새로운 선택 상태를 만들어 이전 후보와의 혼합을 방지하세요.

## 공간·선택·접촉의 경계

~~~text
실행 요청의 위치·방향·참조
  → SpatialTargetRequirement (필수 필드만 검사)
  → SpatialSession (위치·방향·회전을 실제로 관측)
  → 게임의 후보 집합·필터·Scorer
  → TargetSelector (하나/전체/상위 N개)
  → 필요하면 PhysicsDelivery/ProjectileDelivery (Collider 접촉)
  → 프로젝트 GameplayOperation (실제 적용)
~~~

Targeting은 Faction·피격 가능 여부·동일 대상 중복 제한을 내장하지 않습니다. 각 기준은 게임의 후보 공급·Filter·Score 또는 Operation에서 처리합니다.

## 관련 문서

- [Gameplay Framework 전체 구조](overview.md)
- [Execution — 실행 시간과 Trigger](execution.md)
- [Delivery·Gameplay Operation](delivery-operation.md)
- [연결 가이드](../../guides/action/ability-execution-delivery.md)
