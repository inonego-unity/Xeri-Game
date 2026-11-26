/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveEngine.cs
수정일 : 2026-11-26

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
    /// 비영속 Composite 평가·사건 입력과 Instance 원자 커밋을 조정한다.
    /// </summary>
    // ============================================================
    public sealed class ObjectiveEngine
    {

    #region 연결 대상

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

        // 이 Engine이 조회·전파에 쓰는 Directory.
        internal ObjectiveDirectory Directory => directory;
        private readonly ObjectiveDirectory directory;

        // 외부 Fact의 동일 시점 판정을 맡는 Scope. 없으면 Fact를 쓰지 않는다.
        private readonly IObjectiveFactScope factScope;

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
            changes = directory?.ChangeQueue ?? new ObjectiveChangeQueue();

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
            evaluator = new ObjectiveEvaluator(spec, directory);
            stabilizer = new ObjectiveStabilizer(spec, evaluator);
            planStep = PlanStep;
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
                    || ObjectiveStabilizer.FindEntryIndex(group, boundary.EntryID) < 0)
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

    #endregion

    #region 순수 평가

        // 평가 계산과 후보 안정화. 둘 다 Engine이 소유하며 상태 변경과 확정은 Engine이 맡는다.
        private readonly ObjectiveEvaluator evaluator;
        private readonly ObjectiveStabilizer stabilizer;

        // 마지막으로 확정·초기화한 평가. 변경 전파와 알림의 비교 기준이다.
        private ObjectiveEvaluation lastEvaluation;

        // 마지막으로 확정·초기화한 평가에서 Condition이 읽은 대상. Directory가 변경 전파에 쓴다.
        internal ReadOnlySetView<(string Scope, string Name)> Reads => lastEvaluation?.Reads == null ? default : new(lastEvaluation.Reads);

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
                return evaluator.Evaluate(instance);
            }
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>NonAlloc 평가에 쓸 결과 버퍼를 만든다. 이 Engine의 Spec에만 쓸 수 있다.
        /// <br/>한 번 만든 버퍼는 EvaluateNonAlloc에 계속 다시 넘겨 쓴다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public ObjectiveEvaluation CreateEvaluationBuffer() => new ObjectiveEvaluation(spec.Index);

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>Evaluate와 같은 평가를 호출자의 버퍼에 채운다.
        /// <br/>버퍼를 먼저 비우므로 이전 결과는 사라지고 할당은 없다.
        /// <br/>버퍼는 이 Engine의 CreateEvaluationBuffer로 만든 것이어야 한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void EvaluateNonAlloc(ObjectiveEvaluation buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (!buffer.IsFor(spec.Index))
            {
                throw new ArgumentException("Buffer was created for a different Spec.", nameof(buffer));
            }

            using (Enter())
            {
                using var scope = factScope?.Begin();
                buffer.Clear();
                evaluator.Fill(instance, buffer, false);
            }
        }

        // 복원 시 외부 Fact가 준비된 뒤 파생 평가의 기준만 설정한다.
        // 영속 후보 커밋과 변경 알림은 발생시키지 않는다.
        internal void InitializeEvaluation()
        {
            using (Enter())
            {
                using var scope = factScope?.Begin();
                lastEvaluation = evaluator.Evaluate(instance);
                directory?.SyncReads(this);
            }
        }

    #endregion

    #region 입력 명령

        // Progress를 소유한 Task를 찾는 표. Spec이 정해 두며 입력 Gate 판정에 쓴다.
        private readonly IReadOnlyDictionary<string, string> progressOwners;

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

            // 재진입이면 바깥 명령의 인수를 덮어쓰기 전에 거부한다.
            RequireNotExecuting();
            cached.ProgressID = progressID;
            cached.Edit = edit;
            try
            {
                return ExecuteProgress(progressID, cached.EditProgress);
            }
            finally
            {
                cached.ProgressID = null;
                cached.Edit = null;
            }
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

            cached.Delta = delta;
            return Execute(cached.Advance);
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

            return Execute(noCommand);
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

            Execute(noCommand, submit: true);
            return instance.Status switch
            {
                ObjectiveStatus.Succeeded => ObjectiveSubmitResult.Succeeded,
                ObjectiveStatus.Failed => ObjectiveSubmitResult.Failed,
                _ => ObjectiveSubmitResult.None
            };
        }

    #endregion

    #region 관리자 강제

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
                cached.Status = terminalStatus;
                changed = instance.Apply(cached.ForceStatus);
                if (changed)
                {
                    QueueStatusTransition(before);
                }
            }

            FlushChanges();
            return changed;
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

            RequireNotExecuting();
            cached.Edit = edit;
            try
            {
                return Execute(cached.ForceProgress);
            }
            finally
            {
                cached.Edit = null;
            }
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

        // 이 Engine을 실행하는 살아 있는 Flow. 한 Engine은 Flow 하나에만 속한다.
        internal ObjectiveFlow Flow { get; set; }

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

        // 계획한 Choice 취소를 확정하고 변경을 대기열에 둔다. 발행은 Flow가 모든 확정 뒤에 한다.
        internal void CommitCancelForChoice(ObjectiveInstance candidate)
        {
            var before = instance.Status;
            if (instance.Commit(candidate))
            {
                QueueStatusTransition(before);
            }
        }

        internal void FlushChoiceChanges() => FlushChanges();

    #endregion

    #region 원자 실행

        // 재진입을 막는 실행 구간 표시.
        private bool executing;

        // 명령 실행 중에만 쓰는 계획 상태. 재진입이 막혀 있어 Engine마다 하나면 충분하다.
        private readonly Action<ObjectiveInstance> planStep;
        private Func<ObjectiveInstance, bool> planCommand;
        private bool planSubmit;
        private ObjectiveEvaluation planned;

        // 명령마다 클로저를 만들지 않도록 한 번 만들어 두는 명령과 그 인자.
        private readonly CachedCommands cached = new();

        // 입력 Gate 판정에만 쓰고 버리는 재사용 평가 결과.
        private ObjectiveEvaluation gateBuffer;

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
                    gateBuffer ??= new ObjectiveEvaluation(spec.Index);
                    gateBuffer.Clear();
                    evaluator.Fill(instance, gateBuffer, false);
                    gateBuffer.TryGetNode(taskID, out var task);
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

            FlushChanges();
            return changed;
        }

        // 상태를 바꾸지 않고 평가와 확정만 하는 명령. 호출마다 대리자를 만들지 않도록 한 번만 만든다.
        private static bool NoCommand(ObjectiveInstance candidate) => false;

        private static readonly Func<ObjectiveInstance, bool> noCommand = NoCommand;

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

            FlushChanges();
            return changed;
        }

        // 명령·순차 안정화·최종 Status까지 후보에서 계획하고, 확정 뒤 기준이 될 평가를 함께 돌려준다.
        private ObjectiveInstance PlanCandidate
        (
            Func<ObjectiveInstance, bool> command, bool submit, out ObjectiveEvaluation next
        )
        {
            planCommand = command;
            planSubmit = submit;
            planned = null;
            try
            {
                var result = instance.Plan(planStep);
                next = planned;
                return result;
            }
            finally
            {
                planCommand = null;
                planned = null;
            }
        }

        // 명령을 적용한 뒤 후보를 안정화하고 최종 Status까지 계획한다.
        private void PlanStep(ObjectiveInstance candidate)
        {
            planCommand(candidate);
            if (candidate.Status != ObjectiveStatus.Active)
            {
                planned = evaluator.Evaluate(candidate);
                return;
            }

            // 안정된 평가가 곧 계획 평가다. 최종 Status를 바꾼 때만 다시 평가한다.
            planned = stabilizer.Stabilize(candidate);
            var root = planned.RootResult;
            if (root == TaskResult.Failed
                && (planSubmit || spec.FailureCommitMode == ObjectiveCommitMode.Automatic))
            {
                candidate.ChangeStatus(ObjectiveStatus.Failed);
                planned = evaluator.Evaluate(candidate);
            }
            else if (root == TaskResult.Succeeded
                && (planSubmit || spec.SuccessCommitMode == ObjectiveCommitMode.Automatic))
            {
                candidate.ChangeStatus(ObjectiveStatus.Succeeded);
                planned = evaluator.Evaluate(candidate);
            }
        }

        // 계획한 후보를 확정하고 그 뒤에만 변경을 대기열에 둔다.
        private bool CommitCandidate(ObjectiveInstance candidate, ObjectiveEvaluation next)
        {
            var before = instance.Status;
            bool changed = instance.Commit(candidate);
            QueueChanges(changed, next, before);
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

        // ================================================================================
        /// <summary>
        /// <br/>입력 명령이 Plan에 넘기는 delegate를 한 번만 만들어 두고, 명령마다 인자만 바꿔 쓴다.
        /// <br/>재진입이 막혀 있어 Engine마다 하나면 충분하며, 인자는 명령이 끝나면 호출자가 비운다.
        /// </summary>
        // ================================================================================
        private sealed class CachedCommands
        {
            internal double Delta;
            internal string ProgressID;
            internal Action<ValueRegistry> Edit;
            internal ObjectiveStatus Status;

            internal readonly Func<ObjectiveInstance, bool> Advance;
            internal readonly Func<ObjectiveInstance, bool> EditProgress;
            internal readonly Func<ObjectiveInstance, bool> ForceProgress;
            internal readonly Action<ObjectiveInstance> ForceStatus;

            internal CachedCommands()
            {
                Advance = candidate => candidate.AdvanceTime(Delta);
                EditProgress = candidate => candidate.EditProgress(ProgressID, Edit);
                ForceProgress = candidate => candidate.ForceProgress(Edit);
                ForceStatus = candidate => candidate.ChangeStatus(Status);
            }
        }

    #endregion

    #region 알림

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

        // 확정 뒤 발행할 변경 대기열. 발행 중 재진입한 명령의 변경도 순서대로 발행한다.
        private readonly ObjectiveChangeQueue changes;

        // Status만 바꾸는 확정(강제 종료·Choice 취소)의 변경을 대기열에 둔다. 파생 평가는 다음 확정에서 다시 만든다.
        private void QueueStatusTransition(ObjectiveStatus before)
        {
            lastEvaluation = null;
            QueueChanges(true, null, before);
        }

        // 확정 뒤의 변경을 Changed → StatusChanged → EntryEvaluationChanged → EvaluationChanged 순서로 대기열에 둔다.
        private void QueueChanges(bool changed, ObjectiveEvaluation next, ObjectiveStatus before)
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
                    changes.Enqueue(handler);
                }
            }

            var after = instance.Status;
            if (after != before)
            {
                Action<ObjectiveStatus, ObjectiveStatus> handler = StatusChanged;
                if (handler != null)
                {
                    changes.Enqueue(handler, before, after);
                }
            }

            if (viewChanged)
            {
                Action<TaskEntryTransition> entryHandler = EntryEvaluationChanged;
                if (entryHandler != null)
                {
                    var transitions = ListPool<TaskEntryTransition>.Get();
                    try
                    {
                        next.DiffEntries(previous, transitions);
                        foreach (var transition in transitions)
                        {
                            changes.Enqueue(entryHandler, transition);
                        }
                    }
                    finally
                    {
                        ListPool<TaskEntryTransition>.Release(transitions);
                    }
                }

                Action<ObjectiveEvaluation> handler = EvaluationChanged;
                if (handler != null)
                {
                    changes.Enqueue(handler, next);
                }
            }
        }

        private void FlushChanges() => changes.Flush();

    #endregion

    }
}
