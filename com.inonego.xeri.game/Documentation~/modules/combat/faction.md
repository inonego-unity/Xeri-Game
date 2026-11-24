# Faction

`FactionChecker`는 두 그룹 인덱스 사이의 상대 진영 관계를 판정하는 추상 클래스입니다. 실제 적대·동맹 정책은 이를 구현하는 프로젝트가 정의합니다.

## 주요 API

- `RelativeFaction`: `Me`, `Ally`, `Enemy`, `Neutral`
- `RelativeFactionGroup`: `Me`, `Ally`, `All`, `AllyNotMe`, `AllNotMe`, `Neutral`, `Enemy`
- `FactionChecker.Check(int self, int other)`: 두 그룹 인덱스의 관계 확인
- `FactionChecker.CheckIsInGroup(int self, int other, RelativeFactionGroup group)`: 관계 그룹 판정

`IReadOnlyGroupMember`를 받는 오버로드는 두 객체의 `Group.Base`를 사용합니다.

## 책임 범위

팀 배치, Faction 간 관계 정의, HP 적용, Targeting에서 적합한 대상 선택은 별도 정책입니다.

[HP](hp.md) · [Targeting](../action/spatial-targeting.md)
