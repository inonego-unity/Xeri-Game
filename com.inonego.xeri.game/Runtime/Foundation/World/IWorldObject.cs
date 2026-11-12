/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : IWorldObject.cs
수정일 : 2026-08-24

# 설명
Unity Scene의 gameplay object와 직렬화 가능한 WorldObjectState를 연결하는 공통 계약.

# 제약사항
Stage, Save 파일 형식, Unity 표현 적용 방식은 이 계약이 소유하지 않는다.
========================================================================= BLOCK_HEADER_END */

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// persistent World Object State의 생성·Binding 수명을 제공한다.
    /// </summary>
    // ============================================================
    public interface IWorldObject
    {
        // ------------------------------------------------------------
        /// <summary>
        /// Scene Object와 persistent State를 연결하는 안정 ID.
        /// </summary>
        // ------------------------------------------------------------
        string ID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Scene authoring 초기값으로 새 persistent State를 만든다.
        /// </summary>
        // ------------------------------------------------------------
        WorldObjectState CreateState();

        // ------------------------------------------------------------
        /// <summary>
        /// 생성 또는 복원된 State를 이 World Object의 runtime authority로 연결한다.
        /// </summary>
        // ------------------------------------------------------------
        void BindState(WorldObjectState state);

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 State와의 runtime 연결을 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        void UnbindState();
    }
}
