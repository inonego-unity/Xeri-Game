/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ProjectileDeliveryStepStatus.cs
수정일 : 2026-11-02

# 설명
2D·3D Projectile Delivery의 공통 이동·접촉·만료·검색 미완료 상태를 정의한다.
========================================================================= BLOCK_HEADER_END */

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Projectile의 이동·접촉·만료·검색 미완료 상태.
    /// </summary>
    // ============================================================
    public enum ProjectileDeliveryStepStatus
    {
        Active = 0,
        Contact = 1,
        Expired = 2,
        Incomplete = 3,
    }
}
