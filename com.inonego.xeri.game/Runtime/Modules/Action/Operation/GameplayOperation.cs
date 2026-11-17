/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : GameplayOperation.cs
수정일 : 2026-11-17

# 설명
Execution 또는 Delivery 이후 project-domain 작업을 호출하는 공통 GameplayOperation 경계를 정의한다.
Damage·Heal·Buff 같은 의미는 해석하지 않고 provenance, recipient, payload와 결과만 전달한다.
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
        // ------------------------------------------------------------
        /// <summary>
        /// 처리 결과의 분류를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public GameplayOperationStatus Status { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 거부 또는 무변경 사유를 반환한다. 사유가 없으면 빈 문자열이다.
        /// </summary>
        // ------------------------------------------------------------
        public string Reason { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 거부되지 않은 결과인지 반환한다. Applied와 NoOp이 해당한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Succeeded => Status != GameplayOperationStatus.Rejected;

        // ------------------------------------------------------------
        /// <summary>
        /// 실제 상태 변경이 적용되었는지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsApplied => Status == GameplayOperationStatus.Applied;

        // ------------------------------------------------------------
        /// <summary>
        /// 상태 변경 없이 무변경으로 끝났는지 반환한다.
        /// </summary>
        // ------------------------------------------------------------
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

        // ------------------------------------------------------------
        /// <summary>
        /// 상태 변경이 적용되었음을 나타내는 결과를 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public static GameplayOperationResult Applied() =>
            new(GameplayOperationStatus.Applied, string.Empty);

        // ------------------------------------------------------------
        /// <summary>
        /// 상태 변경 없이 무변경으로 끝났음을 나타내는 결과를 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public static GameplayOperationResult NoOp(string reason = null) =>
            new(GameplayOperationStatus.NoOp, reason);

        // ------------------------------------------------------------
        /// <summary>
        /// 거부 결과를 만든다. 거부 사유를 함께 전달할 수 있다.
        /// </summary>
        // ------------------------------------------------------------
        public static GameplayOperationResult Reject(string reason = null) =>
            new(GameplayOperationStatus.Rejected, reason);
    }

    // ======================================================================
    /// <summary>
    /// Owner, Instigator, Origin, Recipient와 Payload를 전달하는 실행 맥락.
    /// </summary>
    // ======================================================================
    public readonly struct GameplayOperationContext<TRecipient, TPayload>
    {
        // ------------------------------------------------------------
        /// <summary>
        /// 효과의 소유 주체를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public object Owner { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 효과를 일으킨 주체를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public object Instigator { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 효과가 시작된 출처를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public object Origin { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 효과를 적용받을 대상을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public TRecipient Recipient { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 프로젝트가 해석할 전달 데이터를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public TPayload Payload { get; }

        // ----------------------------------------------------------------------
        /// <summary>
        /// Owner, Instigator, Origin, Recipient와 Payload로 실행 맥락을 생성한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public GameplayOperationContext
        (
            object owner,
            object instigator,
            object origin,
            TRecipient recipient,
            TPayload payload
        )
        {
            Owner = owner;
            Instigator = instigator;
            Origin = origin;
            Recipient = recipient;
            Payload = payload;
        }
    }

    // ============================================================
    /// <summary>
    /// 구체 게임 의미를 코어에 넣지 않고 Recipient에 적용을 요청하는 계약.
    /// </summary>
    // ============================================================
    public interface IGameplayOperation<TRecipient, TPayload>
    {
        // ------------------------------------------------------------
        /// <summary>
        /// Recipient를 대상으로 상태 변경을 실행하고 결과 상태를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        GameplayOperationResult Invoke
        (
            in GameplayOperationContext<TRecipient, TPayload> context
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
        public static GameplayOperationResult Invoke<TRecipient, TPayload>
        (
            IGameplayOperation<TRecipient, TPayload> operation,
            in GameplayOperationContext<TRecipient, TPayload> context
        )
        {
            return operation != null
                ? operation.Invoke(context)
                : GameplayOperationResult.Reject("GameplayOperation이 없습니다.");
        }
    }
}