/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotorGroundSupport.cs
수정일 : 2026-11-25

# 설명
기존 IGroundChecker의 접지와 지면 지점 속도를 Motor Physics 관측 수명에 연결한다.

# 특이사항, 제약사항
발판 속도나 이동 정책을 재계산·적용하지 않는다. Checker Owner 변경이 관측되면 해당 Physics 세대에서 재사용하지 않는다.
Checker Owner는 Unity 파괴 수명을 반영해 한 경로로만 조회한다. 파괴된 Owner는 Owner 없음으로 본다.
Checker 콜백은 Motor 작업 구간 안에서 실행하므로, 콜백이 Correction·Restore·관측 작업을 다시 시작하면 InvalidOperationException으로 거절된다.
Character Controller를 Motor에 잇는 연결 어댑터다. Motion·Motor 본체는 이 어댑터를 참조하지 않으며, Host가 이 관측으로 MotionFeedback을 채운다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Primitive;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Controller;
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Game.Motor;

namespace inonego.Xeri.Game.Motor.Adapter
{
    // ============================================================
    /// <summary>
    /// <br/> 현재 Step에서 관측한 지면 속도입니다.
    /// <br/> 과거 World State나 Support ID는 아닙니다.
    /// </summary>
    // ============================================================
    public readonly struct MotorSupportObservation
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 관측을 소유하는 로컬 Actor입니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionActorKey Actor { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 관측을 완료한 Physics Step Index입니다.
        /// </summary>
        // ------------------------------------------------------------
        public long Step { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 이 Actor에 연결된 GroundChecker가 있었는지 표시합니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasChecker { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 연결된 GroundChecker가 접지를 승인했는지 표시합니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsOnGround { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 지면 속도를 조회한 World Point입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 WorldPoint { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 기존 GroundChecker에서 조회한 지점의 속도입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 GroundPointVelocity { get; }

    #endregion

    #region 생성자

        internal MotorSupportObservation
        (
            MotionActorKey actor,
            long step,
            bool hasChecker,
            bool isOnGround,
            Vector3 worldPoint,
            Vector3 groundPointVelocity
        )
        {
            Actor = actor;
            Step = step;
            HasChecker = hasChecker;
            IsOnGround = isOnGround;
            WorldPoint = worldPoint;
            GroundPointVelocity = groundPointVelocity;
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// <br/> 기존 GroundChecker의 관측을 Motor에 연결합니다.
    /// <br/> 새 Step과 Correction은 이전 표본을 무효화합니다.
    /// </summary>
    // ============================================================
    public sealed class MotorGroundSupport<TState>
    where TState : unmanaged
    {

    #region 필드

        private readonly Motor<TState> motor;
        private readonly IGroundChecker checker;
        private MotorSupportObservation observation;
        private GameObject observationCheckerObject;
        private Revision observationGeneration = Revision.None;
        private Revision invalidatedCheckerGeneration = Revision.None;

        // ------------------------------------------------------------
        /// <summary>
        /// 연결된 GroundChecker의 존재 여부입니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasChecker => ReadCheckerOwner() != null;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Motor에 GroundChecker를 선택적으로 연결합니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotorGroundSupport
        (
            Motor<TState> motor,
            IGroundChecker checker = null
        )
        {
            this.motor = motor ?? throw new ArgumentNullException(nameof(motor));
            this.checker = checker;
        }

    #endregion

    #region 관측

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 실제 Physics Step 뒤 기존 GroundChecker를 검사합니다.
        /// <br/> Host가 지정한 World Point의 속도를 그대로 관측합니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool CheckAndObserve
        (
            long step, float deltaTime, Vector3 worldPoint
        )
        {
            if (step < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(step));
            }

            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            if (!worldPoint.IsFinite())
            {
                throw new ArgumentOutOfRangeException(nameof(worldPoint));
            }

            // Checker 콜백이 Motor를 교정하거나 관측을 다시 요청하지 못하도록 Motor 작업 구간 안에서 관측한다.
            motor.EnterOperation();

            try
            {
                return CheckAndObserveCore(step, deltaTime, worldPoint);
            }
            finally
            {
                motor.ExitOperation();
            }
        }

        private bool CheckAndObserveCore(long step, float deltaTime, Vector3 worldPoint)
        {
            if (motor.ObservedPhysicsStep != step)
            {
                observationGeneration = Revision.None;
                return false;
            }

            var version = motor.PhysicsObservationGeneration;

            // Owner가 달라진 관측 세대는 같은 객체로 돌아와도 재사용하지 않는다.
            if (invalidatedCheckerGeneration == version)
            {
                return false;
            }

            if (observationGeneration == version && observation.Step == step)
            {
                if (!IsObservedCheckerCurrent())
                {
                    invalidatedCheckerGeneration = version;
                    return false;
                }

                return observation.WorldPoint == worldPoint;
            }

            observationGeneration = Revision.None;
            var checkerObject = ReadCheckerOwner();
            var hasChecker = checkerObject != null;
            var onGround = false;
            var pointVelocity = Vector3.zero;

            if (hasChecker)
            {
                checker.Check(deltaTime);
                onGround = checker.IsOnGround;

                if (onGround)
                {
                    pointVelocity = checker.GetGroundPointVelocity(worldPoint);
                }
            }

            // Checker 실행 중 Owner가 파괴·교체됐다면 이 Physics 세대의 표본을 확정하지 않는다.
            if (!IsCheckerOwner(checkerObject))
            {
                invalidatedCheckerGeneration = version;
                return false;
            }

            observation = new MotorSupportObservation
            (
                motor.Actor, step, hasChecker,
                onGround, worldPoint, pointVelocity
            );
            observationCheckerObject = checkerObject;
            observationGeneration = version;
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 마지막으로 검사한 지면 상태가 유효하면 반환합니다.
        /// <br/> Groundless Actor는 HasChecker=false인 정상 표본입니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryRead(out MotorSupportObservation sample)
        {
            sample = default;

            if (observationGeneration.IsNone ||
                observationGeneration != motor.PhysicsObservationGeneration ||
                motor.ObservedPhysicsStep != observation.Step ||
                invalidatedCheckerGeneration == observationGeneration)
            {
                return false;
            }

            // Owner 조회도 Checker 콜백이므로 CheckAndObserve와 같은 Motor 작업 구간에서 실행한다.
            motor.EnterOperation();

            try
            {
                if (!IsObservedCheckerCurrent())
                {
                    invalidatedCheckerGeneration = observationGeneration;
                    return false;
                }
            }
            finally
            {
                motor.ExitOperation();
            }

            sample = observation;
            return true;
        }

        private bool IsObservedCheckerCurrent() => IsCheckerOwner(observationCheckerObject);

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 현재 Checker Owner가 기대한 객체와 같은지 검사합니다.
        /// <br/> 둘 다 null이면 Groundless 상태가 유지된 것입니다.
        /// </summary>
        // ------------------------------------------------------------
        private bool IsCheckerOwner(GameObject expected) => ReferenceEquals(ReadCheckerOwner(), expected);

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Unity 파괴 수명을 반영해 Checker Owner를 조회합니다.
        /// <br/> 파괴된 Owner는 C# null로 정규화합니다.
        /// </summary>
        // ------------------------------------------------------------
        private GameObject ReadCheckerOwner()
        {
            var owner = checker?.GameObject;

            // Checker가 파괴된 Owner 참조를 계속 반환해도 같은 객체로 비교되지 않도록 C# null로 바꾼다.
            return owner != null ? owner : null;
        }

    #endregion

    }
}