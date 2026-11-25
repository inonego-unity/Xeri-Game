/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveDirectory.cs
수정일 : 2026-11-25

# 설명
Objective Engine과 Flow가 함께 실행되는 비영속 환경. 현재 회차 Engine 조회, 이름 붙은 Fact 소스, 변경 전파를 맡는다.

# 특이사항, 제약사항
저장하지 않는다. 같은 Scope·Spec에는 현재 회차 Engine 하나만 둔다. 나중에 등록한 Engine이 현재 회차다.
Condition이 읽은 다른 Objective와 Fact 소스는 평가마다 기록되고, 그 대상이 바뀌면 읽은 Engine을 dirty로 표시한다.
dirty Engine과 등록한 Flow는 Owner가 RefreshDirty로 한 번에 갱신한다. 명령 도중에는 연쇄로 갱신하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Pool;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 살아 있는 Engine을 등록하고, 서로의 변경을 전파한다.
    /// </summary>
    // ============================================================
    public sealed class ObjectiveDirectory
    {

    #region 필드

        private readonly Dictionary<(string ScopeID, string SpecID), ObjectiveEngine> engines = new();
        private readonly List<ObjectiveEngine> order = new();
        private readonly Dictionary<ObjectiveEngine, Action> changeHandlers = new();
        private readonly Dictionary<string, IValueLookup> sources = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Action> sourceHandlers = new(StringComparer.Ordinal);

        // 읽기 대상(다른 Objective의 (Scope, Spec) 또는 Fact 소스의 (null, 이름)) → 그것을 읽은 Engine들.
        private readonly Dictionary<(string Scope, string Name), HashSet<ObjectiveEngine>> readers = new();
        private readonly Dictionary<ObjectiveEngine, IReadOnlyCollection<(string Scope, string Name)>> reading = new();
        private readonly HashSet<ObjectiveEngine> dirty = new();

        // Flow도 시작 조건이 읽은 대상을 같은 방식으로 기록한다.
        private readonly List<ObjectiveFlow> flows = new();
        private readonly Dictionary<(string Scope, string Name), HashSet<ObjectiveFlow>> flowReaders = new();
        private readonly Dictionary<ObjectiveFlow, HashSet<(string Scope, string Name)>> flowReading = new();
        private readonly HashSet<ObjectiveFlow> dirtyFlows = new();

    #endregion

    #region 등록

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>Engine을 현재 회차로 등록한다.
        /// <br/>같은 Scope·Spec의 이전 등록은 교체한다.
        /// <br/>그 (Scope, Spec)을 읽던 Engine과 Flow는 조회 결과가 달라질 수 있으므로 dirty로
        /// <br/>표시한다.
        /// <br/>Engine은 이 Directory로 만들었거나 Directory 없이 만든 것이어야 한다(다른
        /// <br/>Directory면 읽기 기록이 어긋난다).
        /// </summary>
        // --------------------------------------------------------------------------------
        public void Register(ObjectiveEngine engine)
        {
            if (engine == null)
            {
                throw new ArgumentNullException(nameof(engine));
            }

            if (engine.Directory != null && !ReferenceEquals(engine.Directory, this))
            {
                throw new ArgumentException("Engine was created with a different Directory.", nameof(engine));
            }

            var key = (engine.Instance.ScopeID, engine.Spec.SpecID);
            if (engines.TryGetValue(key, out var previous))
            {
                if (ReferenceEquals(previous, engine))
                {
                    return;
                }

                Detach(previous);
            }

            engines[key] = engine;
            order.Add(engine);
            Action handler = () => OnEngineChanged(engine);
            changeHandlers[engine] = handler;
            engine.Changed += handler;
            SyncReads(engine);
            MarkReaders(key, engine);
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>현재 회차로 등록된 Engine만 등록에서 뺀다.
        /// <br/>그 (Scope, Spec)을 읽던 Engine과 Flow는 조회 결과가 달라지므로 dirty로 표시한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public bool Unregister(ObjectiveEngine engine)
        {
            if (engine == null)
            {
                throw new ArgumentNullException(nameof(engine));
            }

            var key = (engine.Instance.ScopeID, engine.Spec.SpecID);
            if (!engines.TryGetValue(key, out var current) || !ReferenceEquals(current, engine))
            {
                return false;
            }

            engines.Remove(key);
            Detach(engine);
            MarkReaders(key);
            return true;
        }

        // 등록에서 빠지는 Engine의 구독·읽기 기록·dirty 표시를 정리한다.
        private void Detach(ObjectiveEngine engine)
        {
            if (changeHandlers.TryGetValue(engine, out var handler))
            {
                engine.Changed -= handler;
                changeHandlers.Remove(engine);
            }

            order.Remove(engine);
            dirty.Remove(engine);
            ClearReads(engine);
        }

    #endregion

    #region Flow 등록

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>Flow를 변경 전파 대상으로 등록한다.
        /// <br/>시작 조건이 읽은 다른 Objective·Fact 소스가 바뀌면 RefreshDirty가
        /// <br/>Flow.Refresh를 부른다.
        /// <br/>Flow의 모든 Engine이 이 Directory를 써야 한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void RegisterFlow(ObjectiveFlow flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            if (ReferenceEquals(flow.Directory, this))
            {
                return;
            }

            flow.Attach(this);
            flows.Add(flow);
            SyncFlowReads(flow, null);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 등록한 Flow를 뺀다. Flow를 Dispose하면 자동으로 빠진다.
        /// </summary>
        // ------------------------------------------------------------
        public bool UnregisterFlow(ObjectiveFlow flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            if (!flows.Remove(flow))
            {
                return false;
            }

            ClearFlowReads(flow);
            dirtyFlows.Remove(flow);
            flow.Detach();
            return true;
        }

    #endregion

    #region Fact 소스

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>이름으로 Fact 소스를 등록한다.
        /// <br/>Condition은 ObjectiveContext.Source(name)으로 읽는다.
        /// <br/>소스가 변경을 관찰할 수 있으면(IReadOnlyValueRegistry) 변경 때 읽은 Engine을
        /// <br/>자동으로 dirty로 표시하고, 아니면 소스가 바뀐 뒤 Invalidate를 불러야 한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public void RegisterSource(string name, IValueLookup source)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Fact source name must be specified.", nameof(name));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (sources.ContainsKey(name))
            {
                throw new ArgumentException($"Fact source '{name}' is already registered.", nameof(name));
            }

            sources.Add(name, source);
            if (source is IReadOnlyValueRegistry observable)
            {
                Action handler = () => Invalidate(name);
                sourceHandlers.Add(name, handler);
                observable.OnChange += handler;
            }
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 등록한 Fact 소스를 뺀다. 그 소스를 읽던 Engine은 다음 평가에서 예외를 만난다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool UnregisterSource(string name)
        {
            if (name == null || !sources.TryGetValue(name, out var source))
            {
                return false;
            }

            if (sourceHandlers.TryGetValue(name, out var handler))
            {
                ((IReadOnlyValueRegistry)source).OnChange -= handler;
                sourceHandlers.Remove(name);
            }

            sources.Remove(name);
            Invalidate(name);
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 이름으로 Fact 소스를 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetSource(string name, out IValueLookup source)
        {
            if (name != null)
            {
                return sources.TryGetValue(name, out source);
            }

            source = null;
            return false;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 소스가 바뀌었음을 알려 그 소스를 읽은 Active Engine을 dirty로 표시한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void Invalidate(string name)
        {
            MarkReaders((null, name));
        }

    #endregion

    #region 조회

        // ------------------------------------------------------------
        /// <summary>
        /// Scope와 Spec ID로 현재 회차 Engine을 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetEngine(string scopeID, string specID, out ObjectiveEngine engine)
        {
            if (scopeID != null && specID != null)
            {
                return engines.TryGetValue((scopeID, specID), out engine);
            }

            engine = null;
            return false;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Scope와 Spec ID로 현재 회차 Instance를 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetInstance(string scopeID, string specID, out ObjectiveInstance instance)
        {
            if (TryGetEngine(scopeID, specID, out var engine))
            {
                instance = engine.Instance;
                return true;
            }

            instance = null;
            return false;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>현재 회차 Engine의 복사본을 등록 순서대로 새 목록으로 돌려준다.
        /// <br/>scopeID가 null이면 등록된 전부, 아니면 그 Scope의 Engine만 돌려준다.
        /// </summary>
        // ----------------------------------------------------------------------
        public IReadOnlyList<ObjectiveEngine> GetEngines(string scopeID = null)
        {
            var results = new List<ObjectiveEngine>();
            GetEnginesNonAlloc(results, scopeID);
            return results;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>호출자의 목록을 비우고 현재 회차 Engine을 등록 순서대로 채운 뒤 개수를 돌려준다.
        /// <br/>목록을 재사용하면 할당이 없다.
        /// <br/>scopeID가 null이면 등록된 전부, 아니면 그 Scope의 Engine만 채운다.
        /// <br/>채운 뒤의 등록 변경은 목록에 영향을 주지 않는다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public int GetEnginesNonAlloc(List<ObjectiveEngine> results, string scopeID = null)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            foreach (var engine in order)
            {
                if (scopeID == null || string.Equals(engine.Instance.ScopeID, scopeID, StringComparison.Ordinal))
                {
                    results.Add(engine);
                }
            }

            return results.Count;
        }

        // 등록을 되돌린 뒤 등록 순서를 이전 목록 순서로 맞춘다. 지금도 등록된 Engine만 남긴다. Bind 실패 복구가 쓴다.
        internal void RestoreOrder(IReadOnlyList<ObjectiveEngine> previous)
        {
            order.Clear();
            foreach (var engine in previous)
            {
                if (changeHandlers.ContainsKey(engine))
                {
                    order.Add(engine);
                }
            }
        }

    #endregion

    #region 변경 전파

        // Engine의 확정이 끝나면 그것을 읽은 다른 Engine을 dirty로 표시한다.
        private void OnEngineChanged(ObjectiveEngine engine)
        {
            MarkReaders((engine.Instance.ScopeID, engine.Spec.SpecID), engine);
        }

        private void MarkReaders((string Scope, string Name) key, ObjectiveEngine except = null)
        {
            if (readers.TryGetValue(key, out var set))
            {
                foreach (var reader in set)
                {
                    if (!ReferenceEquals(reader, except) && reader.Instance.Status == ObjectiveStatus.Active)
                    {
                        dirty.Add(reader);
                    }
                }
            }

            if (flowReaders.TryGetValue(key, out var flowSet))
            {
                foreach (var flow in flowSet)
                {
                    if (!flow.IsDisposed)
                    {
                        dirtyFlows.Add(flow);
                    }
                }
            }
        }

        // Engine이 마지막 평가에서 읽은 대상을 역색인에 맞춘다. Engine이 확정·초기 평가를 마칠 때 부른다.
        internal void SyncReads(ObjectiveEngine engine)
        {
            if (!changeHandlers.ContainsKey(engine))
            {
                return;
            }

            ClearReads(engine);
            var current = engine.Reads;
            if (current == null || current.Count == 0)
            {
                return;
            }

            reading[engine] = current;
            foreach (var key in current)
            {
                if (!readers.TryGetValue(key, out var set))
                {
                    set = new HashSet<ObjectiveEngine>();
                    readers.Add(key, set);
                }

                set.Add(engine);
            }
        }

        // Engine의 읽기 기록을 역색인에서 지운다.
        private void ClearReads(ObjectiveEngine engine)
        {
            if (reading.TryGetValue(engine, out var previous))
            {
                foreach (var key in previous)
                {
                    readers[key].Remove(engine);
                }

                reading.Remove(engine);
            }
        }

        // Flow가 마지막 확인에서 시작 조건이 읽은 대상을 역색인에 맞춘다.
        internal void SyncFlowReads(ObjectiveFlow flow, HashSet<(string Scope, string Name)> current)
        {
            if (!ReferenceEquals(flow.Directory, this))
            {
                return;
            }

            ClearFlowReads(flow);
            if (current == null || current.Count == 0)
            {
                return;
            }

            flowReading[flow] = current;
            foreach (var key in current)
            {
                if (!flowReaders.TryGetValue(key, out var set))
                {
                    set = new HashSet<ObjectiveFlow>();
                    flowReaders.Add(key, set);
                }

                set.Add(flow);
            }
        }

        private void ClearFlowReads(ObjectiveFlow flow)
        {
            if (flowReading.TryGetValue(flow, out var previous))
            {
                foreach (var key in previous)
                {
                    flowReaders[key].Remove(flow);
                }

                flowReading.Remove(flow);
            }
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>dirty인 Active Engine과 Flow를 등록 순서대로 Refresh한다.
        /// <br/>갱신이 다른 대상을 다시 dirty로 만들면 수렴할 때까지 반복한다.
        /// <br/>한 회차에서는 Engine을 먼저, 그다음 Flow를 갱신한다.
        /// <br/>갱신한 Engine과 Flow의 수를 돌려준다.
        /// <br/>한 대상이 예외를 던져도 나머지는 끝까지 갱신한다.
        /// <br/>실패한 대상은 dirty로 남기고(이 호출 안에서는 다시 시도하지 않는다) 실패를 모아
        /// <br/>마지막에 던진다. 하나면 원래 예외, 둘 이상이면 AggregateException이다.
        /// <br/>수렴하지 않으면(서로 영향을 주며 계속 바뀜) 예외를 던지고 남은 대상은 dirty로 남긴다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public int RefreshDirty()
        {
            // 대부분의 프레임은 갱신할 대상이 없으므로 할당 없이 돌아간다.
            if (dirty.Count == 0 && dirtyFlows.Count == 0)
            {
                return 0;
            }

            int refreshed = 0;
            int maximum = (order.Count + flows.Count) * 4 + 8;

            var failedEngines = HashSetPool<ObjectiveEngine>.Get();
            var failedFlows = HashSetPool<ObjectiveFlow>.Get();
            var batch = ListPool<ObjectiveEngine>.Get();
            var flowBatch = ListPool<ObjectiveFlow>.Get();

            var failures = new ExceptionBuffer();
            
            try
            {
                for (int round = 0; round < maximum; round++)
                {
                    if (dirty.Count == 0 && dirtyFlows.Count == 0)
                    {
                        if (failures.Count > 0)
                        {
                            dirty.UnionWith(failedEngines);
                            dirtyFlows.UnionWith(failedFlows);
                            failures.ThrowWhenAny("Objective refresh failed for one or more targets.");
                        }

                        return refreshed;
                    }

                    // 등록 순서로 이번 회차 대상을 정하고, 갱신 중 새로 생기는 dirty는 다음 회차에서 처리한다.
                    batch.Clear();
                    foreach (var engine in order)
                    {
                        if (dirty.Contains(engine))
                        {
                            batch.Add(engine);
                        }
                    }

                    flowBatch.Clear();
                    foreach (var flow in flows)
                    {
                        if (dirtyFlows.Contains(flow))
                        {
                            flowBatch.Add(flow);
                        }
                    }

                    dirty.Clear();
                    dirtyFlows.Clear();
                    refreshed += RefreshBatch(batch, flowBatch, failedEngines, failedFlows, ref failures);
                }

                dirty.UnionWith(failedEngines);
                dirtyFlows.UnionWith(failedFlows);
                throw new InvalidOperationException
                (
                    $"Objective refresh did not converge after {maximum} rounds; {dirty.Count + dirtyFlows.Count} target(s) are still dirty."
                );
            }
            finally
            {
                ListPool<ObjectiveFlow>.Release(flowBatch);
                ListPool<ObjectiveEngine>.Release(batch);
                HashSetPool<ObjectiveFlow>.Release(failedFlows);
                HashSetPool<ObjectiveEngine>.Release(failedEngines);
            }
        }

        // 이번 회차의 Engine과 Flow를 갱신한다. 실패한 대상은 목록에 모으고 예외를 쌓으며 나머지는 계속 갱신한다.
        private int RefreshBatch
        (
            List<ObjectiveEngine> batch, List<ObjectiveFlow> flowBatch,
            HashSet<ObjectiveEngine> failedEngines, HashSet<ObjectiveFlow> failedFlows, ref ExceptionBuffer failures
        )
        {
            int refreshed = 0;
            foreach (var engine in batch)
            {
                if (engine.Instance.Status != ObjectiveStatus.Active)
                {
                    continue;
                }

                try
                {
                    engine.Refresh();
                    refreshed++;
                }
                catch (Exception exception)
                {
                    failedEngines.Add(engine);
                    failures.Add(exception);
                }
            }

            foreach (var flow in flowBatch)
            {
                if (flow.IsDisposed)
                {
                    continue;
                }

                try
                {
                    flow.Refresh();
                    refreshed++;
                }
                catch (Exception exception)
                {
                    failedFlows.Add(flow);
                    failures.Add(exception);
                }
            }

            return refreshed;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>Active인 모든 Engine에 시간을 더한 뒤 dirty Engine을 한 번에 갱신한다.
        /// <br/>Engine마다 원자적으로 더하며 Directory 전체가 하나의 트랜잭션은 아니다.
        /// <br/>한 Engine이 실패해도 나머지에는 시간을 더하고 갱신한다. 실패한 Engine은
        /// <br/>시간이 더해지지 않은 채 남는다.
        /// <br/>실패는 모아 마지막에 던진다. 하나면 원래 예외, 둘 이상이면 AggregateException이다.
        /// <br/>예외 뒤 같은 delta로 다시 부르면 이미 더한 Engine에 중복으로 더해진다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public void AdvanceTime(double delta)
        {
            if (double.IsNaN(delta) || double.IsInfinity(delta) || delta <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delta));
            }

            var failures = new ExceptionBuffer();

            // 순회 중 등록이 바뀌어도 안전하도록 풀에서 빌린 목록에 복사해 돈다.
            var targets = ListPool<ObjectiveEngine>.Get();
            try
            {
                targets.AddRange(order);
                foreach (var engine in targets)
                {
                    if (engine.Instance.Status != ObjectiveStatus.Active)
                    {
                        continue;
                    }

                    try
                    {
                        engine.AdvanceTime(delta);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
            }
            finally
            {
                ListPool<ObjectiveEngine>.Release(targets);
            }

            try
            {
                RefreshDirty();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            failures.ThrowWhenAny("Objective time advance failed for one or more targets.");
        }

    #endregion

    }
}
