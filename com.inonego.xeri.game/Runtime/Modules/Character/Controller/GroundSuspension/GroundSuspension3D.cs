/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : GroundSuspension3D.cs
수정일 : 2026-08-02

# 설명
GroundSuspension 공통 정책을 Rigidbody와 수직 CapsuleCollider에 연결한다.
3D 물리 속도와 각속도를 Vector3 월드 계약으로 제공한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game.Controller
{
    // ============================================================
    /// <summary>
    /// Rigidbody와 수직 CapsuleCollider를 사용하는 지면 지지 구현.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class GroundSuspension3D : GroundSuspension<Rigidbody, Collider, GroundCheckSample3D, CapsuleCollider, GroundSuspension3DSettings>
    {

    #region 필드

        private Vector3 originalCapsuleCenter = Vector3.zero;
        private float originalCapsuleHeight = 0f;
        private bool hasOriginalGeometry = false;

        // ------------------------------------------------------------
        /// <summary>
        /// Physics 중력 벡터.
        /// </summary>
        // ------------------------------------------------------------
        protected override Vector3 Gravity => Rigid != null && !Rigid.isKinematic && Rigid.useGravity ? Physics.gravity : Vector3.zero;

        // ------------------------------------------------------------
        /// <summary>
        /// Physics의 기본 접촉 여유.
        /// </summary>
        // ------------------------------------------------------------
        protected override float ContactOffset => Physics.defaultContactOffset;

    #endregion

    #region 차원별 구현

        // ------------------------------------------------------------
        /// <summary>
        /// 수직 CapsuleCollider와 유효한 Y축 스케일을 요구한다.
        /// </summary>
        // ------------------------------------------------------------
        protected override void ValidateCapsule(CapsuleCollider capsule)
        {
            if (capsule.direction != 1)
            {
                throw new InvalidOperationException
                (
                    "GroundSuspension3D는 로컬 Y축 수직 CapsuleCollider만 지원합니다."
                );
            }

            if (capsule.isTrigger)
            {
                throw new InvalidOperationException
                (
                    "GroundSuspension3D에 사용하는 CapsuleCollider는 Trigger일 수 없습니다."
                );
            }

            if (Mathf.Abs(capsule.transform.lossyScale.y) <= Mathf.Epsilon)
            {
                throw new InvalidOperationException
                (
                    "GroundSuspension3D에 사용하는 CapsuleCollider의 Y축 스케일은 0일 수 없습니다."
                );
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 초기화 전 CapsuleCollider 형상을 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        protected override void CaptureOriginalGeometry()
        {
            originalCapsuleCenter = Capsule.center;
            originalCapsuleHeight = Capsule.height;
            hasOriginalGeometry   = true;
        }

        // ------------------------------------------------------------------------------------------
        /// <summary>
        /// 캡슐 윗면은 유지하고 하단만 줄여 목표 부유 높이만큼 스텝 여유를 확보한다.
        /// </summary>
        // ------------------------------------------------------------------------------------------
        protected override void ConfigureCapsule()
        {
            var verticalScale = Mathf.Abs(Capsule.transform.lossyScale.y);
            var worldClearance = Mathf.Max(0f, Settings.TargetHeight);
            var localClearance = worldClearance / verticalScale;
            var minimumHeight  = Capsule.radius * 2f;
            var nextHeight = Mathf.Max
            (
                minimumHeight,
                originalCapsuleHeight - localClearance
            );
            var appliedClearance = originalCapsuleHeight - nextHeight;

            Capsule.height = nextHeight;
            Capsule.center = originalCapsuleCenter +
                             Vector3.up * (appliedClearance * 0.5f);
            TargetDistance = appliedClearance * verticalScale;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 초기화 전에 사용하던 CapsuleCollider 형상을 복원한다.
        /// </summary>
        // ------------------------------------------------------------
        protected override void RestoreCapsule()
        {
            if (!hasOriginalGeometry || Capsule == null) return;

            Capsule.center       = originalCapsuleCenter;
            Capsule.height       = originalCapsuleHeight;
            hasOriginalGeometry  = false;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody의 선형 속도를 월드 벡터로 가져온다.
        /// </summary>
        // ------------------------------------------------------------
        protected override Vector3 GetLinearVelocity(Rigidbody rigidbody) => rigidbody.linearVelocity;

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody의 지정한 월드 지점 속도를 가져온다.
        /// </summary>
        // ------------------------------------------------------------
        protected override Vector3 GetPointVelocity(Rigidbody rigidbody, Vector3 worldPoint) => rigidbody.GetPointVelocity(worldPoint);

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody의 라디안 단위 월드 각속도 벡터를 가져온다.
        /// </summary>
        // ------------------------------------------------------------
        protected override Vector3 GetAngularVelocity(Rigidbody rigidbody) => rigidbody.angularVelocity;

    #endregion

    }
}
