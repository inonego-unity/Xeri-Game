/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : BillboardManager.cs
수정일 : 2026-05-08

# 설명
씬 전역 빌보드 기본값(LookTarget·Mode·Up·LockAxis)을 보유하는 MonoSingleton.
Awake 에서 자동 Register 하며 OnDestroy 시 베이스에서 자동 Unregister 된다.
인스턴스별 오버라이드는 Billboard 컴포넌트의 Override* 필드로 처리한다.
========================================================================= BLOCK_HEADER_END */

using UnityEngine;

namespace inonego.Xeri.Game
{
    using Xeri.Serializable;

    // ============================================================
    /// <summary>
    /// 빌보드 전역 매니저.
    /// </summary>
    // ============================================================
    public class BillboardManager : MonoSingleton<BillboardManager>
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 등록할 슬롯 이름. 기본은 DEFAULT_SLOT.
        /// </summary>
        // ------------------------------------------------------------
        [SerializeField]
        private string slotKey = DEFAULT_SLOT;

        // ------------------------------------------------------------
        /// <summary>
        /// 빌보드가 바라볼 기본 대상.
        /// </summary>
        // ------------------------------------------------------------
        public Transform LookTarget;

        // ------------------------------------------------------------
        /// <summary>
        /// 기본 LookMode.
        /// </summary>
        // ------------------------------------------------------------
        public BillboardLookMode Mode = BillboardLookMode.Parallel;

        // ------------------------------------------------------------
        /// <summary>
        /// 기본 Up 벡터.
        /// </summary>
        // ------------------------------------------------------------
        public Vector3 Up = Vector3.up;

        // ------------------------------------------------------------
        /// <summary>
        /// 기본 축 잠금(true 면 해당 축 회전 유지).
        /// </summary>
        // ------------------------------------------------------------
        public XVec3B LockAxis;

    #endregion

    #region 유니티 이벤트

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 인스턴스를 slotKey 슬롯에 등록한다.
        /// <br/> 이미 다른 인스턴스가 점유 중이면 자기 GameObject 를 파괴해 단일 매니저를 보장한다.
        /// </summary>
        // ----------------------------------------------------------------------
        private void Awake()
        {
            if (!TryRegisterOrDestroy(slotKey, this)) return;
        }

    #endregion

    }
}
