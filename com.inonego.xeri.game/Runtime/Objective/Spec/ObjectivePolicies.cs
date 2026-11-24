/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectivePolicies.cs
수정일 : 2026-11-24

# 설명
Objective 및 TaskGroup/Entry에서 사용하는 정적 실행 정책 값.

# 특이사항, 제약사항
실행 결과와 참여 여부를 저장하는 상태 enum이 아니다.
========================================================================= BLOCK_HEADER_END */

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 성공과 실패의 최종 확정 방식을 별도로 설정한다.
    /// </summary>
    // ============================================================
    public enum ObjectiveCommitMode { Automatic, Manual }

    // ============================================================
    /// <summary>
    /// 종료한 실행을 새 Instance로 다시 생성할 수 있는지 지정한다.
    /// </summary>
    // ============================================================
    public enum ObjectiveRepeatMode { Once, Repeatable }

    // ============================================================
    /// <summary>
    /// Group 하위 Entry의 최초 개방 순서 정책.
    /// </summary>
    // ============================================================
    public enum TaskExecutionMode { Parallel, Sequential }

    // ============================================================
    /// <summary>
    /// Group 완료 이후 선택 Progress를 계속 수락할지 지정한다.
    /// </summary>
    // ============================================================
    public enum TaskContinuationMode { StopOnSucceeded, ContinueWhileActive }

    // ============================================================
    /// <summary>
    /// Sequential Entry의 이전 완료 재검증 방식.
    /// </summary>
    // ============================================================
    public enum TaskRevalidationMode { WhileValid, OncePassed }

    // ============================================================
    /// <summary>
    /// 후퇴로 닫힌 Entry의 독점 Progress를 초기화할지 지정한다.
    /// </summary>
    // ============================================================
    public enum TaskRollbackMode { Preserve, ResetOnRollback }
}
