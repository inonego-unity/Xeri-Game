/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : Zone.cs
수정일 : 2026-08-24

# 설명
ZoneGraph의 하나의 공간 node를 식별하는 직렬화 가능한 모델.

# 제약사항
Unity GameObject 수명, 콘텐츠 활성화, Actor 진입·이탈, Stage 진행 상태를 소유하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 공간 그래프에서 안정 ID로 식별되는 하나의 Zone node.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class Zone
    {
    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// ZoneGraph 안에서 이 Zone을 식별하는 안정 문자열.
        /// </summary>
        // ------------------------------------------------------------
        public string ZoneID => zoneID;

        [SerializeField]
        private string zoneID = "";

        // ------------------------------------------------------------
        /// <summary>
        /// 유효한 Zone ID가 설정됐는지 나타낸다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsDefined => !string.IsNullOrWhiteSpace(zoneID);

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Serializer용 기본 생성자.
        /// </summary>
        // ------------------------------------------------------------
        public Zone() { }

        // ------------------------------------------------------------
        /// <summary>
        /// 안정 문자열 ID로 Zone을 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public Zone(string zoneID)
        {
            if (string.IsNullOrWhiteSpace(zoneID))
            {
                throw new ArgumentException("Zone ID를 비워 둘 수 없습니다.", nameof(zoneID));
            }

            this.zoneID = zoneID;
        }

    #endregion

    #region 메서드

        public override string ToString()
        {
            return zoneID ?? string.Empty;
        }

    #endregion
    }
}
