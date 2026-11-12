# WorldObject와 State

Scene 객체와 지속 상태를 분리할 때 `IWorldObject`와 `WorldObjectState`를 사용합니다. 두 타입은 월드 전체 Registry, 저장 파일 형식, Scene 로딩 정책을 대신 구현하지 않습니다.

## 핵심 계약

| API | 역할 |
|---|---|
| `IWorldObject.ID` | Scene 객체와 State를 연결하는 안정 문자열 ID |
| `CreateState()` | Scene authoring 초기값으로 상태 생성 |
| `BindState(WorldObjectState)` | 생성·복원한 상태를 Runtime 객체에 연결 |
| `UnbindState()` | 상태 연결 해제 |
| `WorldObjectState.ID` | 해당 상태가 대응하는 World Object의 ID |

`WorldObjectState`는 다형적 상태의 추상 기반 클래스입니다. concrete State의 필드와 동작은 각 모듈이 정의합니다.

## 권장 사용 순서

~~~text
Scene Object 발견 → 안정 ID 확인
→ 상태 조회 또는 CreateState()
→ BindState(state)
→ 게임 로직 실행
→ 사용 수명 종료 시 UnbindState()
~~~

ID의 전역 유일성, 저장소 접근·복원 순서, Scene 생성·표현 적용은 소비 프로젝트가 담당합니다.

[Foundation](index.md) · [Entity 수명](entity-and-spawn.md)
