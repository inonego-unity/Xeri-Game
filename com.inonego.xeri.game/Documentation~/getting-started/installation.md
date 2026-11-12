# Xeri Game 설치와 시작

## 패키지 의존성

Xeri Game의 UPM 패키지명은 `com.inonego.xeri.game`, 기본 어셈블리는 `inonego.Xeri.Game`입니다. 기본 Runtime은 Xeri Core의 `inonego.Xeri`만 참조합니다.

사용 중인 Xeri Core와 Xeri Game의 패키지 버전을 맞추어 Unity Package Manager에 설치합니다.

~~~json
{
  "dependencies": {
    "com.inonego.xeri": "https://github.com/inonego-unity/Xeri.git?path=/com.inonego.xeri#main",
    "com.inonego.xeri.game": "https://github.com/inonego-unity/Xeri-Game.git?path=/com.inonego.xeri.game#main"
  }
}
~~~

각 저장소의 `main`이 대응하는 패키지를 제공할 때의 예시입니다. 로컬 개발에서는 서로 맞는 Core·Game 작업 사본을 `file:` 의존성으로 지정합니다.

## 네임스페이스

~~~csharp
using inonego.Xeri.Game;
~~~

Character Controller의 일부 구현은 `inonego.Xeri.Game.Controller`를 사용합니다. `Foundation`·`Modules`는 C# 네임스페이스가 아닌 폴더 분류명입니다.

## 다음 작업

- [Entity Registry 구현](../guides/foundation/create-entity-registry.md)
- [Ability·Execution·Delivery 조립](../guides/action/ability-execution-delivery.md)
- [Use와 Reaction 연결](../guides/interaction/connect-use-and-reaction.md)

UI·Fabric은 필요할 때 별도로 연결합니다. 기본 Game Runtime은 특정 Input/UI/Fabric/Network Provider를 요구하지 않습니다.
