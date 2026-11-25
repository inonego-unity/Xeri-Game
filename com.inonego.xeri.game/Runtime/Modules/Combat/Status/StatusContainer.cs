/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : StatusContainer.cs
수정일 : 2026-11-25

# 설명
대상 하나에 적용된 Status의 스택·만료·소비·제거와 변경 알림을 소유한다.
시간은 Core Timer를 내부 저장소로 쓰고 읽기만 공개한다. Independent는 스택마다 Timer 하나, SharedRenew·SharedKeep은 Status 하나가 공유하는 Timer 하나를 둔다.
시간 진행 중에는 외부 코드를 호출하지 않는다.
변경 알림은 상태 변경이 끝난 뒤 OnChanged 하나로 보낸다.
Status 효과(피해·회복·행동 제한)는 게임이 알림과 조회로 처리한다.

# 특이사항, 제약사항
이산 변경마다 Revision을 한 번 올리며 시간 진행(Advance)의 만료는 Revision을 바꾸지 않는다.
한 Advance에서 여러 스택이 끝나면 남은 시간, 같으면 적용 순번 순서로 알린다.
CloneFrom은 컨테이너 인스턴스와 OnChanged 구독을 유지한 채 변경 알림 없이 복제하며 Status 항목은 새로 만든다.
항목 복제는 Core List.CloneFrom에 맡겨 실패하면 컨테이너가 바뀌지 않는다. 복제 전에 읽어 둔 IReadOnlyStatus는 복제 뒤 낡은 값이다.
변경 목록과 계산용 목록은 Unity ListPool에서 빌려 쓰므로 반복 호출이 목록을 할당하지 않는다.
새 Status·새 스택 생성과 구독자가 있을 때 알림마다 만드는 호출 목록은 할당한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Pool;

namespace inonego.Xeri.Game
{
    using Xeri;
    using Xeri.Utility;

