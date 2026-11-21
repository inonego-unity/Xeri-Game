/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MovementFrameSample.cs
수정일 : 2026-11-21

# 설명
한 Tick의 합성 결과·Body 현재 상태·이동 상태를 읽기 전용 Step Sample 값으로 묶는다.

# 특이사항, 제약사항
Confirmed 판정은 외부 Authority 소유다. Physics, Network, Recorder, 이벤트 확정은 수행하지 않는다.
캡처는 Motor 작업 구간에서 Body를 읽고, 그 뒤 Motion·Motor 수명을 한 번만 검증한다.
Motion Tick Index와 Motor.CompletePhysicsStep에 넘긴 Step 번호가 같아야 한다.
값 형식이므로 보관·복사에 할당이 없다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Game.Motor;

namespace inonego.Xeri.Game.Movement
{
    // ============================================================
    /// <summary>
    /// Host가 공개 Sample에 부여한 확정 여부입니다.
    /// </summary>
    // ============================================================
    public enum MovementSampleConfirmation
    {
        Unconfirmed,
        Confirmed
    }

    // ============================================================
    /// <summary>
    /// <br/> 합성 결과와 현재 Body State의 독립 Step Sample입니다.
    /// <br/> 외부 Recorder는 이 값을 읽고 보관·직렬화할 수 있습니다.
    /// </summary>
    // ============================================================
    public readonly struct MovementFrameSample<TState>
    where TState : unmanaged, IMotorKinematicState
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 결과를 소유하는 로컬 실행 Actor입니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionActorKey Actor => Frame.Actor;

        // ------------------------------------------------------------
        /// <summary>
        /// 결과의 Tick과 Delta입니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionTick Tick => Frame.Tick;

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 Host가 선언한 결과의 확정 상태입니다.
        /// </summary>
        // ------------------------------------------------------------
        public MovementSampleConfirmation Confirmation { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 해당 Tick의 합성 결과입니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotionFrame Frame { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Physics Step 뒤 Body의 현재 상태입니다.
        /// </summary>
        // ------------------------------------------------------------
        public TState CurrentState { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 캡처 당시 Motor 불연속 번호입니다.
        /// </summary>
        // ------------------------------------------------------------
        public long DiscontinuityVersion { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 표현·복제가 읽는 이동 상태입니다.
        /// </summary>
        // ------------------------------------------------------------
        public MovementState State { get; }

        private MovementFrameSample
        (
            MovementSampleConfirmation confirmation, in MotionFrame frame,
            in TState currentState, long discontinuityVersion, in MovementState state
        )
        {
            Confirmation = confirmation;
            Frame = frame;
            CurrentState = currentState;
            DiscontinuityVersion = discontinuityVersion;
            State = state;
        }

    #endregion

    #region 확정 분류

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 보관 중인 과거 Sample의 확정 상태를 새 값으로 지정합니다.
        /// <br/> Motor·Physics를 다시 읽거나 실행하지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        public MovementFrameSample<TState> WithConfirmation(MovementSampleConfirmation confirmation)
        {
            ValidateConfirmation(confirmation);
            return new MovementFrameSample<TState>(confirmation, Frame, CurrentState, DiscontinuityVersion, State);
        }

        private static void ValidateConfirmation(MovementSampleConfirmation confirmation)
        {
            if (confirmation != MovementSampleConfirmation.Unconfirmed &&
                confirmation != MovementSampleConfirmation.Confirmed)
            {
                throw new ArgumentOutOfRangeException(nameof(confirmation));
            }
        }

    #endregion

    #region 캡처

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Model의 마지막 합성 결과와 Motor 현재 상태를 묶습니다.
        /// <br/> Tick Index와 같은 번호로 완료한 Physics Step에서만 생성합니다.
        /// </summary>
        // ----------------------------------------------------------------------
        public static MovementFrameSample<TState> Capture
        (
            MotionModel model, Motor<TState> motor, in MotionFrame frame,
            bool isGrounded, MovementSampleConfirmation confirmation
        )
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            if (motor == null)
            {
                throw new ArgumentNullException(nameof(motor));
            }

            if (!model.Actor.Equals(motor.Actor))
            {
                throw new InvalidOperationException("Motion과 Motor의 Actor 수명이 일치하지 않습니다.");
            }

            ValidateConfirmation(confirmation);

            // Body 조회 콜백이 Motor를 교정하지 못하도록 Motor 작업 구간에서 캡처한다.
            motor.EnterOperation();

            try
            {
                var state = motor.ReadCurrentState();

                // Model은 작업 구간 밖이므로 Body 조회가 끝난 뒤 Motion·Motor 수명을 한 번에 검증한다.
                if (!frame.Actor.Equals(model.Actor) ||
                    frame.Tick.Index != model.LastTick || frame.StepVersion != model.StepVersion)
                {
                    throw new InvalidOperationException("Model의 마지막 Step 결과만 캡처할 수 있습니다.");
                }

                if (frame.Tick.Index != motor.ObservedPhysicsStep)
                {
                    throw new InvalidOperationException
                    (
                        "Motor가 관측한 Physics Step이 Motion Tick과 다릅니다. CompletePhysicsStep에 같은 Tick Index를 넘겨야 합니다."
                    );
                }

                var movement = MovementState.Create
                (
                    frame.Actor, frame.Tick.Index, frame.Up,
                    state.WorldLinearVelocity, state.WorldRotation, isGrounded,
                    frame.ActionID, frame.ActionStartTick, frame.BlockedChannels,
                    motor.LastDiscontinuity
                );

                return new MovementFrameSample<TState>(confirmation, frame, state, motor.DiscontinuityVersion, movement);
            }
            finally
            {
                motor.ExitOperation();
            }
        }

    #endregion

    }
}
