# Billboard

`Billboard`와 `BillboardManager`는 월드 표현의 카메라 방향 갱신을 지원합니다. `BillboardLookMode`는 카메라를 향하는 방식을 선택하는 데 사용합니다.

이 기능은 [Entity Presentation](../../foundation/entity-and-spawn.md)의 객체 생성·등록·반환 계약과 구분됩니다. Billboard는 실제 표현 동작을 담당하지만, UI·카메라 선택 정책이나 게임별 View 수명 자체를 소유하지는 않습니다.
