/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveFlow.cs
수정일 : 2026-11-26

# 설명
Objective 선행 성공, Scope 경계, 단회 Choice와 새 회차 교체의 실행을 조정한다.

# 특이사항, 제약사항
선택 기록은 ObjectiveFlowState가 소유하며 Objective Progress는 각 Instance가 소유한다.
Choice는 모든 대안의 취소를 계획한 뒤 선택 기록과 함께 확정한다. 계획이 실패하면 아무것도 바뀌지 않는다.
한 FlowState와 한 Engine은 살아 있는 Flow 하나만 실행한다. Dispose로 소유를 놓아야 같은 객체로 다시 만들 수 있다.
Objective마다 현재 회차 Engine 하나를 가진다. 새 회차 교체는 종료한 회차를 되살리지 않고 새 Instance의 Engine으로 바꾼다.
Auto Entry는 Engine의 StatusChanged와 Flow 명령 뒤에 시작 조건을 다시 확인해 자동으로 시작한다. 생성자는 시작하지 않는다.
Directory에 등록하면 시작 조건이 읽은 다른 Objective·Fact 소스가 바뀔 때 Directory의 RefreshDirty가 Refresh를 대신 부른다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Pool;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 한 Scope의 Objective 실행을 선행·배타 규칙으로 연결한다.
    /// </summary>
    // ============================================================
    public sealed class ObjectiveFlow : IDisposable
    {

    #region 필드·프로퍼티

        private readonly Dictionary<string, ObjectiveFlowEntry> entries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ObjectiveFlowChoice> choices = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ObjectiveEngine> engines = new(StringComparer.Ordinal);
        private readonly Dictionary<ObjectiveEngine, Action<ObjectiveStatus, ObjectiveStatus>> statusHandlers = new();
        private bool advancing;

        // 이 Flow를 변경 전파 대상으로 등록한 Directory. 등록하지 않았으면 null이다.
        internal ObjectiveDirectory Directory { get; private set; }

        // 시작 조건이 읽은 대상의 두 집합. 하나는 Directory에 넘긴 마지막 기록이고, 다른 하나에 다음 확인을 모은다.
        private HashSet<(string Scope, string Name)> syncedReads;
        private HashSet<(string Scope, string Name)> spareReads;

        // ------------------------------------------------------------
        /// <summary>
        /// Flow 소유자의 직접 영속 선택 기록.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveFlowState State => state;
        private readonly ObjectiveFlowState state;

        // ------------------------------------------------------------
        /// <summary>
        /// 등록된 선행·선택 관계 명세.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveFlowSpec Spec => spec;
        private readonly ObjectiveFlowSpec spec;

    #endregion

    #region 생성자·복원

        // ------------------------------------------------------------
        /// <summary>
        /// 같은 Scope의 Objective들을 이미 복원한 FlowState와 연결한다.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveFlow
        (
            ObjectiveFlowSpec spec, ObjectiveFlowState state,
            IEnumerable<ObjectiveEngine> source
        )
        {
            this.spec = spec ?? throw new ArgumentNullException(nameof(spec));
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            state.Validate();
            if (spec.FlowID != state.FlowID || spec.Version != state.Version)
            {
                throw new ArgumentException
                (
                    $"Flow spec '{spec.FlowID}' r{spec.Version} and saved state '{state.FlowID}' r{state.Version} do not match."
                );
            }

            foreach (var entry in spec.Entries)
            {
                entries.Add(entry.ObjectiveID, entry);
            }

            foreach (var choice in spec.Choices)
            {
                choices.Add(choice.ChoiceID, choice);
            }

            foreach (var engine in source)
            {
                if (engine == null || !entries.ContainsKey(engine.Spec.SpecID)
                    || !string.Equals(engine.Instance.ScopeID, state.ScopeID, StringComparison.Ordinal)
                    || !engines.TryAdd(engine.Spec.SpecID, engine))
                {
                    throw new ArgumentException
                    (
                        $"Flow Objective registry contains a mismatch: '{engine?.Spec.SpecID}'.", nameof(source)
                    );
                }
            }

            if (engines.Count != entries.Count)
            {
                throw new ArgumentException($"Flow has {engines.Count} Objective engines but its Spec lists {entries.Count}.");
            }

            foreach (var selection in state.Selections)
            {
                if (!choices.TryGetValue(selection.ChoiceID, out var choice)
                    || !choice.Includes(selection.ObjectiveID))
                {
                    throw new InvalidOperationException
                    (
                        $"Saved Flow selection does not match its spec: choice '{selection.ChoiceID}', objective '{selection.ObjectiveID}'."
                    );
                }

                foreach (var other in choice.ObjectiveIDs)
                {
                    if (other != selection.ObjectiveID
                        && engines[other].Instance.Status != ObjectiveStatus.Cancelled)
                    {
                        throw new InvalidOperationException
                        (
                            $"Unselected Objective '{other}' was not cancelled (choice '{selection.ChoiceID}')."
                        );
                    }
                }
            }

            // 한 상태와 한 Engine은 살아 있는 Flow 하나만 실행한다. 모두 비어 있을 때만 함께 차지한다.
            if (state.Owner != null)
            {
                throw new InvalidOperationException("Flow state is already bound to another Flow.");
            }

            foreach (var engine in engines.Values)
            {
                if (engine.Flow != null)
                {
                    throw new InvalidOperationException("Objective engine is already bound to another Flow.");
                }
            }

            state.Owner = this;
            foreach (var engine in engines.Values)
            {
                Claim(engine);
            }
        }

    #endregion

    #region 수명·연결

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>상태와 Engine의 실행 소유를 놓는다.
        /// <br/>같은 상태·Engine으로 새 Flow를 만들 수 있고, 이 Flow의 명령은 거부한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            state.Owner = null;
            foreach (var engine in engines.Values)
            {
                Release(engine);
            }

            Directory?.UnregisterFlow(this);
        }

        // Engine을 이 Flow의 실행 대상으로 차지한다. Status 변경을 구독해 Auto Entry를 다시 확인한다.
        private void Claim(ObjectiveEngine engine)
        {
            engine.Flow = this;
            Action<ObjectiveStatus, ObjectiveStatus> handler = (before, after) => Advance();
            statusHandlers[engine] = handler;
            engine.StatusChanged += handler;
        }

        // Claim한 Engine의 소유와 구독을 놓는다.
        private void Release(ObjectiveEngine engine)
        {
            engine.Flow = null;
            if (statusHandlers.TryGetValue(engine, out var handler))
            {
                engine.StatusChanged -= handler;
                statusHandlers.Remove(engine);
            }
        }

        // Directory에 등록될 때 호출한다. 모든 Engine이 같은 Directory를 써야 읽기 기록이 전파에 맞는다.
        internal void Attach(ObjectiveDirectory directory)
        {
            RequireNotDisposed();
            if (Directory != null && !ReferenceEquals(Directory, directory))
            {
                throw new InvalidOperationException("Flow is already registered in another Directory.");
            }

            foreach (var engine in engines.Values)
            {
                if (!ReferenceEquals(engine.Directory, directory))
                {
                    throw new InvalidOperationException
                    (
                        $"Objective engine '{engine.Spec.SpecID}' does not use the Directory this Flow is registered in."
                    );
                }
            }

            Directory = directory;
            syncedReads = null;
        }

        internal void Detach()
        {
            Directory = null;
            syncedReads = null;
        }

        internal bool IsDisposed => disposed;
        private bool disposed;

        private void RequireNotDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ObjectiveFlow));
            }
        }

    #endregion

    #region 조회

        // --------------------------------------------------------------------------------
        /// <summary>
        /// Objective의 현재 회차 Engine을 조회한다. 새 회차로 교체하면 교체된 Engine을 돌려준다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public ObjectiveEngine GetEngine(string objectiveID)
        {
            RequireNotDisposed();
            if (objectiveID == null || !engines.TryGetValue(objectiveID, out var engine))
            {
                throw new ArgumentException($"Unknown Flow Objective: '{objectiveID}'.", nameof(objectiveID));
            }

            return engine;
        }

    #endregion

    #region 활성 조건

        // ------------------------------------------------------------
        /// <summary>
        /// Choice 선택과 모든 선행 성공을 만족할 때만 개방한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool CanActivate(string objectiveID) => CanActivate(objectiveID, null);

        // reads를 주면 시작 조건이 읽은 대상을 기록한다.
        private bool CanActivate(string objectiveID, HashSet<(string Scope, string Name)> reads)
        {
            RequireNotDisposed();
            if (objectiveID == null || !entries.TryGetValue(objectiveID, out var entry))
            {
                throw new ArgumentException($"Unknown Flow Objective: '{objectiveID}'.", nameof(objectiveID));
            }

            if (engines[objectiveID].Instance.Status != ObjectiveStatus.Inactive)
            {
                return false;
            }

            foreach (var predecessor in entry.PredecessorIDs)
            {
                if (engines[predecessor].Instance.Status != ObjectiveStatus.Succeeded)
                {
                    return false;
                }
            }

            if (entry.ActivationCond != null && !engines[objectiveID].CheckActivation(entry.ActivationCond, reads))
            {
                return false;
            }

            if (spec.TryGetChoice(objectiveID, out var choice)
                && (!state.TryGetSelection(choice.ChoiceID, out var selected) || selected != objectiveID))
            {
                return false;
            }

            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Flow의 선행·Choice Gate를 통과할 때만 시작한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Activate(string objectiveID)
        {
            return CanActivate(objectiveID) && engines[objectiveID].ActivateCore();
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>Auto Entry의 시작 조건을 확인해 충족한 것을 시작한다.
        /// <br/>시작이 다른 Entry의 조건을 충족시키면 이어서 시작한다.
        /// <br/>외부 Fact가 바뀐 뒤에는 이 메서드를 불러야 한다.
        /// <br/>시작한 Objective 수를 돌려준다.
        /// </summary>
        // ------------------------------------------------------------
        public int Refresh()
        {
            RequireNotDisposed();
            return Advance();
        }

        // 시작할 Auto Entry가 더 없을 때까지 반복한다. Engine 알림에서 다시 불려도 중첩 실행하지 않는다.
        private int Advance()
        {
            if (advancing || disposed)
            {
                return 0;
            }

            advancing = true;
            int started = 0;
            HashSet<(string Scope, string Name)> reads = null;
            if (Directory != null)
            {
                reads = spareReads ??= new HashSet<(string Scope, string Name)>();
                reads.Clear();
            }

            try
            {
                int maximum = entries.Count + 1;
                for (int pass = 0; pass < maximum; pass++)
                {
                    bool any = false;
                    foreach (var entry in spec.Entries)
                    {
                        if (entry.Auto && CanActivate(entry.ObjectiveID, reads) && engines[entry.ObjectiveID].ActivateCore())
                        {
                            started++;
                            any = true;
                        }
                    }

                    if (!any)
                    {
                        break;
                    }
                }
            }
            finally
            {
                advancing = false;
            }

            // 마지막 확인에서 시작 조건이 읽은 대상을 Directory가 변경 전파에 쓰도록 넘긴다.
            // 지난번과 같으면 역색인을 그대로 두고, 바뀌었으면 넘긴 뒤 Directory가 놓은 이전 집합을 다음 확인에 쓴다.
            if (Directory != null && !ObjectiveDirectory.HasSameReads(reads, syncedReads))
            {
                Directory.SyncFlowReads(this, reads);
                spareReads = syncedReads;
                syncedReads = reads;
            }

            return started;
        }

    #endregion

    #region 단회 Choice

        // ------------------------------------------------------------
        /// <summary>
        /// 한 대안을 확정한 뒤 나머지 선택지를 Cancelled로 종료한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool SelectChoice(string choiceID, string objectiveID)
        {
            RequireNotDisposed();
            if (choiceID == null || !choices.TryGetValue(choiceID, out var choice)
                || !choice.Includes(objectiveID))
            {
                throw new ArgumentException($"Unknown Flow Choice or alternative: '{choiceID}' / '{objectiveID}'.");
            }

            if (state.TryGetSelection(choiceID, out var selected))
            {
                if (selected == objectiveID)
                {
                    return false;
                }

                throw new InvalidOperationException("Choice has already been resolved.");
            }

            if (engines[objectiveID].Instance.IsTerminal)
            {
                throw new InvalidOperationException("A terminal Objective cannot be chosen.");
            }

            // 선택 이전에 모든 취소 대상의 상태 전이 가능 여부를 검증한다.
            foreach (var id in choice.ObjectiveIDs)
            {
                if (id == objectiveID)
                {
                    continue;
                }

                var status = engines[id].Instance.Status;
                if (status == ObjectiveStatus.Succeeded || status == ObjectiveStatus.Failed)
                {
                    throw new InvalidOperationException("A resolved alternative cannot be cancelled.");
                }
            }

            // 모든 취소를 먼저 계획한다. 하나라도 실패하면 선택도 취소도 반영하지 않는다.
            var plans = ListPool<(ObjectiveEngine Engine, ObjectiveInstance Candidate)>.Get();
            var failures = new ExceptionBuffer();

            try
            {
                foreach (var id in choice.ObjectiveIDs)
                {
                    if (id != objectiveID && engines[id].Instance.Status != ObjectiveStatus.Cancelled)
                    {
                        var engine = engines[id];
                        plans.Add((engine, engine.PlanCancelForChoice()));
                    }
                }

                // 계획이 모두 끝난 뒤 선택 기록과 취소를 함께 확정한다. 확정은 실패하지 않는 대입이다.
                state.Select(choiceID, objectiveID);
                foreach (var plan in plans)
                {
                    plan.Engine.CommitCancelForChoice(plan.Candidate);
                }

                // 모든 확정 뒤에 알린다. 한 대안의 구독자가 실패해도 다른 대안의 알림은 전달하고, 실패는 모아 마지막에 던진다.
                foreach (var plan in plans)
                {
                    try
                    {
                        plan.Engine.FlushChoiceChanges();
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
            }
            finally
            {
                ListPool<(ObjectiveEngine Engine, ObjectiveInstance Candidate)>.Release(plans);
            }

            failures.ThrowWhenAny("Choice alternative change handlers failed.");
            Advance();
            return true;
        }

    #endregion

    #region 새 회차 교체

        // ------------------------------------------------------------
        /// <summary>
        /// <br/>종료한 Objective를 새 InstanceID의 새 회차로 교체한다.
        /// <br/>같은 Spec의 Repeatable 정책이 필요하다.
        /// <br/>Choice가 확정됐다면 선택한 대안만 교체할 수 있다.
        /// <br/>선택받지 못한 대안은 Cancelled로 남아야 저장이 다시 열린다.
        /// </summary>
        // ------------------------------------------------------------
        public ObjectiveEngine Repeat(string objectiveID, string newInstanceID)
        {
            RequireNotDisposed();
            var engine = GetEngine(objectiveID);
            if (spec.TryGetChoice(objectiveID, out var choice)
                && state.TryGetSelection(choice.ChoiceID, out var selected) && selected != objectiveID)
            {
                throw new InvalidOperationException("An unselected alternative of a resolved Choice cannot be repeated.");
            }

            // 새 회차를 만드는 일이 마지막 실패 지점이다. 이후 교체는 실패하지 않는 대입이다.
            var repeated = engine.CreateRepeatCore(newInstanceID);
            Replace(objectiveID, repeated);
            Advance();
            return repeated;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>확정한 Choice를 다시 연다.
        /// <br/>선택 기록을 지우고 모든 대안을 새 회차로 교체한다.
        /// <br/>선택한 대안이 아직 끝나지 않았다면 먼저 Cancelled로 종료한다.
        /// <br/>성공한 대안은 다시 열 수 없다.
        /// <br/>모든 대안이 Repeatable이어야 하며, 하나라도 실패하면 아무것도 바뀌지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public IReadOnlyList<ObjectiveEngine> ReopenChoice(string choiceID, Func<string, string> newInstanceID)
        {
            RequireNotDisposed();
            if (choiceID == null || !choices.TryGetValue(choiceID, out var choice))
            {
                throw new ArgumentException($"Unknown Flow Choice: '{choiceID}'.", nameof(choiceID));
            }

            if (newInstanceID == null)
            {
                throw new ArgumentNullException(nameof(newInstanceID));
            }

            if (!state.TryGetSelection(choiceID, out var selected))
            {
                throw new InvalidOperationException("Choice has not been resolved.");
            }

            var chosen = engines[selected];
            if (chosen.Instance.Status == ObjectiveStatus.Succeeded)
            {
                throw new InvalidOperationException("A succeeded alternative cannot be reopened.");
            }

            // 선택한 대안의 종료와 모든 새 회차를 먼저 만든다. 어느 하나라도 실패하면 아무것도 바뀌지 않는다.
            ObjectiveInstance cancellation = null;
            if (!chosen.Instance.IsTerminal)
            {
                cancellation = chosen.PlanCancelForChoice();
            }

            var replacements = ListPool<(string ObjectiveID, ObjectiveEngine Engine)>.Get();
            List<ObjectiveEngine> result = null;

            try
            {
                foreach (var id in choice.ObjectiveIDs)
                {
                    replacements.Add((id, engines[id].NewRun(newInstanceID(id))));
                }

                // 선택 기록, 선택한 대안의 종료, 회차 교체를 함께 확정한다. 확정은 실패하지 않는 대입이다.
                if (cancellation != null)
                {
                    chosen.CommitCancelForChoice(cancellation);
                }

                state.ClearSelection(choiceID);
                result = new List<ObjectiveEngine>(replacements.Count);
                foreach (var replacement in replacements)
                {
                    Replace(replacement.ObjectiveID, replacement.Engine);
                    replacement.Engine.Publish();
                    result.Add(replacement.Engine);
                }
            }
            finally
            {
                ListPool<(string ObjectiveID, ObjectiveEngine Engine)>.Release(replacements);
            }

            // 확정이 끝난 뒤에 알린다. 알림 예외는 확정을 되돌리지 않는다.
            chosen.FlushChoiceChanges();
            Advance();
            return result;
        }

        // 현재 회차 Engine을 바꾼다. 이전 Engine은 Flow 소유를 놓고, 새 Engine이 소유한다.
        private void Replace(string objectiveID, ObjectiveEngine next)
        {
            Release(engines[objectiveID]);
            engines[objectiveID] = next;
            Claim(next);
        }

    #endregion

    }
}
