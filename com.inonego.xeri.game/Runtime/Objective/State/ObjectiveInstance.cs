/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveInstance.cs
수정일 : 2026-11-26

# 설명
Objective의 직접 영속 원본과 원자 변경을 소유한다.

# 특이사항, 제약사항
평가와 UI/Save I/O는 외부 책임이며, 변경은 동일 타입 임시 후보에 계획·검증(Plan)한 뒤 한 번에 반영(Commit)한다.
복원된 Inactive에는 시간·진행·통과 및 Sequential 경계 이력을 허용하지 않는다.
Progress·이력 복제는 Core IDeepCloneable·컬렉션 CloneFrom을 쓰고, 비교·유무 확인·키 이름 변경은 Core ValueRegistry의 IsSameAs·HasAny·Rename을 쓴다.
후보는 쓰기 전까지 원본의 Progress를 공유하고, 평가가 본 Registry는 이후 쓰기 때 복사해 바꾸지 않는다(쓰기 시 복사).
후보는 확정하면 폐기되며 이후 변경을 거부한다. 원본은 후보를 통해서만 바뀐다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.Pool;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Progress;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 하나의 Objective 실행을 직접 저장하고 변경을 확정하는 객체.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class ObjectiveInstance : IRevisioned
    {

    #region 영속 식별자·상태

        // ------------------------------------------------------------
        /// <summary>
        /// 정적 Objective Spec의 안정 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string SpecID => specID;
        [SerializeField] private string specID;

        // ------------------------------------------------------------
        /// <summary>
        /// 실행에 연결된 Spec Version.
        /// </summary>
        // ------------------------------------------------------------
        public int SpecVersion => specVersion;
        [SerializeField] private int specVersion;

        // ------------------------------------------------------------
        /// <summary>
        /// 이 실행만의 안정 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string InstanceID => instanceID;
        [SerializeField] private string instanceID;

        // ------------------------------------------------------------
        /// <summary>
        /// 소유 Scope의 안정 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string ScopeID => scopeID;
        [SerializeField] private string scopeID;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 영속 Objective 상태.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveStatus Status => status;
        [SerializeField] private ObjectiveStatus status;

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 시간 명령으로 누적한 경과 시간.
        /// </summary>
        // ------------------------------------------------------------
        public double ElapsedTime => elapsedTime;
        [SerializeField] private double elapsedTime;

        [SerializeField] private ProgressState progress;
        [SerializeField] private List<string> passedEntryIDs = new();
        [SerializeField] private List<SequentialBoundary> sequentialBoundaries = new();
        [SerializeField] private List<EntryOpening> entryOpenings = new();

    #endregion

    #region 후보·평가 보호 상태

        [NonSerialized] private bool applying;
        [NonSerialized] private readonly ObjectiveInstance origin;

        // 후보가 원본과 Progress 객체를 공유하거나 평가에 노출한 상태. 이 객체는 제자리에서 바꾸지 않는다.
        [NonSerialized] private bool progressShared;

        // 확정된 후보는 폐기되며 더 변경할 수 없다.
        [NonSerialized] private bool committed;

        // 후보가 계획 시점에 기억한 원본의 확정 횟수. 오래된 후보의 확정을 거부하는 데 쓴다.
        [NonSerialized] private readonly RevisionMark baseMark;

        // ------------------------------------------------------------
        /// <summary>
        /// 확정 횟수를 IRevisioned의 주된 축으로 내보낸다.
        /// </summary>
        // ------------------------------------------------------------
        Revision IRevisioned.Revision => revision;
        [NonSerialized] private Revision revision = Revision.None;

    #endregion

    #region 생성자·복원

        // ------------------------------------------------------------
        /// <summary>
        /// Spec과 식별자를 받아 초기 진행 상태를 직접 소유한다.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveInstance
        (
            string specID, int specVersion, string instanceID, string scopeID
        )
        {
            RequireID(specID, nameof(specID));
            RequireID(instanceID, nameof(instanceID));
            RequireID(scopeID, nameof(scopeID));
            if (specVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(specVersion));
            }

            this.specID = specID;
            this.specVersion = specVersion;
            this.instanceID = instanceID;
            this.scopeID = scopeID;
            progress = new ProgressState();
        }

        private ObjectiveInstance(ObjectiveInstance source)
        {
            specID = source.specID;
            specVersion = source.specVersion;
            instanceID = source.instanceID;
            scopeID = source.scopeID;
            status = source.status;
            elapsedTime = source.elapsedTime;
            progress = source.progress;
            progressShared = true;
            passedEntryIDs = new List<string>();
            passedEntryIDs.CloneFrom(source.passedEntryIDs);
            sequentialBoundaries = new List<SequentialBoundary>();
            sequentialBoundaries.CloneFrom(source.sequentialBoundaries);
            entryOpenings = new List<EntryOpening>();
            entryOpenings.CloneFrom(source.entryOpenings);
            origin = source;
            baseMark = RevisionMark.Capture(source);
            applying = true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 역직렬화된 실행의 영속 구조를 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Validate() => ValidateStructure();

        private static void RequireID(string id, string paramName)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("An ID must not be blank.", paramName);
            }
        }

    #endregion

    #region Version 변환

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>새 Version으로 옮긴 Instance를 만든다.
        /// <br/>상태·시간·값은 그대로 두고 이름만 매핑한다.
        /// <br/>원본은 바꾸지 않으며, 새 Instance의 구조는 만들면서 검증한다.
        /// <br/>매핑한 새 이름이 매핑하지 않은 기존 이름과 겹치면 항목 종류와 관계없이 구조 검증이
        /// <br/>InvalidOperationException으로 거부한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal ObjectiveInstance CreateMigrated(ObjectiveMigration migration)
        {
            var result = new ObjectiveInstance(specID, migration.ToVersion, instanceID, scopeID);
            result.status = status;
            result.elapsedTime = elapsedTime;
            result.progress = progress.Clone();
            result.progress.Values.Rename(migration.ProgressKeys);

            result.passedEntryIDs = new List<string>();
            foreach (var entryID in passedEntryIDs)
            {
                if (migration.TryMapEntryID(entryID, out string mapped))
                {
                    result.passedEntryIDs.Add(mapped);
                }
            }

            result.sequentialBoundaries = new List<SequentialBoundary>();
            foreach (var boundary in sequentialBoundaries)
            {
                if (migration.TryMapGroupID(boundary.GroupID, out string group)
                    && migration.TryMapEntryID(boundary.EntryID, out string entry))
                {
                    result.sequentialBoundaries.Add(new SequentialBoundary(group, entry));
                }
            }

            result.entryOpenings = new List<EntryOpening>();
            foreach (var opening in entryOpenings)
            {
                if (migration.TryMapEntryID(opening.EntryID, out string mapped))
                {
                    result.entryOpenings.Add(new EntryOpening(mapped, opening.OpenTime));
                }
            }

            result.ValidateStructure();
            return result;
        }

    #endregion

    #region 조회

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 원본과 분리된 실행 Progress Snapshot. Objective 입력은 Engine에서 확정한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public ProgressState Progress =>
            (progress ?? throw new InvalidOperationException("Objective Progress is missing.")).Clone();

        // ------------------------------------------------------------
        /// <summary>
        /// 일반 입력을 더 이상 받을 수 없는 종료 상태인지 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsTerminal =>
            status == ObjectiveStatus.Succeeded
            || status == ObjectiveStatus.Failed
            || status == ObjectiveStatus.Cancelled;

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 저장된 OncePassed EntryID의 읽기 전용 목록. 확정한 목록은 바뀌지 않으므로 복사하지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public ReadOnlyListView<string> PassedEntryIDs => passedEntryIDs == null ? default : new(passedEntryIDs);

        // --------------------------------------------------------------------------------
        /// <summary>
        /// 저장된 Sequential 경계 항목의 읽기 전용 목록. 확정한 목록은 바뀌지 않으므로 복사하지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public ReadOnlyListView<SequentialBoundary> SequentialBoundaries => sequentialBoundaries == null ? default : new(sequentialBoundaries);

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>저장된 Entry 참여 시작 시각 항목의 읽기 전용 목록.
        /// <br/>확정한 목록은 바뀌지 않으므로 복사하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public ReadOnlyListView<EntryOpening> EntryOpenings => entryOpenings == null ? default : new(entryOpenings);

        // ------------------------------------------------------------
        /// <summary>
        /// 이 Entry가 OncePassed로 기록되었는지 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool HasPassed(string entryID) =>
            !string.IsNullOrWhiteSpace(entryID) && passedEntryIDs.Contains(entryID);

        // ------------------------------------------------------------
        /// <summary>
        /// Sequential Group의 직전 도달 EntryID를 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetBoundary(string groupID, out string entryID)
        {
            if (!string.IsNullOrWhiteSpace(groupID))
            {
                foreach (var boundary in sequentialBoundaries)
                {
                    if (string.Equals(boundary.GroupID, groupID, StringComparison.Ordinal))
                    {
                        entryID = boundary.EntryID;
                        return true;
                    }
                }
            }

            entryID = null;
            return false;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 시간을 재는 Entry가 참여를 시작한 누적 경과 시간을 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetOpenTime(string entryID, out double openTime)
        {
            if (!string.IsNullOrWhiteSpace(entryID))
            {
                foreach (var opening in entryOpenings)
                {
                    if (string.Equals(opening.EntryID, entryID, StringComparison.Ordinal))
                    {
                        openTime = opening.OpenTime;
                        return true;
                    }
                }
            }

            openTime = 0;
            return false;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>현재 상태의 평가 Context를 만든다.
        /// <br/>Progress는 복사하지 않고 조회 전용으로 노출한다.
        /// <br/>노출한 Registry는 이후 쓰기에서 복사해 바꾸지 않으므로 Context가 보는 값은 변하지
        /// <br/>않는다.
        /// <br/>Entry 참여 시각은 Context에 복사해 넘기고, Directory 조회는 reads에 기록한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal ObjectiveContext CreateContext(ObjectiveDirectory directory, HashSet<(string Scope, string Name)> reads)
        {
            progressShared = true;
            Dictionary<string, double> opened = null;
            if (entryOpenings.Count > 0)
            {
                opened = new Dictionary<string, double>(entryOpenings.Count, StringComparer.Ordinal);
                foreach (var opening in entryOpenings)
                {
                    opened.Add(opening.EntryID, opening.OpenTime);
                }
            }

            return new ObjectiveContext(progress.Values, elapsedTime, status, scopeID, opened, directory, reads);
        }

    #endregion

    #region 원자 변경

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 타입 후보를 검증한 뒤 모든 영속 필드를 함께 반영한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool Apply(Action<ObjectiveInstance> edit) => Commit(Plan(edit));

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>원본을 바꾸지 않고 후보에 변경을 계획·검증한다.
        /// <br/>원본과 같으면 null을 돌려준다.
        /// </summary>
        // ------------------------------------------------------------
        internal ObjectiveInstance Plan(Action<ObjectiveInstance> edit)
        {
            if (edit == null)
            {
                throw new ArgumentNullException(nameof(edit));
            }

            if (applying)
            {
                throw new InvalidOperationException("Reentrant ObjectiveInstance mutation is not allowed.");
            }

            applying = true;
            try
            {
                var candidate = new ObjectiveInstance(this);
                edit(candidate);
                candidate.ValidateStructure();
                return IsSameAs(candidate) ? null : candidate;
            }
            finally
            {
                applying = false;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>이 원본에서 계획한 후보를 한 번에 반영한다.
        /// <br/>검증은 계획에서 끝났으므로 반영은 실패하지 않는 대입만 한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool Commit(ObjectiveInstance candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            if (!ReferenceEquals(candidate.origin, this))
            {
                throw new ArgumentException("Candidate was not planned from this Instance.", nameof(candidate));
            }

            // 계획 이후 원본이 바뀌었거나 이미 확정한 후보는 오래된 상태이므로 반영하지 않는다.
            if (candidate.committed || !candidate.baseMark.IsCurrent)
            {
                throw new InvalidOperationException("Candidate is stale or already committed.");
            }

            // 확정한 후보는 폐기되어 이후 변경을 거부하므로 상태 객체를 복사하지 않고 넘겨받는다.
            status = candidate.status;
            elapsedTime = candidate.elapsedTime;
            progress = candidate.progress;
            progressShared = true;
            passedEntryIDs = candidate.passedEntryIDs;
            sequentialBoundaries = candidate.sequentialBoundaries;
            entryOpenings = candidate.entryOpenings;
            revision = revision.Inc();
            candidate.committed = true;
            return true;
        }

        private bool IsSameAs(ObjectiveInstance other)
        {
            if (status != other.status || elapsedTime != other.elapsedTime
                || passedEntryIDs.Count != other.passedEntryIDs.Count
                || sequentialBoundaries.Count != other.sequentialBoundaries.Count
                || entryOpenings.Count != other.entryOpenings.Count)
            {
                return false;
            }

            for (int index = 0; index < passedEntryIDs.Count; index++)
            {
                if (!string.Equals(passedEntryIDs[index], other.passedEntryIDs[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            for (int index = 0; index < sequentialBoundaries.Count; index++)
            {
                var first = sequentialBoundaries[index];
                var second = other.sequentialBoundaries[index];
                if (first.GroupID != second.GroupID || first.EntryID != second.EntryID)
                {
                    return false;
                }
            }

            // 기록 순서는 의미가 없으므로 Entry별로 시각이 같은지만 비교한다.
            // 후보는 원본의 순서를 이어받으므로 같은 위치를 먼저 보면 대개 항목마다 한 번에 끝나고, 어긋날 때만 찾아본다.
            for (int index = 0; index < entryOpenings.Count; index++)
            {
                var opening = entryOpenings[index];
                var counterpart = other.entryOpenings[index];
                if (counterpart.EntryID == opening.EntryID)
                {
                    if (counterpart.OpenTime != opening.OpenTime)
                    {
                        return false;
                    }
                }
                else if (!other.TryGetOpenTime(opening.EntryID, out double openTime) || openTime != opening.OpenTime)
                {
                    return false;
                }
            }

            return progress.Values.IsSameAs(other.progress.Values);
        }

    #endregion

    #region 상태·시간 명령

        // ------------------------------------------------------------
        /// <summary>
        /// 허용된 Inactive/Active/Terminal 상태 전이만 적용한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool ChangeStatus(ObjectiveStatus next)
        {
            RequireCandidate();
            if (next == status)
            {
                return false;
            }

            bool allowed = status switch
            {
                ObjectiveStatus.Inactive => next == ObjectiveStatus.Active || next == ObjectiveStatus.Cancelled,
                ObjectiveStatus.Active => next == ObjectiveStatus.Succeeded
                    || next == ObjectiveStatus.Failed || next == ObjectiveStatus.Cancelled,
                _ => false
            };

            if (!allowed)
            {
                throw new InvalidOperationException("Objective status transition is not allowed.");
            }

            status = next;
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Active 실행의 경과 시간을 유한한 양수만큼 누적한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool AdvanceTime(double delta)
        {
            RequireCandidate();
            RequireActive();
            if (double.IsNaN(delta) || double.IsInfinity(delta) || delta <= 0
                || double.IsInfinity(elapsedTime + delta))
            {
                throw new ArgumentOutOfRangeException(nameof(delta));
            }

            elapsedTime += delta;
            return true;
        }

    #endregion

    #region Progress 명령

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Active 실행의 후보 Registry에 자유로운 변경을 적용한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool EditProgress(string progressID, Action<ValueRegistry> edit)
        {
            RequireCandidate();
            RequireActive();
            if (string.IsNullOrWhiteSpace(progressID))
            {
                throw new ArgumentException("Progress key must be specified.", nameof(progressID));
            }

            return ChangeProgress(edit, progressID);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>현재 Active 실행의 후보 Registry에 Key 제한 없이 변경을 적용한다.
        /// <br/>입력 Gate와 Task 소유 Key 검사는 Engine이 일반 입력에서 하는 일이며 여기서는 하지
        /// <br/>않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal bool ForceProgress(Action<ValueRegistry> edit)
        {
            RequireCandidate();
            RequireActive();
            return ChangeProgress(edit, null);
        }

        // 후보 Registry를 복사본에서 고친다. allowedKey가 있으면 그 Key 밖의 변경은 거부한다.
        private bool ChangeProgress(Action<ValueRegistry> edit, string allowedKey)
        {
            if (edit == null)
            {
                throw new ArgumentNullException(nameof(edit));
            }

            // 이전 Registry는 그대로 두고 복사본만 고친다. 이전 Registry를 본 Context와 비교 기준이 모두 변하지 않는다.
            var before = progress;
            progress = before.Clone();
            progressShared = false;
            edit(progress.Values);

            // Objective의 Gate를 다른 Key로 우회하는 변경은 후보에서 거부한다.
            if (allowedKey != null && !before.Values.IsSameAs(progress.Values, allowedKey))
            {
                throw new InvalidOperationException("Progress edit changed a key outside the allowed task.");
            }

            if (before.Values.IsSameAs(progress.Values))
            {
                // 변경이 없으면 이전 Registry를 그대로 쓴다.
                progress = before;
                progressShared = true;
                return false;
            }

            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Active 실행의 소유 Progress 하나를 초기화한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool ResetProgress(string progressID)
        {
            RequireCandidate();
            RequireActive();
            if (!progress.Values.Has(progressID))
            {
                return false;
            }

            // 공유하거나 평가에 노출한 Registry는 바꾸지 않고 복사본에서 지운다.
            if (progressShared)
            {
                progress = progress.Clone();
                progressShared = false;
            }

            return progress.Values.Remove(progressID);
        }

    #endregion

    #region 이력 명령

        // ------------------------------------------------------------
        /// <summary>
        /// 적법한 Entry 통과 이력을 한 번 기록한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool MarkPassed(string entryID)
        {
            RequireCandidate();
            RequireActive();
            RequireID(entryID, nameof(entryID));
            if (passedEntryIDs.Contains(entryID))
            {
                return false;
            }

            passedEntryIDs.Add(entryID);
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 후퇴에 따라 Entry 통과 이력을 취소한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool ClearPassed(string entryID)
        {
            RequireCandidate();
            RequireActive();
            RequireID(entryID, nameof(entryID));
            return passedEntryIDs.Remove(entryID);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Sequential Group이 실제 도달한 Entry 경계를 기록한다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool SetBoundary(string groupID, string entryID)
        {
            RequireCandidate();
            RequireActive();
            RequireID(groupID, nameof(groupID));
            RequireID(entryID, nameof(entryID));
            foreach (var boundary in sequentialBoundaries)
            {
                if (boundary.GroupID == groupID)
                {
                    if (boundary.EntryID == entryID)
                    {
                        return false;
                    }

                    boundary.SetEntry(entryID);
                    return true;
                }
            }

            sequentialBoundaries.Add(new SequentialBoundary(groupID, entryID));
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 후퇴로 더 이상 도달하지 않은 Group 경계를 지운다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool ClearBoundary(string groupID)
        {
            RequireCandidate();
            RequireActive();
            RequireID(groupID, nameof(groupID));
            var removed = false;

            for (var index = sequentialBoundaries.Count - 1; index >= 0; index--)
            {
                if (sequentialBoundaries[index].GroupID == groupID)
                {
                    sequentialBoundaries.RemoveAt(index);
                    removed = true;
                }
            }

            return removed;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 시간을 재는 Entry의 참여 시작 시각을 현재 경과 시간으로 한 번 기록한다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal bool MarkOpened(string entryID)
        {
            RequireCandidate();
            RequireActive();
            RequireID(entryID, nameof(entryID));
            if (TryGetOpenTime(entryID, out _))
            {
                return false;
            }

            entryOpenings.Add(new EntryOpening(entryID, elapsedTime));
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 참여를 멈춘 Entry의 시작 시각 기록을 지운다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool ClearOpened(string entryID)
        {
            RequireCandidate();
            RequireActive();
            RequireID(entryID, nameof(entryID));
            var removed = false;

            for (var index = entryOpenings.Count - 1; index >= 0; index--)
            {
                if (entryOpenings[index].EntryID == entryID)
                {
                    entryOpenings.RemoveAt(index);
                    removed = true;
                }
            }

            return removed;
        }

    #endregion

    #region 명령 조건 확인

        // 영속 원본은 계획한 후보를 통해서만 바뀌며 확정한 후보는 폐기된다.
        private void RequireCandidate()
        {
            if (origin == null || committed)
            {
                throw new InvalidOperationException("Objective state can only change on a planned, uncommitted candidate.");
            }
        }

        private void RequireActive()
        {
            if (status != ObjectiveStatus.Active)
            {
                throw new InvalidOperationException("Objective must be Active to change its progress.");
            }
        }

    #endregion

    #region 저장 구조 검증

        private void ValidateStructure()
        {
            if (string.IsNullOrWhiteSpace(specID)
                || specVersion <= 0
                || string.IsNullOrWhiteSpace(instanceID)
                || string.IsNullOrWhiteSpace(scopeID)
                || !status.IsDefined()
                || double.IsNaN(elapsedTime) || double.IsInfinity(elapsedTime)
                || elapsedTime < 0
                || progress == null || passedEntryIDs == null || sequentialBoundaries == null
                || entryOpenings == null)
            {
                throw new InvalidOperationException($"Objective instance '{instanceID}' contains invalid persistent fields.");
            }

            // 정상 명령으로 Inactive에 남을 수 없는 시간·진행·통과 이력을 거부한다.
            if (status == ObjectiveStatus.Inactive)
            {
                if (elapsedTime != 0 || passedEntryIDs.Count > 0 || sequentialBoundaries.Count > 0
                    || entryOpenings.Count > 0)
                {
                    throw new InvalidOperationException("Inactive Objective contains execution history.");
                }

                if (progress.Values.HasAny)
                {
                    throw new InvalidOperationException("Inactive Objective contains local Progress history.");
                }
            }

            var passed = HashSetPool<string>.Get();
            var groups = HashSetPool<string>.Get();
            var opened = HashSetPool<string>.Get();

            try
            {
                foreach (var entryID in passedEntryIDs)
                {
                    if (string.IsNullOrWhiteSpace(entryID) || !passed.Add(entryID))
                    {
                        throw new InvalidOperationException("OncePassed entry history is invalid.");
                    }
                }

                foreach (var boundary in sequentialBoundaries)
                {
                    if (boundary == null || string.IsNullOrWhiteSpace(boundary.GroupID)
                        || string.IsNullOrWhiteSpace(boundary.EntryID) || !groups.Add(boundary.GroupID))
                    {
                        throw new InvalidOperationException("Sequential boundary history is invalid.");
                    }
                }

                foreach (var opening in entryOpenings)
                {
                    // 참여 시작 시각은 현재 경과 시간을 넘을 수 없다.
                    if (opening == null || string.IsNullOrWhiteSpace(opening.EntryID) || !opened.Add(opening.EntryID)
                        || double.IsNaN(opening.OpenTime) || double.IsInfinity(opening.OpenTime)
                        || opening.OpenTime < 0 || opening.OpenTime > elapsedTime)
                    {
                        throw new InvalidOperationException("Entry opening history is invalid.");
                    }
                }
            }
            finally
            {
                HashSetPool<string>.Release(passed);
                HashSetPool<string>.Release(groups);
                HashSetPool<string>.Release(opened);
            }
        }

    #endregion

    }
}
