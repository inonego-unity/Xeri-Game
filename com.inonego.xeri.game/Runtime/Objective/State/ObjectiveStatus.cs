/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveStatus.cs
수정일 : 2026-11-13

# 설명
ObjectiveInstance의 영속 실행 상태를 정의한다.

# 특이사항, 제약사항
성공/실패의 판정 후보와 Submit의 None은 영속 Status가 아니다.
========================================================================= BLOCK_HEADER_END */

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// Objective 실행 인스턴스의 영속 상태.
    /// </summary>
    // ============================================================
    public enum ObjectiveStatus { Inactive, Active, Succeeded, Failed, Cancelled }
}
