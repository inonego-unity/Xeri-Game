/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : DeliveryBatchResult.cs
수정일 : 2026-11-02

# 설명
2D·3D Delivery 공통의 후보 수와 버퍼 포화·검색 완전성을 보존한다.
========================================================================= BLOCK_HEADER_END */

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 공간 질의 결과 수와 버퍼 포화 여부를 보고한다.
    /// </summary>
    // ============================================================
    public readonly struct DeliveryBatchResult
    {

        public int Count { get; }
        public bool IsSaturated { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 실제 결과가 모두 수집됐음을 보장하는지 나타낸다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsComplete => !IsSaturated;

        public DeliveryBatchResult(int count, bool isSaturated)
        {
            Count = Mathf.Max(0, count);
            IsSaturated = isSaturated;
        }

    }
}
