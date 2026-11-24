/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotorContactReader.cs
수정일 : 2026-11-21

# 설명
Motor의 Physics 관측 수명에 맞춰 Backend의 실제 Contact를 정해진 형식으로 읽는다.
읽기 결과 상태(MotorContactReadStatus) 열거형을 함께 둔다.

# 특이사항, 제약사항
Contact 형식은 생성할 때 Motor Backend와 대조하며, 맞지 않으면 생성을 거절한다.
Contact 표본의 시작과 무효화는 Motor의 Physics Step·Correction·Restore가 맡는다.
복사는 Motor 작업 구간 안에서 실행하므로, 복사 콜백이 수명 작업을 다시 시작하면 InvalidOperationException으로 거절된다.
========================================================================= BLOCK_HEADER_END */

using System;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;

namespace inonego.Xeri.Game.Motor
{
    // ============================================================
    /// <summary>
    /// Physics Step 완료 후 Contact 읽기 상태입니다.
    /// </summary>
    // ============================================================
    public enum MotorContactReadStatus
    {
        Ready,
        Unsupported,
        NotObserved
    }

    // ============================================================
    /// <summary>
    /// <br/> 한 Motor의 실제 Contact를 정해진 형식으로 읽습니다.
    /// <br/> 완료된 Physics Step의 표본만 Ready로 반환합니다.
    /// </summary>
    // ============================================================
    public sealed class MotorContactReader<TState, TContact>
    where TState : unmanaged
    where TContact : struct
    {

    #region 필드

        private readonly Motor<TState> motor;
        private readonly IMotorContactBackend<TContact> backend;

        // ------------------------------------------------------------
        /// <summary>
        /// Backend가 현재 실제 Contact 관측을 제공하는지 나타냅니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsSupported => backend.SupportsContactObservation;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Motor Backend가 제공하는 Contact 형식으로 연결합니다.
        /// <br/> 형식이 맞지 않으면 ArgumentException입니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotorContactReader(Motor<TState> motor)
        {
            this.motor = motor ?? throw new ArgumentNullException(nameof(motor));

            if (motor.Backend is not IMotorContactBackend<TContact> contacts)
            {
                throw new ArgumentException("Motor Backend가 이 Contact 형식을 제공하지 않습니다.", nameof(motor));
            }

            backend = contacts;
        }

    #endregion

    #region 읽기

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 마지막 Physics Step의 실제 접촉을 복사합니다.
        /// <br/> 미지원과 Correction 직후의 미관측 상태를 구분합니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotorContactReadStatus Read(TContact[] results, out int count)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            count = 0;
            motor.EnterOperation();

            try
            {
                if (!backend.SupportsContactObservation)
                {
                    return MotorContactReadStatus.Unsupported;
                }

                // 새 Physics Step 없이 센서가 교체·재활성화됐다면 빈 접촉으로 오인하지 않는다.
                if (!motor.IsContactObservationCurrent())
                {
                    return MotorContactReadStatus.NotObserved;
                }

                var copied = backend.ReadContacts(results);

                // 센서 세대는 Backend 소유 상태이므로 복사 도중 바뀌었다면 이전 관측으로 보고하지 않는다.
                if (!motor.IsContactObservationCurrent())
                {
                    return MotorContactReadStatus.NotObserved;
                }

                count = copied;
                return MotorContactReadStatus.Ready;
            }
            finally
            {
                motor.ExitOperation();
            }
        }

    #endregion

    }
}
