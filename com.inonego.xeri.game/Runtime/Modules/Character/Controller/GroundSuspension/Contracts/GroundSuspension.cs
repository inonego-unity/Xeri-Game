/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : GroundSuspension.cs
수정일 : 2026-08-03

# 설명
2D/3D Floating Capsule 지면 지지의 공통 초기화, 표본 계산과 상태 전이를 담당한다.
차원별 구현은 Rigidbody 접근과 CapsuleCollider 형상 처리만 제공한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game.Controller
{
    // ============================================================
    /// <summary>
    /// 2D/3D Floating Capsule 지면 지지의 공통 구현.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class GroundSuspension<TRigidbody, TCollider, TGroundSample, TCapsule, TSettings>
    where TRigidbody : Component
    where TCollider : Component
    where TGroundSample : struct, IGroundCheckSample<TRigidbody, TCollider>
    where TCapsule : Component
    where TSettings : GroundSuspensionSettings
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 지면 지지를 적용할 Rigidbody.
        /// </summary>
        // ------------------------------------------------------------
        protected TRigidbody Rigid => rigid;
        private TRigidbody rigid = null;

        // ------------------------------------------------------------
        /// <summary>
        /// Floating Capsule 형상으로 사용할 Collider.
        /// </summary>
        // ------------------------------------------------------------
        protected TCapsule Capsule => capsule;
        private TCapsule capsule = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 지면 지지 조정값.
        /// </summary>
        // ------------------------------------------------------------
        protected TSettings Settings => settings;
        private TSettings settings = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Capsule 형상에 실제 적용된 지면 목표 거리.
        /// </summary>
        // ------------------------------------------------------------
        public float TargetDistance { get; protected set; } = 0f;

        private bool isFollowingGround = false;
        private bool isDetached = false;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 차원에서 사용하는 중력 벡터.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract Vector3 Gravity { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 물리 엔진의 기본 접촉 여유.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract float ContactOffset { get; }

    #endregion

    #region 초기화

        // ------------------------------------------------------------
        /// <summary>
        /// 지면 지지 계산에 필요한 물리 형상과 설정을 연결한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Init(TRigidbody rigid, TCapsule capsule, TSettings settings)
        {
            if (rigid == null)
            {
                throw new ArgumentNullException(nameof(rigid));
            }

            if (capsule == null)
            {
                throw new ArgumentNullException(nameof(capsule));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            ValidateCapsule(capsule);

            this.rigid    = rigid;
            this.capsule  = capsule;
            this.settings = settings;

            TargetDistance    = 0f;
            isFollowingGround = false;
            isDetached        = false;

            CaptureOriginalGeometry();
            ConfigureCapsule();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 원래 Capsule 형상을 복원하고 런타임 참조를 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Release()
        {
            RestoreCapsule();

            rigid    = null;
            capsule  = null;
            settings = null;

            TargetDistance    = 0f;
            isFollowingGround = false;
            isDetached        = false;
        }

    #endregion

    #region 메서드

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>GroundChecker가 승인한 표면 정보와 Rigidbody 운동을 사용해
        /// <br/>현재 상대 운동에 맞는 지면 지지 표본을 계산한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public GroundSuspensionSample<TRigidbody> Sample(in TGroundSample groundSample)
        {
            if (rigid == null || capsule == null || settings == null)
            {
                throw new InvalidOperationException("GroundSuspension이 초기화되지 않았습니다.");
            }

            if (!groundSample.HasGround)
            {
                isFollowingGround = false;
                return default;
            }

            var up = capsule.transform.up.normalized;
            var normal = groundSample.Normal.normalized;
            var normalUpDot = Vector3.Dot(normal, up);

            // 외부 표본도 같은 지지 경사 계약을 만족해야 Suspension이 추종합니다.
            if (!IsSupportSurface(normalUpDot))
            {
                isFollowingGround = false;
                return default;
            }

            var groundRigid = groundSample.GroundRigid;
            var groundVelocity = groundRigid != null
                ? GetPointVelocity(groundRigid, groundSample.Point)
                : Vector3.zero;
            var groundAngularVelocity = groundRigid != null
                ? GetAngularVelocity(groundRigid)
                : Vector3.zero;
            var relativeVelocity = GetLinearVelocity(rigid) - groundVelocity;
            var relativeUpSpeed  = Vector3.Dot(relativeVelocity, up);
            var relativePlanarVelocity = Vector3.ProjectOnPlane
            (
                relativeVelocity,
                up
            );
            var groundFollowUpSpeed = -Vector3.Dot
            (
                relativePlanarVelocity,
                normal
            ) / normalUpDot;
            var gravityCompensation = Mathf.Max
            (
                0f,
                Vector3.Dot(-Gravity, up)
            );

            if
            (
                !TryCalculateAcceleration
                (
                    groundSample.Distance,
                    relativeUpSpeed,
                    groundFollowUpSpeed,
                    gravityCompensation,
                    out var acceleration
                )
            )
            {
                return default;
            }

            return new GroundSuspensionSample<TRigidbody>
            (
                groundSample.Ground,
                groundRigid,
                groundSample.Distance,
                groundSample.Point,
                normal,
                groundVelocity,
                groundAngularVelocity,
                acceleration
            );
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 지면 추종을 끝내고 하강 착지 전까지 재획득을 막는다.
        /// </summary>
        // ------------------------------------------------------------
        public void Detach()
        {
            isFollowingGround = false;
            isDetached        = true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 표면 법선이 현재 설정에서 지지 가능한지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        private bool IsSupportSurface(float normalUpDot)
        {
            return normalUpDot >= settings.MinimumGroundAlignment;
        }

        // ------------------------------------------------------------------------------------------
        /// <summary>
        /// <br/>승인된 표면 거리와 상대 운동으로 이번 Tick의 지면 추종 가속도를 계산한다.
        /// <br/>이륙 분리와 최초 획득, 최대 추종 거리 상태도 같은 계약에서 갱신한다.
        /// </summary>
        // ------------------------------------------------------------------------------------------
        private bool TryCalculateAcceleration
        (
            float distance,
            float relativeUpSpeed,
            float groundFollowUpSpeed,
            float gravityCompensation,
            out float acceleration
        )
        {
            acceleration = 0f;

            var acquisitionDistance = TargetDistance + ContactOffset;
            var maximumDistance = Mathf.Max
            (
                acquisitionDistance,
                settings.MaximumDistance
            );

            // 이륙으로 분리된 동안에는 하강해 실제 착지 범위에 돌아오기 전까지 지면을 다시 잡지 않는다.
            if (isDetached)
            {
                if (relativeUpSpeed >= 0f || distance > acquisitionDistance)
                {
                    return false;
                }

                isDetached = false;
            }

            // 최초 지지는 Collider 아래의 접촉 범위에서만 획득한다.
            if (!isFollowingGround && distance > acquisitionDistance)
            {
                return false;
            }

            // 지지 중에는 설정이 허용한 최대 거리까지만 낮아지는 지면을 이어서 추종한다.
            if (isFollowingGround && distance > maximumDistance)
            {
                isFollowingGround = false;
                return false;
            }

            isFollowingGround = true;

            var suspensionSpeed = relativeUpSpeed - groundFollowUpSpeed;
            var heightError = TargetDistance - distance;

            acceleration = gravityCompensation +
                           heightError * Mathf.Max(0f, settings.Strength) -
                           suspensionSpeed * Mathf.Max(0f, settings.Damping);
            acceleration = Mathf.Clamp
            (
                acceleration,
                -Mathf.Max(0f, settings.MaxAcceleration),
                Mathf.Max(0f, settings.MaxAcceleration)
            );

            return true;
        }

    #endregion

    #region 차원별 구현

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 차원에서 지원하는 Capsule 형상인지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract void ValidateCapsule(TCapsule capsule);

        // ------------------------------------------------------------
        /// <summary>
        /// 초기화 전 Capsule 형상을 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract void CaptureOriginalGeometry();

        // ------------------------------------------------------------
        /// <summary>
        /// 목표 부유 높이에 맞게 Capsule 형상을 구성한다.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract void ConfigureCapsule();

        // ------------------------------------------------------------
        /// <summary>
        /// 초기화 전에 사용하던 Capsule 형상을 복원한다.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract void RestoreCapsule();

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody의 선형 속도를 월드 벡터로 가져온다.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract Vector3 GetLinearVelocity(TRigidbody rigidbody);

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody의 지정한 월드 지점 속도를 가져온다.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract Vector3 GetPointVelocity(TRigidbody rigidbody, Vector3 worldPoint);

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody의 라디안 단위 월드 각속도 벡터를 가져온다.
        /// </summary>
        // ------------------------------------------------------------
        protected abstract Vector3 GetAngularVelocity(TRigidbody rigidbody);

    #endregion

    }
}
