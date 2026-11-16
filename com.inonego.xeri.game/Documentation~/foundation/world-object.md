# WorldObject와 지속 상태

World 영역은 **논리적 World에 남아야 하는 데이터**와 **Unity Scene에 존재하는 표현 객체**를 분리합니다. `WorldObjectStateStore`가 상태 참조를 보관하고, `WorldObjectSceneBinding`이 Scene 수명 동안 `IWorldObject`에 그 상태를 연결합니다. Scene을 내릴 때 Store는 유지할 수 있습니다.

## 구성 요소

| 요소 | 역할 | 소유 범위 |
|---|---|---|
| `WorldObjectState` | 게임이 정의한 지속 데이터의 추상 기반 타입 | ID 및 구체 상태 필드 |
| `WorldObjectStateStore` | 상태 등록·조회·교체·삭제 | 하나의 논리 World Scope |
| `IWorldObject` | 상태 데이터를 사용하는 Scene 표현 객체 | 해당 Scene 객체의 연결 참조 |
| `WorldObjectSceneBinding` | 여러 Scene 객체의 일괄 Bind·Unbind 및 ID 예약 | 하나의 Scene 연결 수명 |
| `IWorldObjectCurrentStateReceiver` | 외부에서 확정된 현재 상태를 검증하고 적용하는 선택적 계약 | 해당 Scene 객체의 현재 표현 |
| `WorldObjectBindingStatus` | 연결·정리 상태 표시 | Binding 인스턴스 |

`WorldObjectState`의 State는 데이터이고, [State Machine](state-machine.md)의 `IState`는 행동 로직입니다. 두 시스템은 자동으로 연결되지 않습니다.

## WorldObjectState: 유지할 데이터

`WorldObjectState`는 `[Serializable]` 기반의 추상 타입이며 `ID`를 제공합니다. 문 열림, 상자 소모 여부처럼 구체적인 필드와 규칙은 게임이 파생 클래스에 정의합니다. Scene `GameObject`, `MonoBehaviour`, 이펙트 또는 애니메이션 재생 수명은 상태 객체의 책임이 아닙니다.

ID는 같은 Store 안에서 객체와 상태를 대응시키는 **대소문자 구분 문자열**입니다. 비어 있거나 공백뿐인 ID는 허용하지 않습니다. Store에 등록된 동안에는 `WorldObjectState.ID`를 변경하지 않아야 합니다. Entity Key나 네트워크 ID와 자동 대응하지 않습니다.

## WorldObjectStateStore: 논리 World의 상태 소유자

| API | 동작 |
|---|---|
| `new WorldObjectStateStore(initialStates)` | 초기 상태의 ID 유효성과 중복을 검사해 참조를 인수; 인수 생략 가능 |
| `States` | 등록된 **가변 Live State 객체**의 읽기 전용 컬렉션 뷰 |
| `TryGetState(id, out state)` | ID로 현재 상태 참조 조회; 잘못된 ID는 `false` |
| `AddState(state)` | 신규 ID 등록; 중복·예약 중 ID 거절 |
| `ReplaceState(state)` | 등록되어 있고 Scene 연결 중이지 않은 ID의 상태 참조 교체 |
| `RemoveState(id)` | Scene 연결 중이지 않은 ID 제거; 없는 ID는 `false` |

Store마다 ID 공간이 독립적입니다. 같은 ID를 다른 Store에 둘 수 있지만, **같은 Store의 한 ID에는 동시에 하나의 활성 Scene 연결만** 허용합니다. 연결 중인 ID는 Store에서 직접 교체하거나 제거할 수 없습니다. 삭제가 게임 세계에서 무엇을 뜻하는지는 Domain이 결정합니다.

`States`는 저장 파일이나 네트워크 전송용 Snapshot이 아닙니다. 상태 필드를 직접 수정하면 해당 Store에 보관된 객체가 즉시 변경됩니다. 변경 이력, 트랜잭션, 복제본 생성은 제공하지 않습니다.

상태를 캡처해야 하는 소비처는 `WorldObjectState` 파생 타입을 `[Serializable]` 데이터로 정의하고, 안전한 시점에 `States`를 읽어 직접 복제하거나 직렬화합니다. 캡처 형식과 시점은 Foundation이 정하지 않습니다.

