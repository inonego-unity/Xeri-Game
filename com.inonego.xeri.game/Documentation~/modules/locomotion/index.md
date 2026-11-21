# Locomotion

Actor 한 명의 이동을 운동 합성, Body 적용, 경로 안내, 이동 상태 표현으로 나눈 계약입니다.

- [Locomotion 전체 구조](overview.md)
- [Motion](motion.md) — 여러 시스템의 운동 기여를 네 층으로 합성
- [Motor](motor.md) — 합성 결과를 Body에 적용하고 접촉·지면을 관측
- [Navigation](navigation.md) — 목표와 경로의 수명, 다음 Waypoint 안내
- [Movement](movement.md) — 표현·복제가 읽는 이동 상태와 Step Sample

[Tick 실행 가이드](../../guides/locomotion/run-a-tick.md)에서 한 Tick의 호출 순서를 확인할 수 있습니다.

지면 감지와 점프 상태는 [Game Controller](../character/controller.md)가 제공하고, Locomotion은 그 결과를 관측으로 연결하는 어댑터만 둡니다.
