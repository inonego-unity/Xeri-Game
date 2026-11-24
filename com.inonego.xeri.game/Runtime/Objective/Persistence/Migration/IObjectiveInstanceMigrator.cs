/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : IObjectiveInstanceMigrator.cs
수정일 : 2026-11-25

# 설명
등록된 Version이 없는 저장 Instance를 새 Version으로 옮겨 주는 선택 계약.

# 특이사항, 제약사항
원본 Instance는 바꾸지 않고 새 Instance를 돌려준다. Bind가 Resolver가 이 계약을 구현했을 때만 쓴다.
========================================================================= BLOCK_HEADER_END */

namespace inonego.Xeri.Game.Objective
{
    // ======================================================================
    /// <summary>
    /// 저장된 Instance를 현재 등록된 Version으로 옮기는 Resolver의 선택 인터페이스.
    /// </summary>
    // ======================================================================
    public interface IObjectiveInstanceMigrator
    {
        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>정확한 Version이 없는 Instance를 등록된 Version까지 옮긴다.
        /// <br/>옮길 변환이 없으면 false다.
        /// <br/>변환 결과가 유효하지 않으면 false로 숨기지 않고 InvalidOperationException으로
        /// <br/>거부한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        bool TryMigrate(ObjectiveInstance instance, out ObjectiveInstance migrated);
    }
}
