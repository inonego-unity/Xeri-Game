/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : GameplayOperation.cs
수정일 : 2026-11-03

# 설명
Execution 또는 Delivery 이후 project-domain 작업을 호출하는 공통 GameplayOperation 경계를 정의한다.
Damage·Heal·Buff 같은 의미는 해석하지 않고 provenance, recipient, project context와 결과만 전달한다.
========================================================================= BLOCK_HEADER_END */

using System;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Gameplay Operation 적용, 무변경 및 거부 결과의 분류.
    /// </summary>
    // ============================================================
    public enum GameplayOperationStatus
    {
        Rejected = 0,
        Applied = 1,
        NoOp = 2,
    }

    // ============================================================
    /// <summary>
    /// Operation 처리 상태와 거부 사유를 담는 읽기 전용 결과.
    /// </summary>
    // ============================================================
    public readonly struct GameplayOperationResult
    {
        public GameplayOperationStatus Status { get; }
        public string Reason { get; }
        public bool Succeeded => Status != GameplayOperationStatus.Rejected;
        public bool IsApplied => Status == GameplayOperationStatus.Applied;
        public bool IsNoOp => Status == GameplayOperationStatus.NoOp;

        private GameplayOperationResult
        (
            GameplayOperationStatus status,
            string reason
        )
        {
            Status = status;
            Reason = reason ?? string.Empty;
        }

        public static GameplayOperationResult Applied() =>
            new(GameplayOperationStatus.Applied, string.Empty);

        public static GameplayOperationResult NoOp(string reason = null) =>
            new(GameplayOperationStatus.NoOp, reason);

        public static GameplayOperationResult Reject(string reason = null) =>
            new(GameplayOperationStatus.Rejected, reason);
    }

    // ================================================================================
    /// <summary>
    /// Owner, Instigator, Origin, Recipient와 프로젝트 데이터를 전달하는 실행 맥락.
    /// </summary>
    // ================================================================================
    public readonly struct GameplayOperationContext<TRecipient, TProjectContext>
    {
        public object Owner { get; }
        public object Instigator { get; }
        public object Origin { get; }
        public TRecipient Recipient { get; }
        public TProjectContext Project { get; }

        public GameplayOperationContext
        (
            object owner,
            object instigator,
            object origin,
            TRecipient recipient,
            TProjectContext project
        )
        {
            Owner = owner;
            Instigator = instigator;
            Origin = origin;
            Recipient = recipient;
            Project = project;
        }
    }

    // ============================================================
    /// <summary>
    /// 구체 게임 의미를 코어에 넣지 않고 Recipient에 적용을 요청하는 계약.
    /// </summary>
    // ============================================================
    public interface IGameplayOperation<TRecipient, TProjectContext>
    {
        // ------------------------------------------------------------
        /// <summary>
        /// Recipient를 대상으로 상태 변경을 실행하고 결과 상태를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        GameplayOperationResult Invoke
        (
            in GameplayOperationContext<TRecipient, TProjectContext> context
        );
    }

    // ============================================================
    /// <summary>
    /// 명시적인 실행 맥락을 이용해 프로젝트 Operation을 호출한다.
    /// </summary>
    // ============================================================
    public static class GameplayOperationRunner
    {
        // ----------------------------------------------------------------------
        /// <summary>
        /// 주어진 Context를 전달해 Operation을 실행하고 명시적인 결과를 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public static GameplayOperationResult Invoke<TRecipient, TProjectContext>
        (
            IGameplayOperation<TRecipient, TProjectContext> operation,
            in GameplayOperationContext<TRecipient, TProjectContext> context
        )
        {
            return operation != null
                ? operation.Invoke(context)
                : GameplayOperationResult.Reject("GameplayOperation이 없습니다.");
        }
    }
}