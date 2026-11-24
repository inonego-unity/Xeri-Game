# Level

Xeri Level은 0부터 시작하는 레벨 값, 최대 레벨 제한과 선택적 경험치 기반 자동 레벨업을 제공하는 상태 모델입니다.

## 왜 필요한가

레벨 값과 경험치 누적, 최대 레벨 제한을 각각 처리하면 레벨업 시 남은 EXP 이월이나 상한 변경 규칙이 여러 곳에 분산됩니다. Xeri Level은 레벨 상태와 이벤트를 공통화하고, `LevelxEXP`는 경험치 기반 증가 규칙을 한 모델에 묶습니다.

## 언제 사용하는가

- 0-based Level과 최대 레벨 제한이 필요한 성장 시스템
- 경험치가 여러 레벨을 한 번에 넘을 수 있고 초과 EXP를 이월해야 할 때
- 레벨 변화 이벤트를 UI/게임 로직에서 함께 관찰할 때

프로젝트가 완전히 다른 곡선·스킬 포인트·Prestige 구조를 사용한다면 `LevelBase`만 확장하거나 별도 시스템을 두는 편이 낫습니다.

## 기본 사용

```csharp
var level = new LevelxEXP(new[] { 100, 150, 250 });
level.OnLevelUp += (_, e) => Debug.Log($"Level {e.Level}");

level.EXP += 120;
```

첫 100 EXP로 한 번 LevelUp하고 남은 EXP는 다음 레벨 진행도에 남습니다.

## LevelBase

실질적인 최대 레벨은 `LimitMax`와 파생 타입의 `FullMax` 중 작은 값입니다.

```text
Min = 0
Max = min(LimitMax, FullMax)
```

`BlockLevelUp`이 켜져 있거나 현재 값이 Max에 도달하면 `CanLevelUp`이 false가 됩니다.

`LevelUp(amount)`은 가능한 횟수만큼 한 단계씩 증가시키고 각 단계마다 `OnLevelUp`을 발생시킵니다.

## LevelxEXP

`LevelxEXP`는 각 레벨에서 다음 레벨로 넘어갈 때 필요한 EXP 배열을 소유합니다.

```text
EXP 증가
→ 현재 MaxEXP 이상인가?
→ 초과분을 남기고 LevelUp
→ 다음 레벨 MaxEXP로 반복
```

한 번에 많은 EXP를 넣으면 여러 레벨을 올리고 남은 EXP를 다음 레벨로 이월합니다.
레벨 값을 직접 변경하면 현재 EXP는 0으로 초기화됩니다.

EXP 테이블은 null이 아니어야 하고 각 요구량은 0 이상이어야 합니다.

## 책임 범위

- 레벨 값과 상한
- 레벨업 가능 상태
- 경험치 누적과 overflow 이월
- 레벨/값 변경 이벤트

스탯 증가, 스킬 해금, 보상 지급, 경험치 획득 원천은 `OnLevelUp`을 소비하는 상위 게임 시스템에서 처리합니다.

## 관련 문서

- [Xeri Game](../index.md)
- [Serializable Value와 Modifier](https://inonego-unity.github.io/Xeri/docs/modules/serialization/value.html)
