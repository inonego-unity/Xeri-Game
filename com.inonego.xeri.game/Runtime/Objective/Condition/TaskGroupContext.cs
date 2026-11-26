/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TaskGroupContext.cs
수정일 : 2026-11-26

# 설명
Group Condition이 현재 참여하는 하위 Entry 결과를 조회하는 읽기 전용 Context.

# 특이사항, 제약사항
참여하지 않는 Entry는 완료·실패에 집계되지 않는다. 입력 허용 여부는 Engine 책임이다.
Engine은 평가마다 같은 Context를 다시 채워 쓰므로 Condition은 Context를 그 판정 밖으로 보관하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Pool;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// Condition 평가 시점의 하위 Node 결과.
    /// </summary>
    // ============================================================
    public enum TaskResult { Pending, Succeeded, Failed }

    // ============================================================
    /// <summary>
    /// 하위 Entry의 참여와 원시 결과를 분리하여 전달한다.
    /// </summary>
    // ============================================================
    public readonly struct TaskEntryResult
    {

    #region 프로퍼티

        // ------------------------------------------------------------
        /// <summary>
        /// 직접 자식 Entry의 안정적인 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string EntryID { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 부모 Group 완료 집계에 참여하는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool Participating { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Entry의 원시 Node 평가 결과.
        /// </summary>
        // ------------------------------------------------------------
        public TaskResult Result { get; }

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 한 Entry의 식별자와 결과를 검증해 설정한다.
        /// </summary>
        // ------------------------------------------------------------
        public TaskEntryResult(string entryID, bool participating, TaskResult result)
        {
            if (string.IsNullOrWhiteSpace(entryID))
            {
                throw new ArgumentException("Entry ID is required.", nameof(entryID));
            }

            if (!result.IsDefined())
            {
                throw new ArgumentOutOfRangeException(nameof(result));
            }

            EntryID = entryID;
            Participating = participating;
            Result = result;
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// TaskGroup의 하위 유효 결과와 Objective 판정 정보를 제공한다.
    /// </summary>
    // ============================================================
    public sealed class TaskGroupContext
    {

    #region 프로퍼티

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 평가 시점의 Objective 정보.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveContext Objective { get; private set; }

        // ------------------------------------------------------------
        /// <summary>
        /// 참여하지 않는 항목을 포함한 직접 자식의 전체 수.
        /// </summary>
        // ------------------------------------------------------------
        public int EntryCount => entries.Count;

        // ------------------------------------------------------------
        /// <summary>
        /// 실제 참여하며 완료된 자식 수.
        /// </summary>
        // ------------------------------------------------------------
        public int SucceededCount { get; private set; }

        // ------------------------------------------------------------
        /// <summary>
        /// 실제 참여하며 실패한 자식 수.
        /// </summary>
        // ------------------------------------------------------------
        public int FailedCount { get; private set; }

        private IReadOnlyList<TaskEntryResult> entries = Array.Empty<TaskEntryResult>();

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Group의 각 직접 자식 결과를 방어적으로 복사하고 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        public TaskGroupContext(ObjectiveContext objective, IEnumerable<TaskEntryResult> entryResults)
        {
            Objective = objective ?? throw new ArgumentNullException(nameof(objective));
            if (entryResults == null)
            {
                throw new ArgumentNullException(nameof(entryResults));
            }

            var values = new List<TaskEntryResult>();
            var ids = HashSetPool<string>.Get();
            int completed = 0;
            int failed = 0;

            try
            {
                foreach (var entry in entryResults)
                {
                    if (string.IsNullOrWhiteSpace(entry.EntryID) || !ids.Add(entry.EntryID)
                        || !entry.Result.IsDefined())
                    {
                        throw new ArgumentException("Duplicate or invalid Entry result.", nameof(entryResults));
                    }

                    values.Add(entry);
                    if (entry.Participating && entry.Result == TaskResult.Succeeded)
                    {
                        completed++;
                    }

                    if (entry.Participating && entry.Result == TaskResult.Failed)
                    {
                        failed++;
                    }
                }
            }
            finally
            {
                HashSetPool<string>.Release(ids);
            }

            entries = values.AsReadOnly();
            SucceededCount = completed;
            FailedCount = failed;
        }

        // 평가마다 Prepare로 다시 채워 쓰는 빈 문맥. 소유자가 Prepare하기 전에는 쓰지 않는다.
        internal TaskGroupContext()
        {
            // NONE
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>Engine이 방금 중복 없이 만든 결과 목록을 복사·검증 없이 그대로 보도록 채운다.
        /// <br/>목록은 판정이 끝나면 풀로 돌아가므로 소유자는 그 전에 Clear로 놓는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal void Prepare(ObjectiveContext objective, List<TaskEntryResult> results)
        {
            int completed = 0;
            int failed = 0;
            for (int index = 0; index < results.Count; index++)
            {
                var entry = results[index];
                if (entry.Participating && entry.Result == TaskResult.Succeeded)
                {
                    completed++;
                }

                if (entry.Participating && entry.Result == TaskResult.Failed)
                {
                    failed++;
                }
            }

            Objective = objective;
            entries = results;
            SucceededCount = completed;
            FailedCount = failed;
        }

        // Prepare로 빌려 본 결과 목록과 Objective 문맥을 놓아 빈 문맥으로 되돌린다.
        internal void Clear()
        {
            Objective = null;
            entries = Array.Empty<TaskEntryResult>();
            SucceededCount = 0;
            FailedCount = 0;
        }

    #endregion

    #region 조회

        // ------------------------------------------------------------
        /// <summary>
        /// 직접 자식 Entry의 결과와 참여 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetEntry(string entryID, out TaskEntryResult result)
        {
            for (int index = 0; index < entries.Count; index++)
            {
                if (entries[index].EntryID == entryID)
                {
                    result = entries[index];
                    return true;
                }
            }

            result = default;
            return false;
        }

    #endregion

    }
}
