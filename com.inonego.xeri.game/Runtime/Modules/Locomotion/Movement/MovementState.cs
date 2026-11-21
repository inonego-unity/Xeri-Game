/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MovementState.cs
수정일 : 2026-11-22

# 설명
애니메이션·원격 표시·Replay가 읽는 Actor의 이동 상태 값을 정의한다.

# 특이사항, 제약사항
Model 없이 호출할 수 있는 순수 계산이다. 원격 Actor는 복제된 Body 상태와 동작 값으로 같은 결과를 재구성한다.
복제된 값이 들어오므로 Create는 범위 밖 Tick·동작 시작 Tick, 미정의 불연속 종류, 단위가 아닌 Up, 유한하지 않거나 영인 회전, 유한하지 않거나 오버플로하는 속도를 거절한다.
Animator·Presentation·Entity 타입을 참조하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Primitive;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Game.Motor;

namespace inonego.Xeri.Game.Movement
{
    // ============================================================
    /// <summary>
    /// <br/> 표현·복제가 읽는 Actor 한 Tick의 이동 상태.
    /// <br/> 같은 입력 값이면 서버와 원격에서 같은 값이 된다.
    /// </summary>
    // ============================================================
    public readonly struct MovementState
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 상태의 Actor.
        /// </summary>
        // ------------------------------------------------------------
        public MotionActorKey Actor { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 상태의 Tick 번호.
        /// </summary>
        // ------------------------------------------------------------
        public long TickIndex { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 월드 선속도.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 LinearVelocity { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Body 회전 기준 선속도(X 오른쪽, Y 위, Z 전방).
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 LocalVelocity { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Up 축에 수직인 속력.
        /// </summary>
        // ------------------------------------------------------------
        public float PlanarSpeed { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Up 축 방향 속도.
        /// </summary>
        // ------------------------------------------------------------
        public float VerticalSpeed { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 지면에 닿아 있는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsGrounded { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Frame에 실린 보이는 맨 위 SET 항목의 동작 식별자. 없으면 0.
        /// </summary>
        // ------------------------------------------------------------
        public int ActionID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 동작이 시작한 Tick. 없으면 -1.
        /// </summary>
        // ------------------------------------------------------------
        public long ActionStartTick { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 동작 시작 후 지난 Tick 수. 동작이 없으면 0.
        /// </summary>
        // ------------------------------------------------------------
        public long ActionElapsedTicks => ActionStartTick >= 0 ? TickIndex - ActionStartTick : 0;

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막 Body 불연속 종류. 표현 측 스냅·스무딩 판단에 쓴다.
        /// </summary>
        // ------------------------------------------------------------
        public MotorDiscontinuityKind LastDiscontinuity { get; }

    #endregion

    #region 생성

        private MovementState
        (
            MotionActorKey actor, long tickIndex, Vector3 linearVelocity, Vector3 localVelocity,
            float planarSpeed, float verticalSpeed, bool isGrounded,
            int actionID, long actionStartTick,
            MotorDiscontinuityKind lastDiscontinuity
        )
        {
            Actor = actor;
            TickIndex = tickIndex;
            LinearVelocity = linearVelocity;
            LocalVelocity = localVelocity;
            PlanarSpeed = planarSpeed;
            VerticalSpeed = verticalSpeed;
            IsGrounded = isGrounded;
            ActionID = actionID;
            ActionStartTick = actionStartTick;
            LastDiscontinuity = lastDiscontinuity;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Body 선속도·회전과 동작 값으로 이동 상태를 계산한다.
        /// <br/> 원격 Actor는 복제된 값으로 이 메서드를 직접 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        public static MovementState Create
        (
            MotionActorKey actor, long tickIndex, Vector3 up,
            Vector3 linearVelocity, Quaternion rotation, bool isGrounded,
            int actionID, long actionStartTick,
            MotorDiscontinuityKind lastDiscontinuity
        )
        {
            // 복제된 값도 들어오는 공개 진입점이므로 범위를 먼저 검사한다.
            if (actor.ScopeID <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(actor));
            }

            if (tickIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tickIndex));
            }

            if (actionStartTick < -1 || actionStartTick > tickIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(actionStartTick));
            }

            if (!IsDefinedDiscontinuity(lastDiscontinuity))
            {
                throw new ArgumentOutOfRangeException(nameof(lastDiscontinuity));
            }

            if (!up.TryNormalize(out var unitUp))
            {
                throw new ArgumentOutOfRangeException(nameof(up));
            }

            if (!linearVelocity.IsFinite())
            {
                throw new ArgumentOutOfRangeException(nameof(linearVelocity));
            }

            if (!rotation.TryNormalize(out var unitRotation))
            {
                throw new ArgumentOutOfRangeException(nameof(rotation));
            }

            // 속력은 Up 축으로 나누고, 로컬 속도는 Body 회전의 역으로 구한다.
            var vertical = Vector3.Dot(linearVelocity, unitUp);
            var planarSpeed = (linearVelocity - unitUp * vertical).magnitude;

            // 속도 크기 제곱이 float 범위를 넘으면 속력이 유한하지 않으므로 거절한다.
            if (!planarSpeed.IsFinite())
            {
                throw new ArgumentOutOfRangeException(nameof(linearVelocity));
            }

            var local = Quaternion.Inverse(unitRotation) * linearVelocity;

            if (!local.IsFinite())
            {
                throw new ArgumentOutOfRangeException(nameof(linearVelocity));
            }

            return new MovementState
            (
                actor, tickIndex, linearVelocity, local, planarSpeed, vertical, isGrounded,
                actionID, actionStartTick, lastDiscontinuity
            );
        }

        private static bool IsDefinedDiscontinuity(MotorDiscontinuityKind kind) =>
            kind >= MotorDiscontinuityKind.None && kind <= MotorDiscontinuityKind.HistoricalRestore;

    #endregion

    }
}
