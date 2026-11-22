# Locomotion 검증 씬

Motion·Motor·Movement 구현을 큐브와 바닥만으로 직접 조작해 확인하는 통합 검증 씬입니다. 카드를 하나씩 골라 개별로 확인하고, 카드마다 자동 점검이 PASS/FAIL을 보여 줍니다. 자동 테스트를 대신하지 않고 눈으로 보는 확인을 돕습니다.

## 준비와 실행

1. 프로젝트에 `com.inonego.xeri.game`과 Input System 패키지가 있어야 합니다. Input System이 없으면 이 폴더의 어셈블리는 컴파일되지 않습니다.
2. 메뉴 **Tools > Xeri Game > Locomotion 검증 씬 만들기**로 씬을 만듭니다. 패키지가 편집 가능한 위치에 있으면 `Validation/Locomotion/Scenes/`에 저장하고, 읽기 전용이면 저장 위치를 묻습니다. 이미 있으면 **Locomotion 검증 씬 열기**로 엽니다.
3. Play 모드로 들어갑니다. 도형과 카드 구역은 Play 때 코드가 만듭니다.

Play 중에는 Physics를 스크립트 모드로 바꿔 한 Tick(0.02초)마다 합성 → Motor 적용 → `Physics.Simulate`를 직접 돌리고, 끝나면 원래 모드로 되돌립니다. Active Input Handling이 Input System만 켜져 있어도 동작합니다.

## 화면

| 영역 | 내용 |
| --- | --- |
| 왼쪽 | 카드 목록, 시간 조작(정지·한 Tick·속도), 카드 초기화, 이 카드의 조작 버튼 |
| 위 | 카드 제목, 무엇을 확인하는지, **기대 결과**, 조작 키 |
| 오른쪽 | 합성 결과(MotionFrame), Body 상태, 활성 효과 표, 카드 전용 값, 입력 결과 기록 |
| 아래 | 자동 점검(PASS/FAIL, 누적 횟수, 마지막 실패) |
| 큐브 | 흰색 화살표는 Body 속도, 초록 화살표는 합성 Planar, 위의 색 글씨는 활성 효과 |

공통 키: `Tab`·`PageUp/Down` 카드 전환, `P` 정지, `N` 한 Tick, `R` 카드 초기화, `[` `]` 속도. 카드 키는 숫자 `1`~`9`, `WASD`, `Space` 등이며 화면 위쪽에 적혀 있습니다. 모든 조작은 왼쪽 버튼으로도 할 수 있습니다.

## 카드

| 카드 | 확인하는 것 | 자동 점검 |
| --- | --- | --- |
| 1. Motion 합성 | 대시·넉백·스턴·둔화·바람·저지불가·회전을 Order로 겹침(스턴은 걷기·대시만 막고 뒤 Order의 외부 힘은 받음), 가려지면 Timer 정지, `ActionID` | Core 연산 순서 접기와 같은 Planar, 스턴 중 외부 힘(넉백·바람)만 남음, 가려짐, `ActionID` |
| 2. Free 채널 | 의도 없는 채널은 중력·관성 유지, 지속 ADD 변화량과 충격, `keepVelocity` | 누적 Impulse 보존 법칙, 조용한 Tick은 Impulse 0 |
| 3. 이동 플랫폼 | 발판 속도를 ADD로 넣고 떠날 때 관성 남김·제거 | 발판 위 수평 속도, 떠난 뒤 속도 |
| 4. 롤백·재실행 | 160 Tick 기록 → 체크포인트 복원 → 같은 입력 재실행, Up·월드 선속도 포함 | 모든 프레임 필드 일치(충격 하나를 빼면 FAIL이 정상) |
| 5. 연산 | ADD·SUB·MUL·DIV·MIN·MAX 성분별 접기 | Core 규칙과 같은 Planar |
| 6. 입력 규칙 | 세대·용량(4)·구조 변경·NaN·미정의 연산·없는 키의 결과 코드 | 사례 17개의 기대 상태 |
| 7. Lease | 사다리 구역에서 `AcquireModifier`로 얻고 나오면 해제 | 구역 안/밖 항목, Lease 두 번 해제 시 종료 1번 |

## 효과 수명

Motion은 수명·감쇠·가려짐 정지를 모릅니다. 이 씬의 효과(`ValidationEffect`)가 사용처 역할을 합니다. 지속 시간은 Core `Timer`로 재고, 가려지면(`TryGetModifier`의 `IsVisible`) Timer를 멈추고, 감쇠는 값을 직접 줄여 같은 세대로 다시 넣습니다.

## 범위

이번 구성은 3D 큐브 기준입니다. Up 변경·벽 접촉(`RemoveBlocked`)·루트 모션 Track·2D·Navigation·Motor 교정은 이후 카드로 추가합니다.
