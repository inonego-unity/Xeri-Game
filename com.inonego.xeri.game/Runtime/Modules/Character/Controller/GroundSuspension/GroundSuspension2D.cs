/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : GroundSuspension2D.cs
수정일 : 2026-08-02

# 설명
GroundSuspension 공통 정책을 Rigidbody2D와 수직 CapsuleCollider2D에 연결한다.
2D 물리 속도와 각속도는 GroundChecker2D와 같은 Vector3 월드 계약으로 정규화한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game.Controller
{
    // ============================================================
    /// <summary>
    /// Rigidbody2D와 수직 CapsuleCollider2D를 사용하는 지면 지지 구현.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class GroundSuspension2D : GroundSuspension<Rigidbody2D, Collider2D, GroundCheckSample2D, CapsuleCollider2D, GroundSuspension2DSettings>
    {

    #region 필드

        private Vector2 originalCapsuleOffset = Vector2.zero;
        private Vector2 originalCapsuleSize = Vector2.zero;
        private bool hasOriginalGeometry = false;

        // ------------------------------------------------------------
        /// <summary>
        /// Physics2D 중력 벡터.
        /// </summary>
        // ------------------------------------------------------------
        protected override Vector3 Gravity => Rigid != null && Rigid.simulated && Rigid.bodyType == RigidbodyType2D.Dynamic ? Physics2D.gravity * Rigid.gravityScale : Vector3.zero;

        // ------------------------------------------------------------
        /// <summary>
        /// Physics2D의 기본 접촉 여유.
        /// </summary>
        // ------------------------------------------------------------
        protected override float ContactOffset => Physics2D.defaultContactOffset;

    #endregion

    #region 차원별 구현

        // ------------------------------------------------------------
        /// <summary>
        /// 수직 CapsuleCollider2D와 유효한 Y축 스케일을 요구한다.
        /// </summary>
        // ------------------------------------------------------------
        protected override void ValidateCapsule(CapsuleCollider2D capsule)
        {
            if (capsule.direction != CapsuleDirection2D.Vertical)
            {
                throw new InvalidOperationException
                (
                    "GroundSuspension2D는 수직 CapsuleCollider2D만 지원합니다."
                );
            }

            if (capsule.isTrigger)
            {
                throw new InvalidOperationException
                (
                    "GroundSuspension2D에 사용하는 CapsuleCollider2D는 Trigger일 수 없습니다."
                );
            }

            if (Mathf.Abs(capsule.transform.lossyScale.y) <= Mathf.Epsilon)
            {
                throw new InvalidOperationException
                (
                    "GroundSuspension2D에 사용하는 CapsuleCollider2D의 Y축 스케일은 0일 수 없습니다."
                );
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 초기화 전 CapsuleCollider2D 형상을 보존한다.
        /// </summary>
        // ------------------------------------------------------------
        protected override void CaptureOriginalGeometry()
        {
            originalCapsuleOffset = Capsule.offset;
            originalCapsuleSize   = Capsule.size;
            hasOriginalGeometry   = true;
        }

        // ------------------------------------------------------------------------------------------
        /// <summary>
        /// CapsuleCollider2D 윗면은 유지하고 하단만 줄여 목표 부유 높이만큼 스텝 여유를 확보한다.
        /// </summary>
        // ------------------------------------------------------------------------------------------
        protected override void ConfigureCapsule()
        {
            var verticalScale = Mathf.Abs(Capsule.transform.lossyScale.y);
            var worldClearance = Mathf.Max(0f, Settings.TargetHeight);
            var localClearance = worldClearance / verticalScale;
            var minimumHeight  = originalCapsuleSize.x;
            var nextHeight = Mathf.Max
            (
                minimumHeight,
                originalCapsuleSize.y - localClearance
            );
            var appliedClearance = originalCapsuleSize.y - nextHeight;

            Capsule.size = new Vector2(originalCapsuleSize.x, nextHeight);
            Capsule.offset = originalCapsuleOffset +
                             Vector2.up * (appliedClearance * 0.5f);
            TargetDistance = appliedClearance * verticalScale;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 초기화 전에 사용하던 CapsuleCollider2D 형상을 복원한다.
        /// </summary>
        // ------------------------------------------------------------
        protected override void RestoreCapsule()
        {
            if (!hasOriginalGeometry || Capsule == null) return;

            Capsule.offset       = originalCapsuleOffset;
            Capsule.size         = originalCapsuleSize;
            hasOriginalGeometry  = false;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody2D의 선형 속도를 월드 벡터로 가져온다.
        /// </summary>
        // ------------------------------------------------------------
        protected override Vector3 GetLinearVelocity(Rigidbody2D rigidbody) => rigidbody.linearVelocity;

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody2D의 지정한 월드 지점 속도를 가져온다.
        /// </summary>
        // ------------------------------------------------------------
        protected override Vector3 GetPointVelocity(Rigidbody2D rigidbody, Vector3 worldPoint) => rigidbody.GetPointVelocity(worldPoint);

        // ------------------------------------------------------------
        /// <summary>
        /// Rigidbody2D의 각속도를 라디안 단위 월드 벡터로 가져온다.
        /// </summary>
        // ------------------------------------------------------------
        protected override Vector3 GetAngularVelocity(Rigidbody2D rigidbody) => Vector3.forward * (rigidbody.angularVelocity * Mathf.Deg2Rad);

    #endregion

    }
}