## IWorldObject: Scene 표현과 상태 참조

| 멤버 | 구현 책임 |
|---|---|
| `ID` | 연결 대상의 안정 ID 반환 |
| `BindState(WorldObjectState state)` | 전달된 상태를 현재 Scene 객체에 연결 |
| `UnbindState()` | 상태 참조와 Scene 쪽 연결 정리 |

`BindState`와 `UnbindState`는 **연결 콜백**입니다. 이곳에서 보상 지급, 이벤트 재실행, 지속 데이터 값 변경을 하지 않아야 합니다. 특히 `UnbindState()`는 부분 연결 후 호출될 수 있고 실패 시 다시 호출될 수 있으므로 멱등적으로 구현합니다. `IWorldObject`에 상태 생성 메서드는 필수로 요구되지 않습니다.

## WorldObjectSceneBinding: Scene 연결 수명

`new WorldObjectSceneBinding(store)`는 전달받은 Store를 참조하지만 Store의 수명을 소유하지 않습니다. 하나의 Binding은 한 번에 하나의 Scene 연결을 관리하며, 다중 Scene은 같은 Store를 참조하는 별도 Binding으로 조립합니다. Scene 객체의 수집, Scene 로드 및 언로드는 호출자의 책임입니다.

### Bind

`Bind(IEnumerable<IWorldObject> objects, Func<IWorldObject, WorldObjectState> createState = null)`의 순서는 다음과 같습니다.

1. Scene 객체의 null·파괴 여부, ID 유효성 및 입력 내 중복을 검증합니다.
2. Store에서 ID별 상태를 조회합니다. 누락된 경우 Factory가 있으면 새 상태를 준비하고, 없으면 오류를 발생시킵니다.
3. 준비된 상태의 ID가 Scene 객체의 ID와 일치하는지 검사하고 모든 ID를 독점 예약합니다.
4. 객체별 `BindState`가 모두 성공하면 새 상태를 Store에 확정하고 `Bound`가 됩니다.

Factory는 **누락된 상태의 생성만** 담당합니다. 복원된 기존 상태가 있으면 Factory를 호출하지 않습니다. Factory에는 외부 효과나 기존 Store 상태 변경을 넣지 않습니다. 연결 완료 전 실패하면 새로 준비한 상태는 Store에 등록되지 않습니다.

### Unbind와 상태

| `Status` | 의미 | 가능한 다음 작업 |
|---|---|---|
| `Unbound` | 활성 연결과 미완료 정리가 없음 | `Bind` 가능; `Unbind`는 무동작 |
| `Bound` | Scene 객체가 상태에 연결됨 | `ApplyCurrentState`, `Unbind` 가능 |
| `CleanupPending` | 연결·적용·해제 실패로 정리가 필요함 | `Unbind()`로 남은 연결 정리 |

`Unbind()`는 연결 객체를 역순으로 해제하고 성공한 ID 예약을 반환합니다. Unity에서 이미 파괴된 객체는 콜백 없이 정리합니다. 일부 `UnbindState()`가 실패하면 실패한 대상만 보존하고 `CleanupPending`으로 전환합니다. 그 대상의 예약은 유지되며 `Unbind()` 재시도로 해제해야 합니다. 실패한 Bind 중에도 실제로 시도한 객체를 역순 정리합니다.

대상 Scene 객체가 0개이면 `Bind()`는 아무것도 예약하지 않으며 `Unbound` 상태를 유지합니다. 연결 중 재진입 Bind/Unbind, 아직 정리하지 않은 Binding의 재사용은 허용하지 않습니다.

## 외부 확정 현재 상태 적용

`IWorldObjectCurrentStateReceiver`를 구현한 Scene 객체에 한해 `WorldObjectSceneBinding.ApplyCurrentState(next)`를 사용할 수 있습니다.

