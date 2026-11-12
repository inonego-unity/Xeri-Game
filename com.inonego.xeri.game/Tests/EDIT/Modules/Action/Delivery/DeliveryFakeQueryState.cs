/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : DeliveryFakeQueryState.cs
수정일 : 2026-11-12

# 설명
차원별 Projectile fake Query에서 공통으로 사용하는 검색 상태와 호출 횟수를 소유한다.
========================================================================= BLOCK_HEADER_END */

namespace inonego.Xeri.Game.TEST._Delivery
{
    // ============================================================
    /// <summary>
    /// 2D·3D Projectile Fake Query의 응답 상태와 호출 횟수를 보존한다.
    /// </summary>
    // ============================================================
    public class DeliveryFakeQueryState<TContact>
    where TContact : struct
    {

    #region 응답 상태

        public bool HasOverlap { get; set; }
        public bool HasCast { get; set; }
        public bool IsOverlapComplete { get; set; } = true;
        public bool IsCastComplete { get; set; } = true;
        public TContact OverlapContact { get; set; }
        public TContact CastContact { get; set; }

    #endregion

    #region 호출 횟수

        public int OverlapCount { get; private set; }
        public int CastCount { get; private set; }

        protected void RecordOverlap()
        {
            OverlapCount++;
        }

        protected void RecordCast()
        {
            CastCount++;
        }

    #endregion

    }
}
