/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveSubmitResult.cs
수정일 : 2026-11-14

# 설명
검증된 명시 Submit의 결과를 표현한다.

# 특이사항, 제약사항
None은 저장되는 ObjectiveStatus가 아니다.
========================================================================= BLOCK_HEADER_END */

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 현재 조건으로 검증된 제출에서 확정한 종료 결과.
    /// </summary>
    // ============================================================
    public enum ObjectiveSubmitResult { None, Succeeded, Failed }
}
