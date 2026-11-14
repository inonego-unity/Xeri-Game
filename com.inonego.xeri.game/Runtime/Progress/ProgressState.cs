/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ProgressState.cs
수정일 : 2026-11-14

# 설명
게임의 공용 진행 데이터를 Xeri Core ValueRegistry 하나에 보관한다.

# 특이사항, 제약사항
World/Account/Session 등 소유자가 독립적인 상태로 사용한다.
값 등록·조회·알림은 ValueRegistry를 직접 이용하며 목표 판정이나 복제는 제공하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.Progress
{
    // ============================================================
    /// <summary>
    /// 공용 진행값을 보유하는 영속 상태.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class ProgressState
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 자료형별 조회·변경과 변경 알림을 제공하는 공용 진행값 원본.
        /// </summary>
        // ------------------------------------------------------------
        public ValueRegistry Values =>
            values ?? throw new InvalidOperationException("Progress ValueRegistry is missing.");

        [SerializeField] private ValueRegistry values = new();

    #endregion

    }
}