    // ================================================================================
    /// <summary>
    /// <br/> 대상 하나에 적용된 Status 목록과 스택·만료·소비 규칙을 소유한다.
    /// <br/> TOrigin은 게임이 정하는 출처 값이며 저장하려면 Unity 직렬화가 가능해야 한다.
    /// </summary>
    // ================================================================================
    [Serializable]
    public sealed class StatusContainer<TOrigin> :
        ISerializationCallbackReceiver, IDeepCloneable<StatusContainer<TOrigin>>, IRevisioned
    where TOrigin : IEquatable<TOrigin>
    {

    #region 내부 데이터

        // 스택 하나의 적용 순번과, Independent일 때만 쓰는 스택별 시간. 공유 시간 Status의 스택은 Timer를 시작하지 않는다.
        [Serializable]
        private sealed class StackRecord
        {
            internal Timer Timer => timer;

            [SerializeField]
            private Timer timer = new();

            internal ulong Sequence => sequence;

            [SerializeField]
            private ulong sequence = 0;

            internal StackRecord()
            {
                // NONE
            }

            internal StackRecord(ulong sequence)
            {
                this.sequence = sequence;
            }

            internal StackRecord(StackRecord source)
            {
                timer.CloneFrom(source.timer);
                sequence = source.sequence;
            }

            // 만료가 가까운 순서의 정렬 값. 시간으로 끝나지 않는 스택은 가장 뒤다.
            internal float GetRemainingOrder()
            {
                return timer.IsRunning || timer.IsPaused
                    ? timer.RemainingTime
                    : float.PositiveInfinity;
            }
        }

        // 같은 Key(PerOrigin이면 Key와 출처)의 Status 하나.
        [Serializable]
        private sealed class Entry : IReadOnlyStatus<TOrigin>, IDeepCloneable<Entry>
        {
            internal StatusSpec Spec => spec;

            [SerializeField]
            private StatusSpec spec = null;

            internal TOrigin Origin => origin;

            [SerializeField]
            private TOrigin origin = default;

            internal List<StackRecord> StackList => stacks;

            [SerializeField]
            private List<StackRecord> stacks = new();

            // 스택이 시간을 공유하는 Status(SharedRenew·SharedKeep)의 시간. Independent에서는 쓰지 않는다.
            internal Timer SharedTimer => sharedTimer;

            [SerializeField]
            private Timer sharedTimer = new();

            // 스택이 시간을 공유하는 Status인지 반환한다.
            internal bool IsShared => spec != null && spec.Stacking != StatusStacking.Independent;

            // 만료가 가까운 순서의 정렬 값. 시간으로 끝나지 않는 스택은 가장 뒤다.
            internal float GetRemainingOrder(StackRecord stack)
            {
                if (!IsShared)
                {
                    return stack.GetRemainingOrder();
                }

                return sharedTimer.IsRunning || sharedTimer.IsPaused
                    ? sharedTimer.RemainingTime
                    : float.PositiveInfinity;
            }

            internal Entry()
            {
                // NONE
            }

            internal Entry(StatusSpec spec, TOrigin origin)
            {
                this.spec   = spec;
                this.origin = origin;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 컬렉션 복제가 새 항목을 만들 때 쓰는 빈 항목을 생성한다.
            /// </summary>
            // ------------------------------------------------------------
            public Entry @new() => new();

            // ------------------------------------------------------------
            /// <summary>
            /// source의 Spec·출처·스택 시간을 이 항목으로 복사한다.
            /// </summary>
            // ------------------------------------------------------------
            public void CloneFrom(Entry source)
            {
                spec   = source.spec.Copy();
                origin = source.origin;

                sharedTimer.CloneFrom(source.sharedTimer);
                stacks.Clear();

                foreach (var stack in source.stacks)
                {
                    stacks.Add(new StackRecord(stack));
                }
            }

            internal void SetOrigin(TOrigin origin)
            {
                this.origin = origin;
            }

            StatusSpec IReadOnlyStatus<TOrigin>.Spec => spec;

            TOrigin IReadOnlyStatus<TOrigin>.Origin => origin;

            int IReadOnlyStatus<TOrigin>.Stacks => stacks.Count;

            ITimerProgress IReadOnlyStatus<TOrigin>.GetStackTimer(int index)
            {
                if ((uint)index >= (uint)stacks.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return IsShared ? SharedTimer : stacks[index].Timer;
            }
        }

        // 만료·소비 후보 스택과 정렬 값.
        private readonly struct StackCandidate
        {
            internal Entry Entry { get; }

            internal StackRecord Stack { get; }

            internal float RemainingOrder { get; }

            internal StackCandidate(Entry entry, StackRecord stack, float remainingOrder)
            {
                Entry          = entry;
                Stack          = stack;
                RemainingOrder = remainingOrder;
            }

            // 남은 시간이 짧은 순서, 같으면 먼저 적용된 순서.
            internal static int Compare(StackCandidate left, StackCandidate right)
            {
                var byRemaining = left.RemainingOrder.CompareTo(right.RemainingOrder);

                return byRemaining != 0
                    ? byRemaining
                    : left.Stack.Sequence.CompareTo(right.Stack.Sequence);
            }
        }

        private static readonly EqualityComparer<TOrigin> originComparer = EqualityComparer<TOrigin>.Default;

        // 만료·소비 후보 정렬. 호출마다 대리자를 만들지 않도록 한 번만 만든다.
        private static readonly Comparison<StackCandidate> candidateComparison = StackCandidate.Compare;

    #endregion

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 적용 순서대로 현재 Status 목록을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public ReadOnlyListView<IReadOnlyStatus<TOrigin>> Statuses => new(entries);

        [SerializeField]
        private List<Entry> entries = new();

        // 다음 스택에 매길 적용 순번.
        [SerializeField]
        private ulong nextSequence = 1;

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 실제 이산 변경마다 증가하는 변경 번호를 반환한다.
        /// <br/> Advance의 시간 진행과 만료는 변경 번호를 바꾸지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public Revision Revision => revision;

        [NonSerialized]
        private Revision revision = Revision.None;

    #endregion

    #region 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// 상태 변경이 끝난 뒤 변경 한 건마다 발생한다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<StatusContainer<TOrigin>, StatusChange<TOrigin>> OnChanged = null;

    #endregion

    #region 복제

        // ------------------------------------------------------------
        /// <summary>
        /// 빈 StatusContainer 인스턴스를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public StatusContainer<TOrigin> @new() => new();

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> source의 Status·스택 시간·적용 순번을 변경 알림 없이 복제한다.
        /// <br/> 복제 뒤 Revision을 1 올리며 컨테이너 인스턴스와 구독은 유지한다.
        /// <br/> Status 항목은 새로 만든다.
        /// <br/> 항목 복제가 실패하면 컨테이너와 Revision은 바뀌지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void CloneFrom(StatusContainer<TOrigin> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (ReferenceEquals(source, this))
            {
                return;
            }

            // 새 항목 복제는 Core List.CloneFrom이 모두 준비한 뒤에만 반영하므로 실패하면 아래 값도 바뀌지 않는다.
            entries.CloneFrom(source.entries);

            nextSequence = source.nextSequence;
            revision = revision.Inc();
        }

    #endregion

    #region 적용

        // ------------------------------------------------------------
        /// <summary>
        /// 스택 하나를 spec 규칙으로 origin에 적용한다.
        /// </summary>
        // ------------------------------------------------------------
        public StatusApplyResult Apply(StatusSpec spec, TOrigin origin)
        {
            return Apply(spec, origin, new StatusApply(1));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> spec 규칙으로 origin의 Status를 apply.Stacks만큼 적용한다.
        /// <br/> apply.Duration을 주면 이번 적용이 시작하는 스택 시간에 Spec.Duration 대신 쓴다.
        /// <br/> SharedRenew는 다시 시작하는 모든 스택에, SharedKeep은 처음 적용에만 쓴다.
        /// <br/> 같은 Key의 Status가 다른 규칙으로 있으면 ArgumentException을 던진다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public StatusApplyResult Apply(StatusSpec spec, TOrigin origin, in StatusApply apply)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            spec.Validate();

            var stacks = apply.Stacks;

            if (stacks < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(apply), "적용할 스택 수는 1 이상이어야 합니다.");
            }

            var applyDuration = ResolveDuration(spec, apply.Duration);

            // 출처가 달라도 같은 Key는 같은 규칙이어야 한다.
            var sameKey = FindFirst(spec.Key);

            if (sameKey != null && !sameKey.Spec.Equals(spec))
            {
                throw new ArgumentException("같은 Key의 Status가 다른 규칙으로 적용되어 있습니다.", nameof(spec));
            }

            var target = spec.Sourcing == StatusSourcing.PerKey
                ? sameKey
                : Find(spec.Key, origin);

            var changes = ListPool<StatusChange<TOrigin>>.Get();

            try
            {
                var result = target == null
                    ? AddEntry(spec, origin, stacks, applyDuration, changes)
                    : AddStacks(target, origin, stacks, applyDuration, changes);

                if (result.IsRejected)
                {
                    return result;
                }

                revision = revision.Inc();
                Raise(changes);

                return result;
            }
            finally
            {
                ListPool<StatusChange<TOrigin>>.Release(changes);
            }
        }

        // 이번 적용의 스택 시간. 지정값은 지속 시간이 있는 Spec에만, 유한한 양수로만 받는다.
        private static float ResolveDuration(StatusSpec spec, float? duration)
        {
            if (!duration.HasValue)
            {
                return spec.Duration;
            }

            if (!spec.HasDuration)
            {
                throw new ArgumentException("지속 시간이 없는 Status에는 지속 시간을 지정할 수 없습니다.", nameof(duration));
            }

            var value = duration.Value;

            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            return value;
        }

        // 새 Status를 만들고 최대 스택까지 추가한다.
        private StatusApplyResult AddEntry
        (
            StatusSpec spec,
            TOrigin origin,
            int stacks,
            float duration,
            List<StatusChange<TOrigin>> changes
        )
        {
            var entry = new Entry(spec.Copy(), origin);
            var count = Math.Min(stacks, spec.MaxStacks);

            for (var index = 0; index < count; index++)
            {
                entry.StackList.Add(CreateStack(entry, duration));
            }

            StartSharedTimer(entry, duration);

            entries.Add(entry);
            changes.Add(new(StatusChangeKind.Added, spec.Key, origin, count, count, StatusRemoveReason.None));

            return new StatusApplyResult(StatusApplyOutcome.Added, count);
        }

        // 기존 Status에 Stacking 규칙대로 스택을 더하거나 시간을 갱신한다.
        private StatusApplyResult AddStacks
        (
            Entry entry,
            TOrigin origin,
            int stacks,
            float duration,
            List<StatusChange<TOrigin>> changes
        )
        {
            return entry.Spec.Stacking switch
            {
                StatusStacking.Independent => AddIndependentStacks(entry, origin, stacks, duration, changes),
                StatusStacking.SharedRenew => AddRenewingStacks(entry, origin, stacks, duration, changes),
                StatusStacking.SharedKeep  => AddKeepingStacks(entry, origin, stacks, changes),
                _                          => throw new ArgumentOutOfRangeException(nameof(entry)),
            };
        }

        // 스택을 더하고 새 스택과 기존 스택을 모두 이번 적용 시간으로 다시 흐르게 한다. 최대치에서는 Overflow가 Renew면 시간만 갱신하고 Reject면 거절한다.
        private StatusApplyResult AddRenewingStacks
        (
            Entry entry,
            TOrigin origin,
            int stacks,
            float duration,
            List<StatusChange<TOrigin>> changes
        )
        {
            var spec = entry.Spec;
            var added = Math.Min(stacks, spec.MaxStacks - entry.StackList.Count);

            // 최대치에서 거절하는 규칙이면 출처도 시간도 바꾸지 않는다.
            if (added == 0 && spec.EffectiveOverflow == StatusOverflow.Reject)
            {
                return new StatusApplyResult(StatusApplyOutcome.Rejected);
            }

            UpdateSharedOrigin(entry, origin);

            for (var index = 0; index < added; index++)
            {
                entry.StackList.Add(CreateStack(entry, duration));
            }

            RestartTime(entry, duration);

            var kind = added > 0 ? StatusChangeKind.Stacked : StatusChangeKind.Renewed;
            changes.Add(new(kind, spec.Key, entry.Origin, added, entry.StackList.Count, StatusRemoveReason.None));

            return new StatusApplyResult(added > 0 ? StatusApplyOutcome.Stacked : StatusApplyOutcome.Renewed, added);
        }

        // 기존 스택과 같은 시점에 끝나는 스택을 더한다. 최대치면 거절한다.
        private StatusApplyResult AddKeepingStacks
        (
            Entry entry,
            TOrigin origin,
            int stacks,
            List<StatusChange<TOrigin>> changes
        )
        {
            var spec = entry.Spec;
            var added = Math.Min(stacks, spec.MaxStacks - entry.StackList.Count);

            if (added == 0)
            {
                return new StatusApplyResult(StatusApplyOutcome.Rejected);
            }

            UpdateSharedOrigin(entry, origin);

            // 새 스택은 공유 시간에 함께 끝나므로 시간을 따로 만들지 않는다.
            for (var index = 0; index < added; index++)
            {
                entry.StackList.Add(CreateStack(entry, 0f));
            }

            changes.Add(new(StatusChangeKind.Stacked, spec.Key, entry.Origin, added, entry.StackList.Count, StatusRemoveReason.None));

            return new StatusApplyResult(StatusApplyOutcome.Stacked, added);
        }

        // 독립 만료 스택을 더하고, 최대치면 Overflow 규칙으로 가장 먼저 끝날 스택을 교체한다.
        private StatusApplyResult AddIndependentStacks
        (
            Entry entry,
            TOrigin origin,
            int stacks,
            float duration,
            List<StatusChange<TOrigin>> changes
        )
        {
            var spec = entry.Spec;
            var before = entry.StackList.Count;
            var replaces = spec.Overflow == StatusOverflow.ReplaceEarliest;

            // 교체 규칙이면 한 번에 최대 스택 수까지 새로 넣고 넘치는 만큼 기존 스택을 밀어낸다.
            var added = replaces
                ? Math.Min(stacks, spec.MaxStacks)
                : Math.Min(stacks, spec.MaxStacks - before);

            if (added == 0)
            {
                return new StatusApplyResult(StatusApplyOutcome.Rejected);
            }

            UpdateSharedOrigin(entry, origin);

            var overflowed = Math.Max(0, before + added - spec.MaxStacks);

            if (overflowed > 0)
            {
                RemoveEarliest(entry, overflowed);
                changes.Add(new(StatusChangeKind.StacksRemoved, spec.Key, entry.Origin, -overflowed, entry.StackList.Count, StatusRemoveReason.Overflowed));
            }

            for (var index = 0; index < added; index++)
            {
                entry.StackList.Add(CreateStack(entry, duration));
            }

            changes.Add(new(StatusChangeKind.Stacked, spec.Key, entry.Origin, added, entry.StackList.Count, StatusRemoveReason.None));

            return new StatusApplyResult(StatusApplyOutcome.Stacked, added, overflowed);
        }

    #endregion

    #region 소비

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> key의 모든 출처에서 가장 먼저 끝날 스택부터 count만큼 줄인다.
        /// <br/> 실제로 줄인 스택 수를 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public int Consume(StatusKey key, int count)
        {
            return ConsumeStacks(key, false, default, count);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> key와 origin의 Status에서 가장 먼저 끝날 스택부터 count만큼 줄인다.
        /// <br/> 실제로 줄인 스택 수를 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public int Consume(StatusKey key, TOrigin origin, int count)
        {
            return ConsumeStacks(key, true, origin, count);
        }

        // 조건에 맞는 Status들의 스택을 만료가 가까운 순서로 줄이고 Status별로 알린다.
        private int ConsumeStacks(StatusKey key, bool matchesOrigin, TOrigin origin, int count)
        {
            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            var targets = ListPool<Entry>.Get();
            var beforeCounts = ListPool<int>.Get();
            var candidates = ListPool<StackCandidate>.Get();
            var changes = ListPool<StatusChange<TOrigin>>.Get();

            try
            {
                CollectConsumeTargets(key, matchesOrigin, origin, targets, beforeCounts, candidates);

                var consumed = Math.Min(count, candidates.Count);

                if (consumed == 0)
                {
                    return 0;
                }

                candidates.Sort(candidateComparison);

                for (var index = 0; index < consumed; index++)
                {
                    candidates[index].Entry.StackList.Remove(candidates[index].Stack);
                }

                // Status별로 줄어든 수를 적용 순서대로 알린다.
                for (var index = 0; index < targets.Count; index++)
                {
                    var entry = targets[index];
                    var removed = beforeCounts[index] - entry.StackList.Count;

                    if (removed > 0)
                    {
                        changes.Add(CreateRemovalChange(entry, removed, StatusRemoveReason.Consumed));
                    }
                }

                revision = revision.Inc();
                Raise(changes);

                return consumed;
            }
            finally
            {
                ListPool<Entry>.Release(targets);
                ListPool<int>.Release(beforeCounts);
                ListPool<StackCandidate>.Release(candidates);
                ListPool<StatusChange<TOrigin>>.Release(changes);
            }
        }

        // 조건에 맞는 Status와 그 스택을 소비 후보로 모은다.
        private void CollectConsumeTargets
        (
            StatusKey key,
            bool matchesOrigin,
            TOrigin origin,
            List<Entry> targets,
            List<int> beforeCounts,
            List<StackCandidate> candidates
        )
        {
            foreach (var entry in entries)
            {
                if (!Matches(entry, key, matchesOrigin, origin))
                {
                    continue;
                }

                targets.Add(entry);
                beforeCounts.Add(entry.StackList.Count);

                foreach (var stack in entry.StackList)
                {
                    candidates.Add(new StackCandidate(entry, stack, entry.GetRemainingOrder(stack)));
                }
            }
        }

    #endregion

    #region 제거

        // ------------------------------------------------------------
        /// <summary>
        /// key의 모든 출처 Status를 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Remove(StatusKey key)
        {
            return RemoveEntries(key, false, default, StatusRemoveReason.Removed);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// key와 origin의 Status를 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Remove(StatusKey key, TOrigin origin)
        {
            return RemoveEntries(key, true, origin, StatusRemoveReason.Removed);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 모든 Status를 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Clear()
        {
            if (entries.Count == 0)
            {
                return;
            }

            var changes = ListPool<StatusChange<TOrigin>>.Get();

            try
            {
                foreach (var entry in entries)
                {
                    changes.Add(new(StatusChangeKind.Removed, entry.Spec.Key, entry.Origin, -entry.StackList.Count, 0, StatusRemoveReason.Cleared));
                }

                entries.Clear();
                revision = revision.Inc();
                Raise(changes);
            }
            finally
            {
                ListPool<StatusChange<TOrigin>>.Release(changes);
            }
        }

        // 조건에 맞는 Status를 모두 제거하고 적용 순서대로 알린다.
        private bool RemoveEntries(StatusKey key, bool matchesOrigin, TOrigin origin, StatusRemoveReason reason)
        {
            var changes = ListPool<StatusChange<TOrigin>>.Get();

            try
            {
                for (var index = 0; index < entries.Count; index++)
                {
                    var entry = entries[index];

                    if (!Matches(entry, key, matchesOrigin, origin))
                    {
                        continue;
                    }

                    changes.Add(new(StatusChangeKind.Removed, key, entry.Origin, -entry.StackList.Count, 0, reason));
                    entries.RemoveAt(index);
                    index--;
                }

                if (changes.Count == 0)
                {
                    return false;
                }

                revision = revision.Inc();
                Raise(changes);

                return true;
            }
            finally
            {
                ListPool<StatusChange<TOrigin>>.Release(changes);
            }
        }

    #endregion

    #region 조회

        // ------------------------------------------------------------
        /// <summary>
        /// key의 모든 출처 Status 스택 수의 합을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public int CountStacks(StatusKey key)
        {
            var count = 0;

            foreach (var entry in entries)
            {
                if (entry.Spec.Key == key)
                {
                    count += entry.StackList.Count;
                }
            }

            return count;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// key와 origin의 Status 스택 수를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public int CountStacks(StatusKey key, TOrigin origin)
        {
            return Find(key, origin)?.StackList.Count ?? 0;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// key와 origin의 Status를 찾는다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGet(StatusKey key, TOrigin origin, out IReadOnlyStatus<TOrigin> status)
        {
            status = Find(key, origin);
            return status != null;
        }

    #endregion

    #region 시간 진행

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 모든 스택 시계를 deltaTime만큼 진행하고 끝난 스택을 지운다.
        /// <br/> 진행 중에는 외부 코드를 호출하지 않고 끝난 뒤 만료를 알린다.
        /// </summary>
        // ------------------------------------------------------------
        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            var clampedDeltaTime = Mathf.Max(0f, deltaTime);
            var expired = ListPool<StackCandidate>.Get();
            var changes = ListPool<StatusChange<TOrigin>>.Get();

            try
            {
                CollectExpiredStacks(clampedDeltaTime, expired);

                if (expired.Count == 0)
                {
                    return;
                }

                expired.Sort(candidateComparison);

                // 같은 Status가 연속으로 끝나면 한 변경으로 묶어 만료 순서대로 알린다.
                var cursor = 0;

                while (cursor < expired.Count)
                {
                    var entry = expired[cursor].Entry;
                    var removed = 0;

                    while (cursor < expired.Count && ReferenceEquals(expired[cursor].Entry, entry))
                    {
                        entry.StackList.Remove(expired[cursor].Stack);
                        removed++;
                        cursor++;
                    }

                    changes.Add(CreateRemovalChange(entry, removed, StatusRemoveReason.Expired));
                }

                Raise(changes);
            }
            finally
            {
                ListPool<StackCandidate>.Release(expired);
                ListPool<StatusChange<TOrigin>>.Release(changes);
            }
        }

        // 모든 스택 시계를 진행하고 끝난 스택을 모은다.
        // Tick은 지속 시간에서 멈추므로 끝나기 전 남은 시간을 순서 값으로 기록한다.
        private void CollectExpiredStacks(float deltaTime, List<StackCandidate> expired)
        {
            foreach (var entry in entries)
            {
                // 공유 시간 Status는 시간을 한 번만 진행하고 끝나면 모든 스택이 함께 만료된다.
                if (entry.IsShared)
                {
                    var shared = entry.SharedTimer;

                    if (!shared.IsRunning)
                    {
                        continue;
                    }

                    var sharedRemaining = shared.RemainingTime;
                    shared.Tick(deltaTime);

                    if (!shared.IsRunning)
                    {
                        foreach (var stack in entry.StackList)
                        {
                            expired.Add(new StackCandidate(entry, stack, sharedRemaining));
                        }
                    }

                    continue;
                }

                foreach (var stack in entry.StackList)
                {
                    var timer = stack.Timer;

                    if (!timer.IsRunning)
                    {
                        continue;
                    }

                    var remaining = timer.RemainingTime;
                    timer.Tick(deltaTime);

                    if (!timer.IsRunning)
                    {
                        expired.Add(new StackCandidate(entry, stack, remaining));
                    }
                }
            }
        }

    #endregion

    #region 직렬화 복원

        // ------------------------------------------------------------
        /// <summary>
        /// 직렬화 전 처리. 보관 값이 그대로 직렬화된다.
        /// </summary>
        // ------------------------------------------------------------
        public void OnBeforeSerialize()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 직렬화 복원 뒤 Revision을 1 올린다.
        /// </summary>
        // ------------------------------------------------------------
        public void OnAfterDeserialize()
        {
            entries ??= new();
            revision = revision.Inc();
        }

    #endregion

    #region 내부 처리

        // PerKey Status의 출처를 가장 최근 적용자로 바꾼다.
        private static void UpdateSharedOrigin(Entry entry, TOrigin origin)
        {
            if (entry.Spec.Sourcing == StatusSourcing.PerKey)
            {
                entry.SetOrigin(origin);
            }
        }

        // 순번을 매긴 새 스택을 만든다. Independent는 지속 시간이 있으면 스택 시간을 시작한다.
        private StackRecord CreateStack(Entry entry, float duration)
        {
            var stack = new StackRecord(nextSequence++);

            if (!entry.IsShared && entry.Spec.HasDuration)
            {
                stack.Timer.Start(duration);
            }

            return stack;
        }

        // 공유 시간 Status의 시간을 처음 시작한다.
        private static void StartSharedTimer(Entry entry, float duration)
        {
            if (entry.IsShared && entry.Spec.HasDuration)
            {
                entry.SharedTimer.Start(duration);
            }
        }

        // 공유 시간 Status의 시간을 duration으로 다시 시작한다. 스택별 시간은 건드리지 않는다.
        private static void RestartTime(Entry entry, float duration)
        {
            if (!entry.IsShared || !entry.Spec.HasDuration)
            {
                return;
            }

            entry.SharedTimer.Stop();
            entry.SharedTimer.Start(duration);
        }

        // 가장 먼저 끝날 스택부터 count만큼 지운다.
        private static void RemoveEarliest(Entry entry, int count)
        {
            var candidates = ListPool<StackCandidate>.Get();

            try
            {
                foreach (var stack in entry.StackList)
                {
                    candidates.Add(new StackCandidate(entry, stack, stack.GetRemainingOrder()));
                }

                candidates.Sort(candidateComparison);

                for (var index = 0; index < count; index++)
                {
                    entry.StackList.Remove(candidates[index].Stack);
                }
            }
            finally
            {
                ListPool<StackCandidate>.Release(candidates);
            }
        }

        // 스택이 줄어든 Status의 변경을 만들고 스택이 0이면 목록에서 지운다.
        private StatusChange<TOrigin> CreateRemovalChange(Entry entry, int removed, StatusRemoveReason reason)
        {
            var stacks = entry.StackList.Count;

            if (stacks > 0)
            {
                return new(StatusChangeKind.StacksRemoved, entry.Spec.Key, entry.Origin, -removed, stacks, reason);
            }

            entries.Remove(entry);

            return new(StatusChangeKind.Removed, entry.Spec.Key, entry.Origin, -removed, 0, reason);
        }

        private Entry FindFirst(StatusKey key)
        {
            foreach (var entry in entries)
            {
                if (entry.Spec.Key == key)
                {
                    return entry;
                }
            }

            return null;
        }

        private Entry Find(StatusKey key, TOrigin origin)
        {
            foreach (var entry in entries)
            {
                if (Matches(entry, key, true, origin))
                {
                    return entry;
                }
            }

            return null;
        }

        private static bool Matches(Entry entry, StatusKey key, bool matchesOrigin, TOrigin origin)
        {
            return
                entry.Spec.Key == key &&
                (!matchesOrigin || originComparer.Equals(entry.Origin, origin));
        }

        // 모든 변경을 모든 구독자에게 보낸 뒤 구독자 예외를 전달한다.
        private void Raise(List<StatusChange<TOrigin>> changes)
        {
            var observers = OnChanged;

            if (observers == null || changes.Count == 0)
            {
                return;
            }

            var invocationList = observers.GetInvocationList();
            var failures = new ExceptionBuffer();

            foreach (var change in changes)
            {
                foreach (Action<StatusContainer<TOrigin>, StatusChange<TOrigin>> observer in invocationList)
                {
                    try
                    {
                        observer(this, change);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
            }

            // 구독자 예외 하나는 원래 스택 추적을 유지한 채 다시 던진다.
            failures.ThrowWhenAny("Status 변경 알림 중 여러 구독자가 실패했습니다.");
        }

    #endregion

    }
}
