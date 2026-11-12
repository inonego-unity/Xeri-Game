/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : Billboard.cs
수정일 : 2026-05-08

# 설명
LookTarget 을 향해 회전하는 빌보드 컴포넌트.
LookMode(Parallel/HardLook) · Up · LockAxis 를 BillboardManager 의 기본값에서 가져오며 인스턴스별 Override 도 지원한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    using Xeri.Serializable;

    // ============================================================
    /// <summary>
    /// 빌보드 회전 모드.
    /// </summary>
    // ============================================================
    public enum BillboardLookMode
    {
        Parallel,
        HardLook,
    }

    // ============================================================
    /// <summary>
    /// LateUpdate 에서 LookTarget 을 향해 회전하는 컴포넌트.
    /// </summary>
    // ============================================================
    [DefaultExecutionOrder(1000)]
    public class Billboard : MonoBehaviour
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 따를 BillboardManager 의 슬롯 이름. 기본은 DEFAULT_SLOT.
        /// </summary>
        // ------------------------------------------------------------
        [SerializeField]
        private string managerSlot = MonoSingleton<BillboardManager>.DEFAULT_SLOT;

        // ------------------------------------------------------------
        /// <summary>
        /// 인스턴스별 LookTarget 오버라이드(null 이면 매니저 기본값).
        /// </summary>
        // ------------------------------------------------------------
        public Transform OverrideLookTarget = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 인스턴스별 LookMode 오버라이드.
        /// </summary>
        // ------------------------------------------------------------
        public XNullable<BillboardLookMode> OverrideMode = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 인스턴스별 Up 벡터 오버라이드.
        /// </summary>
        // ------------------------------------------------------------
        public XNullable<Vector3> OverrideUp = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 인스턴스별 LockAxis 오버라이드.
        /// </summary>
        // ------------------------------------------------------------
        public XNullable<XVec3B> OverrideLockAxis = null;

    #endregion

    #region 유니티 이벤트

        private void LateUpdate()
        {
            if (!BillboardManager.Named.TryGet(managerSlot, out var manager)) return;

            var lookTarget = OverrideLookTarget != null    ? OverrideLookTarget : manager.LookTarget;
            var mode       = OverrideMode    .HasValue     ? OverrideMode.Value : manager.Mode;
            var up         = OverrideUp      .HasValue     ? OverrideUp.Value   : manager.Up;
            var lockAxis   = OverrideLockAxis.HasValue     ? OverrideLockAxis.Value : manager.LockAxis;

            Quaternion lTargetRotation;

            switch (mode)
            {
                case BillboardLookMode.Parallel:
                    lTargetRotation = GetLookRotationParallel(lookTarget, up);
                    break;

                case BillboardLookMode.HardLook:
                    lTargetRotation = GetLookRotationHardLook(lookTarget, up);
                    break;

                default:
                    return;
            }

            transform.rotation = ApplyLockAxis(transform.rotation, lTargetRotation, lockAxis);
        }

    #endregion

    #region 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// Parallel 모드의 목표 회전을 계산한다.
        /// </summary>
        // ------------------------------------------------------------
        private Quaternion GetLookRotationParallel(Transform lookTarget, Vector3 up)
        {
            if (lookTarget == null) return transform.rotation;

            return Quaternion.LookRotation(lookTarget.forward, up);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// HardLook 모드의 목표 회전을 계산한다.
        /// </summary>
        // ------------------------------------------------------------
        private Quaternion GetLookRotationHardLook(Transform lookTarget, Vector3 up)
        {
            if (lookTarget == null) return transform.rotation;

            Vector3 delta = (lookTarget.position - transform.position).normalized;

            return Quaternion.LookRotation(delta, up);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// LockAxis 에 따라 원래 회전과 목표 회전을 블렌드한다.
        /// </summary>
        // ------------------------------------------------------------
        private Quaternion ApplyLockAxis(Quaternion originalRotation, Quaternion targetRotation, XVec3B lockAxis)
        {
            Vector3 originalEuler = originalRotation.eulerAngles;
            Vector3 lTargetEuler  = targetRotation.eulerAngles;

            Vector3 resultEuler = new Vector3
            (
                lockAxis.X ? originalEuler.x : lTargetEuler.x,
                lockAxis.Y ? originalEuler.y : lTargetEuler.y,
                lockAxis.Z ? originalEuler.z : lTargetEuler.z
            );

            return Quaternion.Euler(resultEuler);
        }

    #endregion

    }
}
