/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : StatusContainer.cs
수정일 : 2026-11-20

# 설명
대상 하나에 적용된 Status의 스택·만료·소비·제거와 변경 알림을 소유한다.
스택마다 Core Timer를 하나씩 내부 저장소로 쓰고 읽기만 공개하며, 시간 진행 중에는 외부 코드를 호출하지 않는다.
변경 알림은 상태 변경이 끝난 뒤 OnChanged 하나로 보낸다.
Status 효과(피해·회복·행동 제한)는 게임이 알림과 조회로 처리한다.

# 특이사항, 제약사항
이산 변경마다 Revision을 한 번 올리며 시간 진행(Advance)의 만료는 Revision을 바꾸지 않는다.
한 Advance에서 여러 스택이 끝나면 남은 시간, 같으면 적용 순번 순서로 알린다.
CloneFrom은 인스턴스와 구독을 유지한 채 변경 알림 없이 복제한다.
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
        ISerializationCallbackReceiver, IDeepCloneable<StatusContainer<TOrigin>>
    where TOrigin : IEquatable<TOrigin>
    {

    #region 내부 데이터

        // 스택 하나의 시간과 적용 순번.
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
        private sealed class Entry : IReadOnlyStatus<TOrigin>
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

            internal Entry()
            {
                // NONE
            }

            internal Entry(StatusSpec spec, TOrigin origin)
            {
                this.spec   = spec;
                this.origin = origin;
            }

            internal Entry(Entry source)
            {
                spec   = source.spec.Copy();
                origin = source.origin;

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

            ITimerProgress IReadOnlyStatus<TOrigin>.GetStackTimer(int index) => stacks[index].Timer;
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
        public IReadOnlyList<IReadOnlyStatus<TOrigin>> Statuses => entries;

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
        public ulong Revision => revision;

        [NonSerialized]
        private ulong revision = 0;

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
        /// <br/> 복제 뒤 Revision을 1 올리며 인스턴스와 구독은 유지한다.
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

            entries.Clear();

            foreach (var entry in source.entries)
            {
                entries.Add(new Entry(entry));
            }

            nextSequence = source.nextSequence;
            revision++;
        }

    #endregion

    #region 적용

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> spec 규칙으로 origin의 Status를 stacks만큼 적용한다.
        /// <br/> 같은 Key의 Status가 다른 규칙으로 있으면 ArgumentException을 던진다.
        /// </summary>
        // ----------------------------------------------------------------------
        public StatusApplyResult Apply(StatusSpec spec, TOrigin origin, int stacks = 1)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            spec.Validate();

            if (stacks < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(stacks));
            }

            // 출처가 달라도 같은 Key는 같은 규칙이어야 한다.
            var sameKey = FindFirst(spec.Key);

            if (sameKey != null && !sameKey.Spec.Equals(spec))
            {
                throw new ArgumentException("같은 Key의 Status가 다른 규칙으로 적용되어 있습니다.", nameof(spec));
            }

            var target = spec.Sourcing == StatusSourcing.Shared
                ? sameKey
                : Find(spec.Key, origin);

            var changes = ListPool<StatusChange<TOrigin>>.Get();

            try
            {
                var result = target == null
                    ? AddEntry(spec, origin, stacks, changes)
                    : AddStacks(target, origin, stacks, changes);

                if (result == StatusApplyResult.Rejected)
                {
                    return result;
                }

                revision++;
                Raise(changes);

                return result;
            }
            finally
            {
                ListPool<StatusChange<TOrigin>>.Release(changes);
            }
        }

        // 새 Status를 만들고 최대 스택까지 추가한다.
        private StatusApplyResult AddEntry
        (
            StatusSpec spec,
            TOrigin origin,
            int stacks,
            List<StatusChange<TOrigin>> changes
        )
        {
            var entry = new Entry(spec.Copy(), origin);
            var count = Math.Min(stacks, spec.MaxStacks);

            for (var index = 0; index < count; index++)
            {
                entry.StackList.Add(CreateStack(entry.Spec, entry.Spec.Duration));
            }

            entries.Add(entry);
            changes.Add(new(StatusChangeKind.Added, spec.Key, origin, count, count, StatusRemoveReason.None));

            return StatusApplyResult.Added;
        }

        // 기존 Status에 Stacking 규칙대로 스택을 더하거나 시간을 갱신한다.
        private StatusApplyResult AddStacks
        (
            Entry entry,
            TOrigin origin,
            int stacks,
            List<StatusChange<TOrigin>> changes
        )
        {
            return entry.Spec.Stacking switch
            {
                StatusStacking.Independent => AddIndependentStacks(entry, origin, stacks, changes),
                StatusStacking.SharedRenew => AddRenewingStacks(entry, origin, stacks, changes),
                StatusStacking.SharedKeep  => AddKeepingStacks(entry, origin, stacks, changes),
                _                          => throw new ArgumentOutOfRangeException(nameof(entry)),
            };
        }

        // 스택을 더하고 새 스택과 기존 스택을 모두 처음부터 다시 흐르게 한다.
        private StatusApplyResult AddRenewingStacks
        (
            Entry entry,
            TOrigin origin,
            int stacks,
            List<StatusChange<TOrigin>> changes
        )
        {
            var spec = entry.Spec;
            var added = Math.Min(stacks, spec.MaxStacks - entry.StackList.Count);

            UpdateSharedOrigin(entry, origin);

            for (var index = 0; index < added; index++)
            {
                entry.StackList.Add(CreateStack(spec, spec.Duration));
            }

            RestartStacks(entry);

            var kind = added > 0 ? StatusChangeKind.Stacked : StatusChangeKind.Renewed;
            changes.Add(new(kind, spec.Key, entry.Origin, added, entry.StackList.Count, StatusRemoveReason.None));

            return added > 0 ? StatusApplyResult.Stacked : StatusApplyResult.Renewed;
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
                return StatusApplyResult.Rejected;
            }

            UpdateSharedOrigin(entry, origin);

            var remaining = entry.StackList[0].Timer.RemainingTime;

            for (var index = 0; index < added; index++)
            {
                entry.StackList.Add(CreateStack(spec, remaining));
            }

            changes.Add(new(StatusChangeKind.Stacked, spec.Key, entry.Origin, added, entry.StackList.Count, StatusRemoveReason.None));

            return StatusApplyResult.Stacked;
        }

        // 독립 만료 스택을 더하고, 최대치면 Overflow 규칙으로 가장 먼저 끝날 스택을 교체한다.
        private StatusApplyResult AddIndependentStacks
        (
            Entry entry,
            TOrigin origin,
            int stacks,
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
                return StatusApplyResult.Rejected;
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
                entry.StackList.Add(CreateStack(spec, spec.Duration));
            }

            changes.Add(new(StatusChangeKind.Stacked, spec.Key, entry.Origin, added, entry.StackList.Count, StatusRemoveReason.None));

            return StatusApplyResult.Stacked;
        }

        // Shared Status의 출처를 가장 최근 적용자로 바꾼다.
        private static void UpdateSharedOrigin(Entry entry, TOrigin origin)
        {
            if (entry.Spec.Sourcing == StatusSourcing.Shared)
            {
                entry.SetOrigin(origin);
            }
        }

        // 순번을 매긴 새 스택을 만들고 지속 시간이 있으면 시작한다.
        private StackRecord CreateStack(StatusSpec spec, float duration)
        {
            var stack = new StackRecord(nextSequence++);

            if (spec.HasDuration)
            {
                stack.Timer.Start(duration);
            }

            return stack;
        }

        // 모든 스택을 Spec 지속 시간으로 다시 시작한다.
        private static void RestartStacks(Entry entry)
        {
            if (!entry.Spec.HasDuration)
            {
                return;
            }

            foreach (var stack in entry.StackList)
            {
                stack.Timer.Stop();
                stack.Timer.Start(entry.Spec.Duration);
            }
        }

    #endregion

    #region 소비와 제거

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

                revision++;
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
                    candidates.Add(new StackCandidate(entry, stack, stack.GetRemainingOrder()));
                }
            }
        }

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
                revision++;
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

                revision++;
                Raise(changes);

                return true;
            }
            finally
            {
                ListPool<StatusChange<TOrigin>>.Release(changes);
            }
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

    #region 변경 알림

        // 모든 변경을 모든 구독자에게 보낸 뒤 구독자 예외를 전달한다.
        private void Raise(List<StatusChange<TOrigin>> changes)
        {
            var observers = OnChanged;

            if (observers == null || changes.Count == 0)
            {
                return;
            }

            var invocationList = observers.GetInvocationList();
            List<Exception> failures = null;

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
                        (failures ??= new List<Exception>()).Add(exception);
                    }
                }
            }

            if (failures == null)
            {
                return;
            }

            throw failures.Count == 1
                ? failures[0]
                : new AggregateException("Status 변경 알림 중 여러 구독자가 실패했습니다.", failures);
        }

    #endregion

    #region 직렬화

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
            revision++;
        }

    #endregion

    }
}
