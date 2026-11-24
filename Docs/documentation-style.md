# Xeri Game 문서 작성 규칙

이 문서는 Xeri-Game 저장소의 공개 README, API 개요, 개념 문서와 사용 가이드의 작성 기준을 정의한다.
문서의 표시 제목과 본문은 한글을 기본으로 하며, 코드 식별자와 고유 기술 용어는 원래 표기를 유지한다.

## 문서 종류

Xeri Game 공개 문서는 다음 세 종류로 구분한다.

- **모듈 개요**: 모듈의 목적, 책임 경계, 핵심 개념, 구조를 설명한다.
- **사용 가이드**: 특정 작업을 실제로 수행하는 절차를 설명한다.
- **개념 문서**: 여러 모듈에 공통으로 적용되는 설계 개념과 규칙을 설명한다.

공개 모듈 개요와 사용 가이드는 `Documentation~`에서 관리한다.
`Runtime/`은 구현을 소유하며, 개별 모듈에 별도 README를 중복 생성하지 않는다.
내부 구현 규칙과 테스트 경로는 공개 Manual이 아닌 개발용 자료에 둔다.

## 문서 위치

- `Docs/`: 문서 작성 규칙, 템플릿, DocFX 운영 자료를 둔다.
- [`Docs/site-maintenance.md`](site-maintenance.md): DocFX build, API snapshot과 GitHub Pages 운영 절차를 둔다.
- `Docs/templates/`: 새 문서를 작성할 때 기준으로 사용할 템플릿을 둔다.
- `com.inonego.xeri.game/Documentation~/`: 공개 Manual 문서의 source of truth를 둔다.
- `com.inonego.xeri.game/Runtime/`: 실제 구현과 계약을 확인하는 근거이다. 공개 문서의 별도 사본을 두지 않는다.

## 제목과 용어

- 이 패키지의 제품명은 `Xeri Game`으로 표기한다. 공통 기반 패키지는 `Xeri Core`로 구분한다.
- 문서 제목은 `# Xeri Game Ability`처럼 이 패키지에서 다루는 대상이 드러나게 작성한다.
- 섹션 제목은 `개요`, `책임 범위`, `핵심 개념`, `구조`, `기본 사용`처럼 한글을 우선한다.
- `Runtime`, `Context`, `Lease`, `Handle`, `Screen`처럼 코드와 직접 대응하는 용어는 억지로 번역하지 않는다.
- public 타입, 멤버, 파일명은 백틱으로 감싼다. 예: `EntitySpawnRegistry`, `TryUse()`.
- 같은 개념에 서로 다른 한글·영문 표현을 혼용하지 않는다.

## 내용 작성 원칙

- 폴더와 파일 목록보다 모듈이 제공하는 계약과 책임을 먼저 설명한다.
- 각 주요 시스템 문서는 최소한 `왜 필요한가`, `언제 사용하는가`, `기본 사용`에 답해야 한다.
- 타입 이름을 설명하기 전에 사용자가 해결하려는 문제와 선택 기준을 먼저 제시한다.
- 코드가 실제로 보장하지 않는 사용법이나 미래 계획을 현재 기능처럼 서술하지 않는다.
- API 멤버 전체를 README에 복제하지 않는다. 상세 멤버 목록은 API Reference에 맡긴다.
- 예제는 컴파일 가능한 방향의 최소 코드와 호출 순서를 보여주고, 프로젝트 고유 도메인명은 사용하지 않는다.
- 소유권과 종료 순서가 중요한 기능은 누가 획득하고 누가 종료하는지 명시한다.
- 부분 생성이나 callback 실패가 중요한 시스템은 rollback과 실패 후 상태를 함께 설명한다.
- 확장 지점이 있는 경우 새 타입을 만들기 전에 사용할 기존 계약을 먼저 제시한다.
- 잘못된 사용을 방지할 가치가 있으면 `제약과 주의사항`에 기록한다.

## 실제 사용 사례 참고 기준

실제 소비 프로젝트는 Xeri API가 현장에서 어떻게 조합되는지 확인하는 근거로 사용할 수 있다. 다만 소비 프로젝트의 구조를 Xeri의 필수 구조처럼 문서화하지 않는다.

- 프로젝트 Adapter/Service/Presenter가 Xeri 계약을 감싸는 패턴은 범용 예제로 일반화할 수 있다.
- 프로젝트 고유 데이터 타입, 화면 이름, 게임 규칙, Addressables label, 입력 Action 이름은 예제에 그대로 복사하지 않는다.
- 실제 프로젝트가 특정 Source, Scanner, Presenter, Registry를 두더라도 Xeri가 그 구체 구현을 요구하는 것처럼 서술하지 않는다.
- Xeri가 보장하는 계약과 소비 프로젝트가 선택한 정책을 문단이나 표에서 명확히 구분한다.
- 실사용에서 확인된 `Acquire/Release`, `Bind/Unbind`, `Register/Unregister`, `Load/Publish` 대칭은 해당 Xeri 계약의 수명 설명에 활용한다.
- 실제 프로젝트의 성공 사례보다 Xeri Game public API와 현재 구현을 최종 사실 근거로 우선한다.

## 문서 분할 기준

- 하나의 README가 여러 독립 작업 절차를 길게 포함하면 사용 가이드로 분리한다.
- 사용자 사용법과 내부 구현 규칙이 섞이면 공개 문서에서는 내부 구현 규칙을 제외한다.
- 테스트 파일 위치와 수동 검증 절차는 일반 사용자 개요에서 분리한다.
- 같은 설명을 여러 README에 복제하지 않고 공통 개념 문서로 연결한다.

## 파일명과 링크

- 문서에 표시되는 제목과 섹션은 한글을 우선한다.
- `README.md` 외의 공개 문서 파일명은 URL과 도구 호환성을 위해 영문 kebab-case를 기본으로 한다.
- 코드 식별자나 실제 파일 경로를 번역해서 링크하지 않는다.
- 같은 저장소 안의 문서는 가능한 한 상대 링크를 사용한다.
- 문서를 이동하거나 분할한 뒤에는 저장소 전체 Markdown 상대 링크를 검사한다.

## 공개 목차

- `com.inonego.xeri.game/Documentation~/toc.yml`은 공개 Manual/Wiki의 상위 탐색 구조를 정의한다.
- Foundation·Modules 개요는 `Documentation~`에서 관리하며, 구현은 `Runtime/Foundation`과 `Runtime/Modules`를 기준으로 검토한다.

## API Reference

- public API 멤버 설명은 C# XML documentation과 생성된 DocFX API Reference를 기준으로 한다.
- Manual 문서는 타입과 멤버를 전부 복제하지 않고 사용 목적, 계약, 흐름과 주의사항을 설명한다.
- DocFX 사이트는 `Documentation~`의 Markdown을 사용하며, 동일한 내용을 Runtime 아래에 복제하지 않는다.
