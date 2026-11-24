/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : WorldObjectState.cs
수정일 : 2026-08-24

# 설명
World Object의 직렬화 가능한 지속 상태가 공유하는 최소 기반 타입.

# 제약사항
Unity Object reference와 표현 상태를 소유하지 않으며 concrete schema는 각 World Object가 정의한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 서로 다른 World Object State를 하나의 다형 컬렉션으로 보관하기 위한 기반 타입.
    /// </summary>
    // ============================================================
    [Serializable]
    public abstract class WorldObjectState
    {
    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 이 State가 대응하는 World Object의 안정 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string ID => id;

        [SerializeField]
        private string id = "";

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Serializer가 concrete State를 복원할 수 있도록 비어 있는 생성자를 제공한다.
        /// </summary>
        // ------------------------------------------------------------
        protected WorldObjectState() { }

        // ------------------------------------------------------------
        /// <summary>
        /// 새 World Object State를 안정 ID와 함께 만든다.
        /// </summary>
        // ------------------------------------------------------------
        protected WorldObjectState(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("World Object State에는 비어 있지 않은 ID가 필요합니다.", nameof(id));
            }

            this.id = id;
        }

    #endregion
    }
}
