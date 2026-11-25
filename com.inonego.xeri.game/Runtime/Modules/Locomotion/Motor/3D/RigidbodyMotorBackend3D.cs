/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : RigidbodyMotorBackend3D.cs
수정일 : 2026-11-25

# 설명
Unity Rigidbody의 현재 상태·Tick 합성 결과 적용과 선택적인 충돌 Probe 관측을 제공한다.

# 특이사항, 제약사항
Planar/Elevation은 합성 결과가 가진 Model Up 축 기준이다. Physics Step 및 World History는 외부 소유다.
유한한 입력도 축 투영 중 오버플로할 수 있으므로 Rigidbody에 쓰기 직전 값을 검증하고, 거절한 결과는 Rigidbody를 바꾸지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Primitive;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;

namespace inonego.Xeri.Game.Motor
{
    // ============================================================
    /// <summary>
    /// <br/> Rigidbody에 대한 기본 3D Motor Backend입니다.
    /// <br/> Planar/Elevation 분해는 합성 결과의 Up 축을 따릅니다.
    /// </summary>
    // ============================================================
    public sealed class RigidbodyMotorBackend3D :
        IMotorBackend<MotorState3D>,
        IMotorContactBackend<ContactPoint>,
        IMotorContactObservationRevision
    {

    #region 필드

        private readonly Rigidbody rigidbody;
        private readonly MotorCollisionProbe3D collisionProbe;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody와 선택적인 충돌 Probe를 연결합니다.
        /// </summary>
        // ------------------------------------------------------------
        public RigidbodyMotorBackend3D
        (
            Rigidbody rigidbody,
            MotorCollisionProbe3D collisionProbe = null
        )
        {
            if (rigidbody == null)
            {
                throw new ArgumentNullException(nameof(rigidbody));
            }

            if (collisionProbe != null && collisionProbe.gameObject != rigidbody.gameObject)
            {
                throw new ArgumentException("충돌 센서는 Rigidbody와 같은 GameObject에 있어야 합니다.", nameof(collisionProbe));
            }

            this.rigidbody = rigidbody;
            this.collisionProbe = collisionProbe;
        }

    #endregion

    #region 현재 상태

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody의 실제 Pose와 속도를 조회합니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotorState3D ReadCurrentState() => new
        (
            rigidbody.position, rigidbody.rotation,
            rigidbody.linearVelocity, rigidbody.angularVelocity
        );

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody에 권한 있는 현재 물리 상태를 적용합니다.
        /// </summary>
        // ------------------------------------------------------------
        public void ApplyCurrentState(in MotorState3D state)
        {
            // 성분이 유한해도 크기 제곱이 오버플로하는 회전은 거절한다.
            var rotationMagnitudeSquared = Quaternion.Dot(state.Rotation, state.Rotation);

            if (!state.Position.IsFinite() || !state.Rotation.IsFinite() ||
                !rotationMagnitudeSquared.IsFinite() || rotationMagnitudeSquared <= 1e-12f ||
                !state.LinearVelocity.IsFinite() || !state.AngularVelocity.IsFinite())
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }

            rigidbody.position = state.Position;
            rigidbody.rotation = state.Rotation;
            rigidbody.linearVelocity = state.LinearVelocity;
            rigidbody.angularVelocity = state.AngularVelocity;
        }

    #endregion

    #region 운동 적용

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Tick 합성 결과를 선속도·각속도에 한 번에 적용합니다.
        /// <br/> 유효하지 않은 프레임이나 계산 결과는 Body를 바꾸지 않습니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryApply(in MotionFrame frame)
        {
            if (!frame.IsValid)
            {
                return false;
            }

            // 축 투영이 오버플로한 결과는 각속도를 포함해 어떤 값도 쓰기 전에 거절한다.
            var linear = frame.ComposeLinear(rigidbody.linearVelocity);
            var angular = frame.ComposeAngular(rigidbody.angularVelocity);

            if (!linear.IsFinite() || !angular.IsFinite())
            {
                return false;
            }

            rigidbody.linearVelocity = linear;
            rigidbody.angularVelocity = angular;
            return true;
        }

    #endregion

    #region 접촉 관측

        // ------------------------------------------------------------
        /// <summary>
        /// 연결된 3D 충돌 센서가 있는지 알려줍니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool SupportsContactObservation =>
            collisionProbe != null && collisionProbe.isActiveAndEnabled;

        // ------------------------------------------------------------
        /// <summary>
        /// Probe의 비활성화로 변경된 관측 세대입니다.
        /// </summary>
        // ------------------------------------------------------------
        public Revision ContactObservationRevision =>
            collisionProbe != null ? collisionProbe.ObservationRevision : Revision.None;

        // ------------------------------------------------------------
        /// <summary>
        /// 다음 물리 Step을 위해 센서의 접촉 기록을 초기화합니다.
        /// </summary>
        // ------------------------------------------------------------
        public void BeginContactStep() => collisionProbe?.Clear();

        // ------------------------------------------------------------
        /// <summary>
        /// Correction 또는 복원 뒤 접촉 기록을 무효화합니다.
        /// </summary>
        // ------------------------------------------------------------
        public void ClearContactObservation() => collisionProbe?.Clear();

        // ------------------------------------------------------------
        /// <summary>
        /// Physics Step 중 충돌 콜백으로 수집된 접촉을 복사합니다.
        /// </summary>
        // ------------------------------------------------------------
        public int ReadContacts(ContactPoint[] results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            return collisionProbe != null ? collisionProbe.ReadContacts(results) : 0;
        }

    #endregion

    }
}
