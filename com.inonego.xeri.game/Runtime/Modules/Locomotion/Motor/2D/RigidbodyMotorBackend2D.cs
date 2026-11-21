/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : RigidbodyMotorBackend2D.cs
수정일 : 2026-11-21

# 설명
Unity Rigidbody2D의 현재 상태·Tick 합성 결과 적용과 실제 접촉 조회를 제공한다.

# 특이사항, 제약사항
Physics2D Step은 외부 소유다. 과거 World와 Contact의 복원은 지원하지 않는다.
선속도는 Model Up 축으로 나눈 결과의 X·Y만 쓴다(위에서 보기는 Up=Z, 옆에서 보기는 Up=Y). 각속도는 rad/s를 도/초로 바꿔 Z축에 쓴다.
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
    /// <br/> Rigidbody2D에 대한 기본 Motor Backend입니다.
    /// <br/> 합성 결과의 X·Y 선속도와 Z축 각속도를 적용합니다.
    /// </summary>
    // ============================================================
    public sealed class RigidbodyMotorBackend2D :
        IMotorBackend<MotorState2D>,
        IMotorContactBackend<ContactPoint2D>
    {

    #region 필드

        private readonly Rigidbody2D rigidbody;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Body의 Rigidbody2D를 연결합니다.
        /// </summary>
        // ------------------------------------------------------------
        public RigidbodyMotorBackend2D(Rigidbody2D rigidbody)
        {
            if (rigidbody == null)
            {
                throw new ArgumentNullException(nameof(rigidbody));
            }

            this.rigidbody = rigidbody;
        }

    #endregion

    #region 현재 상태

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody2D의 실제 Pose와 속도를 조회합니다.
        /// </summary>
        // ------------------------------------------------------------
        public MotorState2D ReadCurrentState() => new
        (
            rigidbody.position, rigidbody.rotation,
            rigidbody.linearVelocity, rigidbody.angularVelocity
        );

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody2D에 권한 있는 현재 물리 상태를 적용합니다.
        /// </summary>
        // ------------------------------------------------------------
        public void ApplyCurrentState(in MotorState2D state)
        {
            if (!state.Position.IsFinite() || !state.Rotation.IsFinite() ||
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

            // Body 속도를 3D로 올려 채널 방식을 적용한 뒤 X·Y만 되돌려 쓴다.
            var current = rigidbody.linearVelocity;
            var linear = frame.ComposeLinear(new Vector3(current.x, current.y, 0f));
            var angular = frame.RotationMode == MotionChannelMode.Set
                ? frame.Rotation.z * Mathf.Rad2Deg
                : rigidbody.angularVelocity;

            if (!linear.IsFinite() || !angular.IsFinite())
            {
                return false;
            }

            rigidbody.linearVelocity = new Vector2(linear.x, linear.y);
            rigidbody.angularVelocity = angular;
            return true;
        }

    #endregion

    #region 접촉 관측

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody2D의 실제 접촉 조회를 지원합니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool SupportsContactObservation => true;

        // ------------------------------------------------------------
        /// <summary>
        /// 접촉은 Rigidbody2D에서 직접 읽어 캐시가 필요 없습니다.
        /// </summary>
        // ------------------------------------------------------------
        public void BeginContactStep()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Physics Step 관측 유효성은 Motor가 관리합니다.
        /// </summary>
        // ------------------------------------------------------------
        public void ClearContactObservation()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 실제 2D 접촉을 호출자의 결과 배열에 기록합니다.
        /// </summary>
        // ------------------------------------------------------------
        public int ReadContacts(ContactPoint2D[] results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            return rigidbody.GetContacts(results);
        }

    #endregion

    }
}
