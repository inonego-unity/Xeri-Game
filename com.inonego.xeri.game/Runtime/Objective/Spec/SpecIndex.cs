/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : SpecIndex.cs
수정일 : 2026-11-25

# 설명
Spec만 알면 정해지는 조회표(Progress 소유자, 시간을 재는 Entry, 이력 대상, Entry 수)를 한 번 만들어 둔다.

# 특이사항, 제약사항
Spec은 불변이므로 처음 읽을 때 한 번 계산해 ObjectiveSpec이 보관하고 모든 Engine이 공유한다.
검증을 통과한 Spec을 전제로 하며, 읽기 전용으로만 쓴다. 집합은 해시 조회(Contains)를 쓰도록 HashSet으로 노출한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// Spec 트리를 한 번 돌아 만든 파생 조회표(색인).
    /// </summary>
    // ============================================================
    internal sealed class SpecIndex : SpecWalker
    {

    #region 조회표

        // ------------------------------------------------------------
        /// <summary>
        /// 로컬 Progress ID → 그 ID를 소유한 Task의 NodeID.
        /// </summary>
        // ------------------------------------------------------------
        internal IReadOnlyDictionary<string, string> ProgressOwners => progressOwners;
        private readonly Dictionary<string, string> progressOwners = new(StringComparer.Ordinal);

        // ------------------------------------------------------------
        /// <summary>
        /// 참여 시작 시각을 기록하도록 선언한 EntryID.
        /// </summary>
        // ------------------------------------------------------------
        internal HashSet<string> TimedEntryIDs => timedEntryIDs;
        private readonly HashSet<string> timedEntryIDs = new(StringComparer.Ordinal);

        // ------------------------------------------------------------
        /// <summary>
        /// 이력을 저장하는 Sequential Group의 NodeID → Group.
        /// </summary>
        // ------------------------------------------------------------
        internal IReadOnlyDictionary<string, TaskGroupSpec> HistoryGroups => historyGroups;
        private readonly Dictionary<string, TaskGroupSpec> historyGroups = new(StringComparer.Ordinal);

        // ------------------------------------------------------------
        /// <summary>
        /// Sequential Group 안에서 OncePassed로 선언한 EntryID.
        /// </summary>
        // ------------------------------------------------------------
        internal HashSet<string> OncePassedEntryIDs => oncePassedEntryIDs;
        private readonly HashSet<string> oncePassedEntryIDs = new(StringComparer.Ordinal);

        // ------------------------------------------------------------
        /// <summary>
        /// 트리 전체의 Entry 수.
        /// </summary>
        // ------------------------------------------------------------
        internal int EntryCount { get; private set; }

    #endregion

    #region 생성

        private SpecIndex()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 검증을 통과한 Spec의 조회표를 한 번 돌아서 만든다.
        /// </summary>
        // ------------------------------------------------------------
        internal static SpecIndex Build(ObjectiveSpec spec)
        {
            var index = new SpecIndex();
            index.Walk(spec.Root, null);
            return index;
        }

    #endregion

    #region 수집

        protected override void VisitTask(TaskSpec task, string path)
        {
            foreach (var progressID in task.LocalProgressIDs)
            {
                progressOwners.TryAdd(progressID, task.NodeID);
            }
        }

        protected override void VisitGroup(TaskGroupSpec group, string path)
        {
            if (group.ExecutionMode == TaskExecutionMode.Sequential)
            {
                historyGroups.TryAdd(group.NodeID, group);
                foreach (var entry in group.Entries)
                {
                    if (entry != null && entry.RevalidationMode == TaskRevalidationMode.OncePassed)
                    {
                        oncePassedEntryIDs.Add(entry.EntryID);
                    }
                }
            }

            base.VisitGroup(group, path);
        }

        protected override void VisitEntry(TaskGroupSpec group, TaskChildEntry entry, int index, string path)
        {
            EntryCount++;
            if (entry.TracksOpenTime)
            {
                timedEntryIDs.Add(entry.EntryID);
            }

            Descend(entry, path);
        }

    #endregion

    }
}