1. `Bound` 여부, ID에 대응하는 활성 객체, Receiver 지원 여부를 확인합니다.
2. `ValidateCurrentState(next)`로 타입·데이터·적용 가능성을 검사합니다. 이 단계는 상태를 변경하지 않아야 합니다.
3. `ApplyCurrentState(next)`가 현재 화면과 객체 내부 상태를 반영합니다. 과거 게임플레이 효과를 재실행하지 않습니다.
4. 적용 성공 후에만 Store의 기존 State 참조를 `next`로 교체합니다.

검증 단계에서 실패하면 기존 연결과 Store 상태를 유지합니다. **적용 도중 실패하면 객체에 부분 변경이 남을 수 있으므로**, Binding은 `CleanupPending`으로 바뀌고 Store에는 실패한 새 참조를 확정하지 않습니다. 호출자는 `Unbind()` 후 새 Scene 객체를 만들어 Store의 현재 상태로 재구성합니다.

이는 이미 확정된 **현재 상태를 적용하는 API**입니다. Authority 판정, 네트워크 패킷 검증, 순서 관리, Snapshot 생성, Replay 시간 제어는 포함하지 않습니다.

현재 상태가 도착하는 시점에 따라 경로가 나뉩니다. `Bind` 전에 도착했다면 Store에 먼저 적재하고 `Bind`로 연결해 초기 구성을 마칩니다. 이미 `Bound`인 뒤에 도착했다면 `ApplyCurrentState`로 적용합니다. 연결되지 않은 ID의 상태는 적용할 수 없으므로, 대상 객체가 아직 없을 때의 보관 정책은 Host가 정합니다.

오래된 상태를 걸러내는 기준(Revision, Epoch 등)은 파생 `WorldObjectState`의 필드로 두고 Receiver의 `ValidateCurrentState`에서 비교합니다. 검증에서 거절하면 기존 연결과 Store 상태가 유지됩니다.

## Scene 전환 조립 예시

다음 변수는 게임이 준비한 입력입니다: `restoredStates`는 `IEnumerable<WorldObjectState>`, `sceneObjects`와 `nextSceneObjects`는 `IEnumerable<IWorldObject>`입니다.

```csharp
var store = new WorldObjectStateStore(restoredStates);
var sceneBinding = new WorldObjectSceneBinding(store);

// 복원된 상태만 연결한다. 누락된 ID가 있으면 오류다.
sceneBinding.Bind(sceneObjects);

// Scene 언로드 전에 연결을 종료한다. 상태는 Store에 남는다.
sceneBinding.Unbind();

// 다음 Scene에 생성된 객체에 동일한 상태를 다시 연결한다.
sceneBinding.Bind(nextSceneObjects);
```

최초 상태를 게임이 생성한다면 `Bind(sceneObjects, obj => new DoorState(obj.ID))` 형태로 Factory를 전달할 수 있습니다. `DoorState`는 게임이 정의한 `WorldObjectState` 파생 타입입니다. 구체적인 Scene 구현과 현재 상태 적용 예시는 [WorldObject 구성 가이드](../guides/foundation/create-world-object.md)를 참고합니다.

## Host·Save·State Machine과의 경계

- **World Store:** 논리 World의 현재 상태 참조를 보유합니다.
- **Scene Binding:** Scene 객체와 상태 참조를 연결·해제합니다. 상태와 Entity 자체를 삭제하지 않습니다.
- **State Machine:** 현재 행동의 `OnEnter`·`OnExit`·Tick을 수행합니다. World 데이터를 저장하지 않습니다.
- **게임 Host/Composition:** Scene 로드·언로드, Binding 생성 순서, 저장 데이터 복원, FSM 초기 상태를 결정합니다.
- **게임 Serializer/Network:** 별도 독립 Snapshot 생성, 상태 복사, 동기화 권한 및 실행 스레드 전달을 담당합니다. Store와 Binding은 스레드 안전 API가 아닙니다.

예를 들어 문 열기 동작은 FSM에서 수행하고 `WorldObjectState`에는 열린 **결과**를 기록합니다. Scene 재방문 시 Store의 결과를 새 문에 연결하며, 과거 열기 동작을 다시 재생하지 않습니다.

관련 문서: [Foundation](index.md) · [State Machine](state-machine.md) · [Entity와 Spawn](entity-and-spawn.md) · [Actor의 독립 수명·조립](../guides/foundation/compose-entity-actor-runtime.md)
