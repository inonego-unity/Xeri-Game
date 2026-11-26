/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : WorldObjectSceneBinding.cs
수정일 : 2026-11-16

# 설명
하나의 Scene 로드 단위에서 WorldObject와 지속 State를 연결하고 해제한다.
Store는 유지하며 부분 실패한 연결의 정리 책임만 이 Binding이 보유한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Pool;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// Scene 연결의 소유 및 정리 상태.
    /// </summary>
    // ============================================================
    public enum WorldObjectBindingStatus { Unbound, Bound, CleanupPending }

    // ============================================================
    /// <summary>
    /// 단일 Scene 로드 단위의 State 연결과 정리 수명을 소유한다.
    /// </summary>
    // ============================================================
    public sealed class WorldObjectSceneBinding
    {

    #region 상태

        // ------------------------------------------------------------
        /// <summary>
        /// 연결 상태. 정리 실패는 CleanupPending으로 남는다.
        /// </summary>
        // ------------------------------------------------------------
        public WorldObjectBindingStatus Status { get; private set; } = WorldObjectBindingStatus.Unbound;

        private readonly WorldObjectStateStore store;
        private readonly List<Entry> entries = new();
        private bool isBusy = false;

        private sealed class Entry
        {
            internal IWorldObject Object;
            internal string ID;
            internal WorldObjectState State;
        }

    #endregion

    #region 생성

        // ------------------------------------------------------------
        /// <summary>
        /// 연결할 상태 Scope를 전달받는다. Store 자체는 소유하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        public WorldObjectSceneBinding(WorldObjectStateStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

    #endregion

    #region 연결

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 기존 State를 연결하고 없는 State는 선택적 Factory로 만든다.
        /// <br/> Factory가 없으면 State 누락을 오류로 처리한다.
        /// <br/> Factory는 순수 생성을, BindState는 참조 연결을 담당한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void Bind(IEnumerable<IWorldObject> objects, Func<IWorldObject, WorldObjectState> createState = null)
        {
            if (objects == null)
            {
                throw new ArgumentNullException(nameof(objects));
            }

            if (isBusy || Status != WorldObjectBindingStatus.Unbound)
            {
                throw new InvalidOperationException("WorldObject Scene Binding이 이미 진행 중이거나 정리가 필요합니다.");
            }

            isBusy = true;
            var reserved = new List<string>();

            try
            {
                var targets = new List<Entry>();
                var newStates = new List<WorldObjectState>();
                var seen = HashSetPool<string>.Get();

                // ID 유효성과 중복을 우선 검사하여 불필요한 Factory 호출을 방지한다.
                try
                {
                    foreach (var obj in objects)
                    {
                        if (IsMissing(obj))
                        {
                            throw new ArgumentException("WorldObject에는 유효한 ID가 필요합니다.", nameof(objects));
                        }

                        var id = obj.ID;
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            throw new ArgumentException("WorldObject에는 유효한 ID가 필요합니다.", nameof(objects));
                        }

                        if (!seen.Add(id))
                        {
                            throw new InvalidOperationException($"Scene에 중복된 WorldObject ID가 있습니다:{id}");
                        }

                        var entry = new Entry();
                        entry.Object = obj;
                        entry.ID = id;
                        targets.Add(entry);
                    }
                }
                finally
                {
                    HashSetPool<string>.Release(seen);
                }

                if (targets.Count == 0)
                {
                    return;
                }

                foreach (var entry in targets)
                {
                    if (!store.TryGetState(entry.ID, out var state))
                    {
                        if (createState == null)
                        {
                            throw new InvalidOperationException($"WorldObject 상태가 없고 생성 정책도 제공되지 않았습니다:{entry.ID}");
                        }

                        state = createState(entry.Object);

                        if (state == null || !StringComparer.Ordinal.Equals(state.ID, entry.ID))
                        {
                            throw new InvalidOperationException($"WorldObject Factory의 상태 ID가 올바르지 않습니다:{entry.ID}");
                        }

                        newStates.Add(state);
                    }

                    if (!StringComparer.Ordinal.Equals(state.ID, entry.ID))
                    {
                        throw new InvalidOperationException($"Store의 WorldObject 상태 ID가 연결 대상과 일치하지 않습니다:{entry.ID}");
                    }

                    entry.State = state;
                }

                foreach (var entry in targets)
                {
                    store.Reserve(entry.ID);
                    reserved.Add(entry.ID);
                }

                var attempted = 0;

                try
                {
                    foreach (var entry in targets)
                    {
                        // 실패한 BindState도 부분 연결을 남겼을 수 있다.
                        attempted++;
                        entry.Object.BindState(entry.State);
                    }

                    store.CommitNewStates(newStates);
                    entries.AddRange(targets);
                    Status = WorldObjectBindingStatus.Bound;
                    reserved.Clear();
                }
                catch (Exception bindError)
                {
                    var failures = new ExceptionBuffer();
                    failures.Add(bindError);
                    var pending = new List<Entry>();

                    for (var index = attempted - 1; index >= 0; index--)
                    {
                        var entry = targets[index];

                        try
                        {
                            if (!IsMissing(entry.Object))
                            {
                                entry.Object.UnbindState();
                            }
                        }
                        catch (Exception cleanupError)
                        {
                            failures.Add(cleanupError);
                            pending.Add(entry);
                        }
                    }

                    pending.Reverse();
                    entries.AddRange(pending);
                    Status = pending.Count == 0
                        ? WorldObjectBindingStatus.Unbound
                        : WorldObjectBindingStatus.CleanupPending;

                    // 실패한 객체의 예약은 반환하지 않아 다른 Binding이 접근하지 못하게 한다.
                    var pendingIDs = HashSetPool<string>.Get();

                    try
                    {
                        foreach (var entry in pending)
                        {
                            pendingIDs.Add(entry.ID);
                        }

                        foreach (var id in reserved)
                        {
                            if (!pendingIDs.Contains(id))
                            {
                                store.Release(id);
                            }
                        }
                    }
                    finally
                    {
                        HashSetPool<string>.Release(pendingIDs);
                    }

                    reserved.Clear();

                    if (failures.Count != 1)
                    {
                        throw failures.CreateAggregateException("WorldObject 연결과 정리가 모두 실패했습니다.");
                    }

                    throw;
                }
            }
            finally
            {
                // State 준비나 Reserve 단계 실패 시 아직 연결하지 않은 예약만 해제한다.
                foreach (var id in reserved)
                {
                    store.Release(id);
                }

                isBusy = false;
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 연결을 해제하고 실패한 대상만 보존하여 재시도한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Unbind()
        {
            if (isBusy)
            {
                throw new InvalidOperationException("Scene Binding 처리 중에는 해제할 수 없습니다.");
            }

            if (Status == WorldObjectBindingStatus.Unbound)
            {
                return;
            }

            isBusy = true;
            var errors = new ExceptionBuffer();
            var pending = new List<Entry>();

            try
            {
                for (var index = entries.Count - 1; index >= 0; index--)
                {
                    var entry = entries[index];

                    try
                    {
                        if (!IsMissing(entry.Object))
                        {
                            entry.Object.UnbindState();
                        }

                        store.Release(entry.ID);
                    }
                    catch (Exception error)
                    {
                        errors.Add(error);
                        pending.Add(entry);
                    }
                }

                pending.Reverse();
                entries.Clear();
                entries.AddRange(pending);
                Status = pending.Count == 0
                    ? WorldObjectBindingStatus.Unbound
                    : WorldObjectBindingStatus.CleanupPending;
            }
            finally
            {
                isBusy = false;
            }

            if (errors.Count != 0)
            {
                throw errors.CreateAggregateException("WorldObject Scene Binding 해제에 실패했습니다.");
            }
        }

    #endregion

    #region 확정 상태 적용

        // ------------------------------------------------------------
        /// <summary>
        /// 외부 확정 상태를 연결된 객체에 적용한다.
        /// </summary>
        // ------------------------------------------------------------
        public void ApplyCurrentState(WorldObjectState next)
        {
            if (next == null || string.IsNullOrWhiteSpace(next.ID))
            {
                throw new ArgumentException("적용할 WorldObject 상태가 유효하지 않습니다.", nameof(next));
            }

            if (isBusy || Status != WorldObjectBindingStatus.Bound)
            {
                throw new InvalidOperationException("정상적으로 연결된 Scene 객체에서만 현재 상태를 적용할 수 있습니다.");
            }

            Entry target = null;

            foreach (var entry in entries)
            {
                if (StringComparer.Ordinal.Equals(entry.ID, next.ID))
                {
                    target = entry;
                    break;
                }
            }

            if (target == null || IsMissing(target.Object))
            {
                throw new InvalidOperationException($"연결된 WorldObject가 없습니다:{next.ID}");
            }

            if (target.Object is not IWorldObjectCurrentStateReceiver receiver)
            {
                throw new NotSupportedException($"현재 상태 적용을 지원하지 않는 WorldObject입니다:{next.ID}");
            }

            isBusy = true;

            try
            {
                // Validation 실패에는 상태를 변경하지 않는다.
                receiver.ValidateCurrentState(next);

                try
                {
                    receiver.ApplyCurrentState(next);
                    store.ReplaceBoundState(target.State, next);
                    target.State = next;
                }
                catch
                {
                    Status = WorldObjectBindingStatus.CleanupPending;
                    throw;
                }
            }
            finally
            {
                isBusy = false;
            }
        }

    #endregion

    #region 공통 검증

        // ------------------------------------------------------------
        /// <summary>
        /// Unity에서 파괴된 객체도 연결 대상에서 제외한다.
        /// </summary>
        // ------------------------------------------------------------
        private static bool IsMissing(IWorldObject obj)
        {
            return obj == null || obj is UnityEngine.Object unityObject && unityObject == null;
        }

    #endregion

    }
}
