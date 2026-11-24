/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveEngine.cs
수정일 : 2026-11-25

# 설명
Objective Composite 평가, 순차 통과, 최종 상태 확정과 변경 알림을 조정한다.

# 특이사항, 제약사항
영속 원본은 ObjectiveInstance만 소유한다. 외부 Fact의 동일 시점 판정은 주입한 평가 Scope가 책임진다.
명령은 Scope 안에서 후보를 계획하고, Scope를 닫은 뒤 확정하고, 확정 뒤에만 알린다. 계획·닫기 실패는 변경도 알림도 없다.
확정된 변경의 알림은 구독자 예외와 관계없이 모두 전달하고, 실패는 모아 마지막에 던진다.
Entry 참여 시각은 순차 안정화와 같은 후보에서 기록하고, 다른 Objective 조회는 주입한 ObjectiveDirectory로 한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Progress;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 비영속 Composite 평가·사건 입력과 Instance 원자 커밋을 조정한다.
    /// </summary>
    // ============================================================
    public sealed class ObjectiveEngine
    {

    #region 필드·프로퍼티

        private readonly IReadOnlyDictionary<string, string> progressOwners;
        private readonly Queue<Action> notifications = new();
        private readonly IObjectiveFactScope factScope;
        private ObjectiveEvaluation lastEvaluation;
        private bool executing;
        private bool notifying;

        // 이 Engine을 실행하는 살아 있는 Flow. 한 Engine은 Flow 하나에만 속한다.
        internal ObjectiveFlow Flow { get; set; }

        // 이 Engine이 조회·전파에 쓰는 Directory.
        internal ObjectiveDirectory Directory => directory;
        private readonly ObjectiveDirectory directory;

        // 마지막으로 확정·초기화한 평가에서 Condition이 읽은 대상. Directory가 변경 전파에 쓴다.
        internal IReadOnlyCollection<(string Scope, string Name)> Reads => lastEvaluation?.Reads;

        // ------------------------------------------------------------
        /// <summary>
        /// 직접 영속 상태를 보관한 현재 ObjectiveInstance.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveInstance Instance => instance;
        private readonly ObjectiveInstance instance;

        // ------------------------------------------------------------
        /// <summary>
        /// 평가할 정적 Objective Spec.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveSpec Spec => spec;
        private readonly ObjectiveSpec spec;

        // ------------------------------------------------------------
        /// <summary>
        /// 실제 원자 커밋이 성공한 뒤에만 발생하는 변경 알림.
        /// </summary>
        // ------------------------------------------------------------
        public event Action Changed;

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>확정으로 Status가 바뀐 뒤 (이전, 이후) 상태와 함께 발생한다.
        /// <br/>Terminal 전이는 한 번만 발생하며 복원(Bind)은 발생시키지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        public event Action<ObjectiveStatus, ObjectiveStatus> StatusChanged;

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>확정으로 Entry의 참여·유효 결과·닫힌 사유가 바뀔 때 Entry마다 한 번 (이전, 이후)
        /// <br/>평가와 함께 발생한다.
        /// <br/>Spec 평가 순서대로 발행하며, 비교할 이전 평가가 없는 첫 확정과 복원(Bind)은 발행하지
        /// <br/>않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public event Action<TaskEntryTransition> EntryEvaluationChanged;

        // ------------------------------------------------------------
        /// <summary>
        /// 영속값 변경 여부와 관계없이 파생 실행 결과가 달라졌을 때 발생한다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<ObjectiveEvaluation> EvaluationChanged;

    #endregion

    #region 생성자

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>이미 검증된 Spec과 Instance를 연결해 Engine을 생성한다.
        /// <br/>directory는 Condition이 다른 Objective를 조회할 때 쓰며, 이 Engine을 등록하는
        /// <br/>일은 생성자가 하지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public ObjectiveEngine
        (
            ObjectiveSpec spec, ObjectiveInstance instance,
            IObjectiveFactScope factScope = null, ObjectiveDirectory directory = null
        )
        {
            this.spec = spec ?? throw new ArgumentNullException(nameof(spec));
            this.instance = instance ?? throw new ArgumentNullException(nameof(instance));
            this.factScope = factScope;
            this.directory = directory;

            ObjectiveSpecValidator.Validate(spec);
            if (!string.Equals(spec.SpecID, instance.SpecID, StringComparison.Ordinal)
                || spec.Version != instance.SpecVersion)
            {
                throw new ArgumentException
                (
                    $"Instance SpecID/Version does not match: Spec '{spec.SpecID}' r{spec.Version}, "
                    + $"Instance '{instance.SpecID}' r{instance.SpecVersion}.", nameof(instance)
                );
            }

            progressOwners = spec.Index.ProgressOwners;
            instance.Validate();
            ValidateHistory(spec.Index, instance);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 등록된 Spec의 정확한 Version을 해결해 Engine을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveEngine
        (
            IObjectiveSpecResolver resolver, ObjectiveInstance instance,
            IObjectiveFactScope factScope = null, ObjectiveDirectory directory = null
        )
            : this(Resolve(resolver, instance), instance, factScope, directory)
        {
            // NONE
        }

        private static ObjectiveSpec Resolve(IObjectiveSpecResolver resolver, ObjectiveInstance instance)
        {
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            if (!resolver.TryResolve(instance.SpecID, instance.SpecVersion, out var spec))
            {
                throw new ArgumentException
                (
                    $"Instance SpecID/Version was not registered: '{instance.SpecID}' r{instance.SpecVersion}.",
                    nameof(instance)
                );
            }

            return spec;
        }

        private static void ValidateHistory(SpecIndex index, ObjectiveInstance instance)
        {
            foreach (var entryID in instance.PassedEntryIDs)
            {
                if (!index.OncePassedEntryIDs.Contains(entryID))
                {
                    throw new InvalidOperationException($"Unknown or non-OncePassed Entry history: '{entryID}'.");
                }
            }

            foreach (var boundary in instance.SequentialBoundaries)
            {
                if (!index.HistoryGroups.TryGetValue(boundary.GroupID, out var group)
                    || FindEntryIndex(group, boundary.EntryID) < 0)
                {
                    throw new InvalidOperationException
                    (
                        $"Sequential boundary does not belong to its Group: group '{boundary.GroupID}', entry '{boundary.EntryID}'."
                    );
                }
            }

            foreach (var opening in instance.EntryOpenings)
            {
                if (!index.TimedEntryIDs.Contains(opening.EntryID))
                {
                    throw new InvalidOperationException($"Unknown or untimed Entry opening history: '{opening.EntryID}'.");
                }
            }
        }

        private static int FindEntryIndex(TaskGroupSpec group, string entryID)
        {
            for (int index = 0; index < group.Entries.Count; index++)
            {
                if (string.Equals(group.Entries[index].EntryID, entryID, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

    #endregion

    #region 순수 평가

        // ------------------------------------------------------------
        /// <summary>
        /// Composite 결과와 신규 입력 Gate를 읽기 전용으로 평가한다.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveEvaluation Evaluate()
        {
            using (Enter())
            {
                using var scope = factScope?.Begin();
                return EvaluateInternal(instance);
            }
        }

        // 복원 시 외부 Fact가 준비된 뒤 파생 평가의 기준만 설정한다.
        // 영속 후보 커밋과 변경 알림은 발생시키지 않는다.
        internal void InitializeEvaluation()
        {
            using (Enter())
            {
                using var scope = factScope?.Begin();
                lastEvaluation = EvaluateInternal(instance);
                directory?.SyncReads(this);
            }
        }

        private ObjectiveEvaluation EvaluateInternal(ObjectiveInstance source)
        {
            var snapshot = new ObjectiveEvaluation();
            var reads = directory != null ? new HashSet<(string Scope, string Name)>() : null;
            var context = source.CreateContext(directory, reads);
            snapshot.RootResult = EvaluateNode(spec.Root, context, source, true, snapshot);
            snapshot.Reads = reads;
            bool active = source.Status == ObjectiveStatus.Active;
            SetInputGates(spec.Root, snapshot, active);
            return snapshot;
        }

        private static TaskResult EvaluateNode
        (
            TaskNodeSpec node, ObjectiveContext context, ObjectiveInstance source,
            bool participating, ObjectiveEvaluation snapshot
        )
        {
            TaskResult result;
            if (node is TaskSpec task)
            {
                if (task.FailureCond?.Check(context) == true)
                {
                    result = TaskResult.Failed;
                }
                else if (task.SuccessCond.Check(context))
                {
                    result = TaskResult.Succeeded;
                }
                else
                {
                    result = TaskResult.Pending;
                }
            }
            else
            {
                var group = (TaskGroupSpec)node;
                var children = new List<TaskEntryResult>(group.Entries.Count);
                bool predecessorPassed = participating;
                foreach (var entry in group.Entries)
                {
                    // 닫힌 사유는 부모, 앞 Entry, Eligibility 순서로 첫 번째만 기록한다. 앞이 닫히면 뒤 조건은 호출하지 않는다.
                    var closedBy = TaskClosedBy.None;
                    if (!participating)
                    {
                        closedBy = TaskClosedBy.Parent;
                    }
                    else if (group.ExecutionMode == TaskExecutionMode.Sequential && !predecessorPassed)
                    {
                        closedBy = TaskClosedBy.Predecessor;
                    }
                    else if (!(entry.EligibilityCond?.Check(context) ?? true))
                    {
                        closedBy = TaskClosedBy.Eligibility;
                    }

                    bool opened = closedBy == TaskClosedBy.None;
                    TaskResult childRaw = EvaluateNode(entry.Node, context, source, opened, snapshot);
                    TaskResult effective = opened ? childRaw : TaskResult.Pending;
                    if (opened && group.ExecutionMode == TaskExecutionMode.Sequential
                        && entry.RevalidationMode == TaskRevalidationMode.OncePassed
                        && childRaw != TaskResult.Failed && source.HasPassed(entry.EntryID))
                    {
                        effective = TaskResult.Succeeded;
                    }

                    snapshot.SetEntry(entry.EntryID, entry.Node.NodeID, opened, effective, closedBy);
                    children.Add(new TaskEntryResult(entry.EntryID, opened, effective));
                    if (group.ExecutionMode == TaskExecutionMode.Sequential)
                    {
                        predecessorPassed = opened && effective == TaskResult.Succeeded;
                    }
                }

                var groupContext = new TaskGroupContext(context, children);
                if (group.FailureCond?.Check(groupContext) == true)
                {
                    result = TaskResult.Failed;
                }
                else if (group.SuccessCond.Check(groupContext))
                {
                    result = TaskResult.Succeeded;
                }
                else
                {
                    result = TaskResult.Pending;
                }
            }

            snapshot.SetNode(node.NodeID, participating, result);
            return result;
        }

        private static void SetInputGates(TaskNodeSpec node, ObjectiveEvaluation snapshot, bool ancestorAllows)
        {
            snapshot.TryGetNode(node.NodeID, out var current);
            bool allowed = current.Participating && ancestorAllows;
            if (node is TaskGroupSpec group)
            {
                if (current.RawResult == TaskResult.Succeeded
                    && group.ContinuationMode == TaskContinuationMode.StopOnSucceeded)
                {
                    allowed = false;
                }

                snapshot.SetNodeInput(node.NodeID, allowed);
                foreach (var entry in group.Entries)
                {
                    snapshot.TryGetEntry(entry.EntryID, out var child);
                    SetInputGates(entry.Node, snapshot, allowed && child.Participating);
                    snapshot.TryGetNode(entry.Node.NodeID, out var childNode);
                    snapshot.SetEntryInput(entry.EntryID, childNode.CanAcceptInput);
                }
            }
            else
            {
                snapshot.SetNodeInput(node.NodeID, allowed);
            }
        }

    #endregion

    #region Sequential 통과·후퇴

        // 후보가 더는 바뀌지 않을 때까지 맞추고, 그 상태의 평가를 돌려준다.
        // 호출자는 같은 후보를 다시 평가하지 않고 이 결과를 쓴다.
        private ObjectiveEvaluation Stabilize(ObjectiveInstance candidate)
        {
            // 복원 직후의 조회·바인딩은 이 메서드를 실행하지 않는다.
            // Active 명령/명시 Refresh에서만 영속 개방 이력을 확정한다.
            int maximum = spec.Index.EntryCount * 4 + 8;
            for (int round = 0; round < maximum; round++)
            {
                var evaluation = EvaluateInternal(candidate);
                if (candidate.Status != ObjectiveStatus.Active)
                {
                    return evaluation;
                }

                // 순차 개방이 바뀌면 참여 여부가 달라지므로 시각 기록은 개방이 안정된 평가에서만 맞춘다.
                if (ReconcileSequential(spec.Root, evaluation, candidate)
                    || ReconcileOpenings(spec.Root, evaluation, candidate))
                {
                    continue;
                }

                return evaluation;
            }

            throw new InvalidOperationException("Objective state did not converge.");
        }

        private static bool ReconcileSequential
        (
            TaskNodeSpec node, ObjectiveEvaluation evaluation, ObjectiveInstance candidate
        )
        {
            if (node is not TaskGroupSpec group)
            {
                return false;
            }

            evaluation.TryGetNode(node.NodeID, out var groupState);
            if (!groupState.Participating)
            {
                return false;
            }

            if (group.ExecutionMode == TaskExecutionMode.Sequential)
            {
                int previous = -1;
                if (candidate.TryGetBoundary(group.NodeID, out string boundaryID))
                {
                    previous = FindEntryIndex(group, boundaryID);
                }

                int reached = -1;
                for (int index = 0; index < group.Entries.Count; index++)
                {
                    var entry = group.Entries[index];
                    evaluation.TryGetEntry(entry.EntryID, out var current);
                    if (!current.Participating)
                    {
                        // Eligibility=false는 입력만 정지한다. 통과·경계·Progress는 보존한다.
                        break;
                    }

                    reached = index;
                    if (index < previous && current.EffectiveResult != TaskResult.Succeeded)
                    {
                        // 실제 참여 중인 선행 Entry가 유효 통과를 잃었을 때만 후퇴한다.
                        for (int later = previous; later > index; later--)
                        {
                            var rolledBack = group.Entries[later];
                            if (rolledBack.RollbackMode == TaskRollbackMode.ResetOnRollback)
                            {
                                ResetEntryTree(rolledBack, candidate);
                            }
                        }

                        candidate.SetBoundary(group.NodeID, entry.EntryID);
                        return true;
                    }

                    evaluation.TryGetNode(entry.Node.NodeID, out var child);
                    if (entry.RevalidationMode == TaskRevalidationMode.OncePassed
                        && child.RawResult == TaskResult.Succeeded
                        && !candidate.HasPassed(entry.EntryID))
                    {
                        candidate.MarkPassed(entry.EntryID);
                        return true;
                    }
                }

                if (reached > previous)
                {
                    candidate.SetBoundary(group.NodeID, group.Entries[reached].EntryID);
                    return true;
                }
            }

            foreach (var entry in group.Entries)
            {
                evaluation.TryGetEntry(entry.EntryID, out var state);
                if (state.Participating && ReconcileSequential(entry.Node, evaluation, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ResetEntryTree(TaskChildEntry entry, ObjectiveInstance candidate)
        {
            // 후퇴하는 Entry 자체의 통과도 subtree Reset 대상에 포함한다.
            candidate.ClearPassed(entry.EntryID);
            ResetNodeTree(entry.Node, candidate);
        }

        private static void ResetNodeTree(TaskNodeSpec node, ObjectiveInstance candidate)
        {
            if (node is TaskSpec task)
            {
                foreach (var progressID in task.LocalProgressIDs)
                {
                    candidate.ResetProgress(progressID);
                }

                return;
            }

            var group = (TaskGroupSpec)node;
            if (group.ExecutionMode == TaskExecutionMode.Sequential)
            {
                candidate.ClearBoundary(group.NodeID);
            }

            foreach (var entry in group.Entries)
            {
                ResetEntryTree(entry, candidate);
            }
        }

    #endregion

    #region 참여 시각 기록

        // 시간을 재는 Entry의 참여 시작 시각을 참여 여부에 맞춘다. 참여를 멈추면 지워서 다시 열릴 때 새로 잰다.
        private static bool ReconcileOpenings
        (
            TaskNodeSpec node, ObjectiveEvaluation evaluation, ObjectiveInstance candidate
        )
        {
            if (node is not TaskGroupSpec group)
            {
                return false;
            }

            bool changed = false;
            foreach (var entry in group.Entries)
            {
                if (entry.TracksOpenTime)
                {
                    evaluation.TryGetEntry(entry.EntryID, out var state);
                    changed |= state.Participating
                        ? candidate.MarkOpened(entry.EntryID)
                        : candidate.ClearOpened(entry.EntryID);
                }

                changed |= ReconcileOpenings(entry.Node, evaluation, candidate);
            }

            return changed;
        }

    #endregion

    #region 입력 명령

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>Active로 변경하며 조건 예외 시 원본을 보존한다.
        /// <br/>Flow가 소유한 Engine은 거부한다.
        /// <br/>선행·Choice 게이트를 우회하지 않도록 <see cref="ObjectiveFlow.Activate"/>로
        /// <br/>시작한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public bool Activate()
        {
            if (Flow != null)
            {
                throw new InvalidOperationException("An Engine owned by a Flow must be activated through ObjectiveFlow.Activate.");
            }

            return ActivateCore();
        }

        // Flow 소유 여부를 확인하지 않는 시작. ObjectiveFlow가 게이트를 확인한 뒤 부른다.
        internal bool ActivateCore() => Execute(candidate => candidate.ChangeStatus(ObjectiveStatus.Active));

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>지정한 Task의 입력 Gate를 통과한 뒤 후보 Registry를 변경한다.
        /// <br/>자료형별 등록·수정은 ValueRegistry의 기존 API를 사용한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool EditProgress(string progressID, Action<ValueRegistry> edit)
        {
            if (edit == null)
            {
                throw new ArgumentNullException(nameof(edit));
            }

            return ExecuteProgress(progressID, candidate => candidate.EditProgress(progressID, edit));
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>관리자 권한으로 입력 Gate와 Task 소유 Key 제한 없이 Progress를 바꾼다.
        /// <br/>일반 명령과 같은 계획·확정·알림을 거치므로 순차 안정화와 최종 Status 확정도 따른다.
        /// <br/>Active가 아닌 실행은 구조 불변식을 지키기 위해 거부한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public bool ForceProgress(Action<ValueRegistry> edit)
        {
            if (edit == null)
            {
                throw new ArgumentNullException(nameof(edit));
            }

            return Execute(candidate => candidate.ForceProgress(edit));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 Fact 변경 뒤 후보 확정과 파생 결과 알림을 갱신한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Refresh()
        {
            if (instance.Status != ObjectiveStatus.Active)
            {
                return false;
            }

            return Execute(NoCommand);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Root 조건을 다시 검증하고 성공·실패를 명시적으로 확정한다.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveSubmitResult Submit()
        {
            RequireNotExecuting();
            if (instance.Status != ObjectiveStatus.Active)
            {
                return ObjectiveSubmitResult.None;
            }

            Execute(NoCommand, submit: true);
            return instance.Status switch
            {
                ObjectiveStatus.Succeeded => ObjectiveSubmitResult.Succeeded,
                ObjectiveStatus.Failed => ObjectiveSubmitResult.Failed,
                _ => ObjectiveSubmitResult.None
            };
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 Time Owner가 전달한 유한 양수 시간만 누적한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool AdvanceTime(double delta)
        {
            if (double.IsNaN(delta) || double.IsInfinity(delta) || delta <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delta));
            }

            if (instance.Status != ObjectiveStatus.Active)
            {
                return false;
            }

            return Execute(candidate => candidate.AdvanceTime(delta));
        }

    #endregion

    #region 강제 종료

        // ------------------------------------------------------------
        /// <summary>
        /// 관리자 권한의 조건 우회 종료. Terminal은 다시 변경하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Force(ObjectiveStatus terminalStatus)
        {
            RequireNotExecuting();
            if (terminalStatus != ObjectiveStatus.Succeeded
                && terminalStatus != ObjectiveStatus.Failed
                && terminalStatus != ObjectiveStatus.Cancelled)
            {
                throw new ArgumentOutOfRangeException(nameof(terminalStatus));
            }

            if (instance.Status == terminalStatus)
            {
                return false;
            }

            bool changed;
            var before = instance.Status;
            using (Enter())
            {
                // 강제 명령은 외부 Condition 대신 Instance 전이 불변식만 확인한다.
                changed = instance.Apply(candidate => candidate.ChangeStatus(terminalStatus));
                if (changed)
                {
                    QueueStatusTransition(before);
                }
            }

            FlushNotifications();
            return changed;
        }

    #endregion

    #region 새 회차

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>Spec 반복 정책에 따라 새 ID의 독립 실행을 만든다.
        /// <br/>Flow가 소유한 Engine은 거부한다.
        /// <br/>Flow 안에서는 <see cref="ObjectiveFlow.Repeat"/>로 교체와 함께 반복한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public ObjectiveEngine CreateRepeat(string newInstanceID)
        {
            RequireNotExecuting();
            if (Flow != null)
            {
                throw new InvalidOperationException("An Engine owned by a Flow must be repeated through ObjectiveFlow.Repeat.");
            }

            return CreateRepeatCore(newInstanceID);
        }

        // Flow 소유 여부를 확인하지 않는 회차 생성. ObjectiveFlow.Repeat가 교체와 함께 부른다.
        internal ObjectiveEngine CreateRepeatCore(string newInstanceID)
        {
            RequireNotExecuting();
            if (!instance.IsTerminal)
            {
                throw new InvalidOperationException("Objective cannot be repeated in its current state.");
            }

            var repeated = NewRun(newInstanceID);
            repeated.Publish();
            return repeated;
        }

        // 이 Spec의 새 회차를 만든다. 이전 회차의 종료 여부는 호출자가 확인하며 이 Engine은 바뀌지 않는다.
        internal ObjectiveEngine NewRun(string newInstanceID)
        {
            RequireNotExecuting();
            if (spec.RepeatMode != ObjectiveRepeatMode.Repeatable)
            {
                throw new InvalidOperationException("Objective cannot be repeated in its current state.");
            }

            if (string.Equals(newInstanceID, instance.InstanceID, StringComparison.Ordinal))
            {
                throw new ArgumentException("Repeat requires a new InstanceID.", nameof(newInstanceID));
            }

            var repeated = new ObjectiveInstance
            (
                spec.SpecID, spec.Version,
                newInstanceID, instance.ScopeID
            );
            return new ObjectiveEngine(spec, repeated, factScope, directory);
        }

        // 주입한 Directory에 현재 회차로 등록한다. Directory가 없으면 아무 일도 하지 않는다.
        internal void Publish() => directory?.Register(this);

    #endregion

    #region Flow 연동

        // Flow의 시작 조건을 이 Engine의 현재 상태로 평가한다. 평가 규칙(Fact Scope·재진입 거부)은 일반 평가와 같다.
        // reads를 주면 조건이 읽은 다른 Objective·Fact 소스를 기록한다.
        internal bool CheckActivation(ICond<ObjectiveContext> condition, HashSet<(string Scope, string Name)> reads)
        {
            using (Enter())
            {
                using var scope = factScope?.Begin();
                return condition.Check(instance.CreateContext(directory, reads));
            }
        }

        // Choice 취소 후보를 계획만 한다. Flow가 모든 대안을 계획한 뒤에야 확정한다.
        internal ObjectiveInstance PlanCancelForChoice()
        {
            RequireNotExecuting();
            return instance.Plan(candidate => candidate.ChangeStatus(ObjectiveStatus.Cancelled));
        }

        // 계획한 Choice 취소를 확정하고 알림을 대기열에 둔다. 전달은 Flow가 모든 확정 뒤에 한다.
        internal void CommitCancelForChoice(ObjectiveInstance candidate)
        {
            var before = instance.Status;
            if (instance.Commit(candidate))
            {
                QueueStatusTransition(before);
            }
        }

        internal void FlushChoiceNotifications() => FlushNotifications();

    #endregion

    #region 원자 실행

        private bool ExecuteProgress(string progressID, Func<ObjectiveInstance, bool> command)
        {
            RequireNotExecuting();
            if (progressID == null || !progressOwners.TryGetValue(progressID, out var taskID))
            {
                throw new ArgumentException($"Unknown or nonlocal Progress ID: '{progressID}'.", nameof(progressID));
            }

            bool changed;
            using (Enter())
            {
                ObjectiveInstance candidate = null;
                ObjectiveEvaluation next = null;
                bool accepted;

                // 입력 Gate 판정과 후보 계획은 같은 Fact 시점에서 한다.
                // Scope 닫기까지 끝나야 확정하므로 닫기 실패는 변경 없는 실패다.
                using (factScope?.Begin())
                {
                    var before = EvaluateInternal(instance);
                    before.TryGetNode(taskID, out var task);
                    accepted = task.CanAcceptInput;
                    if (accepted)
                    {
                        candidate = PlanCandidate(command, false, out next);
                    }
                }

                if (!accepted)
                {
                    return false;
                }

                changed = CommitCandidate(candidate, next);
            }

            FlushNotifications();
            return changed;
        }

        // 상태를 바꾸지 않고 평가와 확정만 하는 명령.
        private static bool NoCommand(ObjectiveInstance candidate) => false;

        private bool Execute(Func<ObjectiveInstance, bool> command, bool submit = false)
        {
            bool changed;
            using (Enter())
            {
                ObjectiveInstance candidate;
                ObjectiveEvaluation next;

                // Scope 닫기까지 끝나야 확정하므로 닫기 실패는 변경 없는 실패다.
                using (factScope?.Begin())
                {
                    candidate = PlanCandidate(command, submit, out next);
                }

                changed = CommitCandidate(candidate, next);
            }

            FlushNotifications();
            return changed;
        }

        // 명령·순차 안정화·최종 Status까지 후보에서 계획하고, 확정 뒤 기준이 될 평가를 함께 돌려준다.
        private ObjectiveInstance PlanCandidate
        (
            Func<ObjectiveInstance, bool> command, bool submit, out ObjectiveEvaluation next
        )
        {
            ObjectiveEvaluation planned = null;

            // 명령을 적용한 뒤 후보를 안정화하고 최종 Status까지 계획한다.
            void PlanStep(ObjectiveInstance candidate)
            {
                command(candidate);
                if (candidate.Status != ObjectiveStatus.Active)
                {
                    planned = EvaluateInternal(candidate);
                    return;
                }

                // 안정된 평가가 곧 계획 평가다. 최종 Status를 바꾼 때만 다시 평가한다.
                planned = Stabilize(candidate);
                var root = planned.RootResult;
                if (root == TaskResult.Failed
                    && (submit || spec.FailureCommitMode == ObjectiveCommitMode.Automatic))
                {
                    candidate.ChangeStatus(ObjectiveStatus.Failed);
                    planned = EvaluateInternal(candidate);
                }
                else if (root == TaskResult.Succeeded
                    && (submit || spec.SuccessCommitMode == ObjectiveCommitMode.Automatic))
                {
                    candidate.ChangeStatus(ObjectiveStatus.Succeeded);
                    planned = EvaluateInternal(candidate);
                }
            }

            var result = instance.Plan(PlanStep);
            next = planned;
            return result;
        }

        // 계획한 후보를 확정하고 그 뒤에만 알림을 대기열에 둔다.
        private bool CommitCandidate(ObjectiveInstance candidate, ObjectiveEvaluation next)
        {
            var before = instance.Status;
            bool changed = instance.Commit(candidate);
            QueueNotifications(changed, next, before);
            return changed;
        }

        private void RequireNotExecuting()
        {
            if (executing)
            {
                throw new InvalidOperationException("Reentrant ObjectiveEngine operation is not allowed.");
            }
        }

        // 재진입을 막는 실행 구간을 연다. 반환 값을 Dispose하면 구간이 끝난다.
        private ExecutionScope Enter()
        {
            RequireNotExecuting();
            executing = true;
            return new ExecutionScope(this);
        }

        private readonly struct ExecutionScope : IDisposable
        {
            private readonly ObjectiveEngine owner;

            internal ExecutionScope(ObjectiveEngine owner)
            {
                this.owner = owner;
            }

            // ------------------------------------------------------------
            /// <summary>
            /// 실행 구간을 끝내 다시 명령을 받을 수 있게 한다.
            /// </summary>
            // ------------------------------------------------------------
            public void Dispose()
            {
                owner.executing = false;
            }
        }

    #endregion

    #region 알림

        // Status만 바꾸는 확정(강제 종료·Choice 취소)의 알림을 대기열에 둔다. 파생 평가는 다음 확정에서 다시 만든다.
        private void QueueStatusTransition(ObjectiveStatus before)
        {
            lastEvaluation = null;
            QueueNotifications(true, null, before);
        }

        // 확정 뒤의 알림을 Changed → StatusChanged → EntryEvaluationChanged → EvaluationChanged 순서로 대기열에 둔다.
        private void QueueNotifications(bool changed, ObjectiveEvaluation next, ObjectiveStatus before)
        {
            var previous = lastEvaluation;
            bool viewChanged = next != null && !next.IsEquivalentTo(previous);
            lastEvaluation = next;
            directory?.SyncReads(this);
            if (changed)
            {
                Action handler = Changed;
                if (handler != null)
                {
                    notifications.Enqueue(handler);
                }
            }

            var after = instance.Status;
            if (after != before)
            {
                Action<ObjectiveStatus, ObjectiveStatus> handler = StatusChanged;
                if (handler != null)
                {
                    notifications.Enqueue(() => handler(before, after));
                }
            }

            if (viewChanged)
            {
                Action<TaskEntryTransition> entryHandler = EntryEvaluationChanged;
                if (entryHandler != null)
                {
                    foreach (var transition in next.DiffEntries(previous))
                    {
                        var current = transition;
                        notifications.Enqueue(() => entryHandler(current));
                    }
                }

                Action<ObjectiveEvaluation> handler = EvaluationChanged;
                if (handler != null)
                {
                    notifications.Enqueue(() => handler(next));
                }
            }
        }

        private void FlushNotifications()
        {
            if (notifying)
            {
                return;
            }

            notifying = true;
            var failures = new ExceptionBuffer();
            try
            {
                // 알림에서 새로운 명령이 실행돼도 다음 알림은 순서대로 전달된다.
                // 확정된 변경의 알림은 구독자 하나가 실패해도 모두 전달하고, 실패는 모아 마지막에 던진다.
                while (notifications.Count > 0)
                {
                    try
                    {
                        notifications.Dequeue()();
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
            }
            finally
            {
                notifying = false;
            }

            failures.ThrowWhenAny("Objective notification handlers failed.");
        }

    #endregion

    }
}
