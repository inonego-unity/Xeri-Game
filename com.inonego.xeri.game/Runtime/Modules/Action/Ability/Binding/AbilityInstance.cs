/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityInstance.cs
수정일 : 2026-11-08

# 설명
공유 Definition과 Owner별 AbilityState를 하나의 runtime Ability 단위로 조합한다.
한 번의 사용 실행은 ExecutionSession이 소유하며 AbilityInstance가 active cast를 의미하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 공유 Definition과 Owner별 State의 runtime 조합.
    /// </summary>
    // ============================================================
    public sealed class AbilityInstance<TDefinition>
    where TDefinition : class
    {

        // ------------------------------------------------------------
        /// <summary>
        /// 이 인스턴스가 소유한 Ability의 논리 Key를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityKey Key => State.Key;

        // ------------------------------------------------------------
        /// <summary>
        /// 여러 Owner가 공유할 수 있는 Ability Definition을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public TDefinition Definition { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 이 Owner에 귀속되는 AbilityState를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityState State { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 공유 Definition과 Owner별 AbilityState를 연결한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityInstance(TDefinition definition, AbilityState state)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            State = state ?? throw new ArgumentNullException(nameof(state));
        }
    }

    // ============================================================
    /// <summary>
    /// Entry Key와 실제 Ability Instance를 함께 보존하는 해석 결과.
    /// </summary>
    // ============================================================
    public readonly struct AbilityBinding<TDefinition>
    where TDefinition : class
    {

        // ------------------------------------------------------------
        /// <summary>
        /// 해석 당시의 입력 Entry Key를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityEntryKey EntryKey { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 해석 당시 선택된 Ability Instance를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityInstance<TDefinition> Instance { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// Entry Key와 실제 Ability Instance의 연결 결과를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityBinding
        (
            AbilityEntryKey entryKey,
            AbilityInstance<TDefinition> instance
        )
        {
            if (!entryKey.IsValid)
            {
                throw new ArgumentException("유효한 Ability Entry Key가 필요합니다.", nameof(entryKey));
            }

            EntryKey = entryKey;
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        }
    }
}