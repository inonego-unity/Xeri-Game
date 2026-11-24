/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotorContactFeedback.cs
수정일 : 2026-11-21

# 설명
Physics Step 이후 실제 접촉 Normal과 Body 속도에서 Blocking 및 Slide 관측을 계산한다.

# 특이사항, 제약사항
운동 정책이나 충돌 Solver를 구현하지 않는다. 외부 Host가 의도 속도와 실제 접촉을 전달한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Primitive;
using inonego.Xeri.Game;

namespace inonego.Xeri.Game.Motor
{
    // ============================================================
    /// <summary>
    /// <br/> 의도 운동과 실제 접촉에 기반한 관측 결과입니다.
    /// <br/> SlideVelocity는 실제 속도를 접촉면에 투영한 값입니다.
    /// </summary>
    // ============================================================
    public readonly struct MotorContactFeedback
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 접촉면이 의도 운동을 실질적으로 차단했는지 나타냅니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsBlocked { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 의도 운동을 차단하는 접촉면의 Normal입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 BlockingNormal { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 실제 속도 중 차단 접촉면에 접하는 성분입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 SlideVelocity { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 의도 속도와 관측 속도의 차이입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 VelocityError { get; }

    #endregion

    #region 생성자

        internal MotorContactFeedback
        (
            bool isBlocked, Vector3 blockingNormal,
            Vector3 slideVelocity, Vector3 velocityError
        )
        {
            IsBlocked = isBlocked;
            BlockingNormal = blockingNormal;
            SlideVelocity = slideVelocity;
            VelocityError = velocityError;
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// <br/> 실제 Physics 접촉과 속도에서 Blocking/Slide를 판별합니다.
    /// <br/> 새로운 충돌 응답이나 Velocity 변경은 수행하지 않습니다.
    /// </summary>
    // ============================================================
    public static class MotorContactFeedbackEvaluator
    {

    #region 충돌 관측 계산

        // ------------------------------------------------------------
        /// <summary>
        /// 2D ContactPoint 표본으로 충돌 관측을 계산합니다.
        /// </summary>
        // ------------------------------------------------------------
        public static MotorContactFeedback Evaluate
        (
            Vector2 intendedVelocity,
            Vector2 observedVelocity,
            ContactPoint2D[] contacts,
            int count,
            float tolerance = 0.01f
        )
        {
            Validate(intendedVelocity, observedVelocity, contacts, count, tolerance);

            for (var index = 0; index < count; index++)
            {
                var normal = (Vector3)contacts[index].normal;

                var blocked = TryGetBlockingNormal
                (
                    intendedVelocity, observedVelocity, normal,
                    tolerance, out var blockingNormal
                );

                if (blocked)
                {
                    return BuildFeedback
                    (
                        intendedVelocity, observedVelocity, blockingNormal
                    );
                }
            }

            return BuildFeedback(intendedVelocity, observedVelocity, Vector3.zero);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 3D ContactPoint 표본으로 충돌 관측을 계산합니다.
        /// </summary>
        // ------------------------------------------------------------
        public static MotorContactFeedback Evaluate
        (
            Vector3 intendedVelocity,
            Vector3 observedVelocity,
            ContactPoint[] contacts,
            int count,
            float tolerance = 0.01f
        )
        {
            Validate(intendedVelocity, observedVelocity, contacts, count, tolerance);

            for (var index = 0; index < count; index++)
            {
                var blocked = TryGetBlockingNormal
                (
                    intendedVelocity, observedVelocity,
                    contacts[index].normal, tolerance,
                    out var blockingNormal
                );

                if (blocked)
                {
                    return BuildFeedback
                    (
                        intendedVelocity, observedVelocity, blockingNormal
                    );
                }
            }

            return BuildFeedback(intendedVelocity, observedVelocity, Vector3.zero);
        }

    #endregion

    #region 유틸

        private static MotorContactFeedback BuildFeedback
        (
            Vector3 intended, Vector3 observed, Vector3 blockingNormal
        )
        {
            var isBlocked = blockingNormal != Vector3.zero;
            var slide = isBlocked
                ? observed - blockingNormal * Vector3.Dot(observed, blockingNormal)
                : observed;

            return new MotorContactFeedback
            (
                isBlocked, blockingNormal, slide, intended - observed
            );
        }

        private static bool TryGetBlockingNormal
        (
            Vector3 intended, Vector3 observed,
            Vector3 normal, float tolerance,
            out Vector3 blockingNormal
        )
        {
            blockingNormal = Vector3.zero;

            if (normal.sqrMagnitude <= 1e-10f)
            {
                return false;
            }

            normal.Normalize();

            var expectedInward = -Vector3.Dot(intended, normal);
            var actualInward = -Vector3.Dot(observed, normal);

            if (expectedInward <= tolerance ||
                actualInward >= expectedInward - tolerance)
            {
                return false;
            }

            blockingNormal = normal;
            return true;
        }

        private static void Validate<T>
        (
            Vector3 intendedVelocity, Vector3 observedVelocity, T[] contacts, int count, float tolerance
        )
        {
            // 유한하지 않은 속도는 비교가 모두 false가 되어 차단으로 오판하므로 먼저 거절한다.
            if (!intendedVelocity.IsFinite())
            {
                throw new ArgumentOutOfRangeException(nameof(intendedVelocity));
            }

            if (!observedVelocity.IsFinite())
            {
                throw new ArgumentOutOfRangeException(nameof(observedVelocity));
            }

            if (contacts == null)
            {
                throw new ArgumentNullException(nameof(contacts));
            }

            if (count < 0 || count > contacts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (float.IsNaN(tolerance) || float.IsInfinity(tolerance) || tolerance < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(tolerance));
            }
        }

    #endregion

    }
}