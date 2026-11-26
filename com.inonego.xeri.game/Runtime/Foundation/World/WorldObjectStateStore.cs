/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : WorldObjectStateStore.cs
수정일 : 2026-11-16

# 설명
하나의 논리 World Scope에 속한 지속 상태를 Scene 수명과 독립적으로 보관한다.
상태 내부의 게임 규칙, 복사, 네트워크 및 저장 포맷은 소유하지 않는다.
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
    /// 하나의 논리 Scope에서 WorldObjectState의 등록과 수명을 관리한다.
    /// </summary>
    // ============================================================
    public sealed class WorldObjectStateStore
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 소유하는 가변 Live State의 뷰. 독립 Snapshot이 아니다.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyCollection<WorldObjectState> States => states.Values;

        private readonly Dictionary<string, WorldObjectState> states = new(StringComparer.Ordinal);
        private readonly HashSet<string> reservedIDs = new(StringComparer.Ordinal);

    #endregion

    #region 생성

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Scope에서 소유할 초기 State를 검증해 가져온다.
        /// </summary>
        // ------------------------------------------------------------
        public WorldObjectStateStore(IEnumerable<WorldObjectState> initialStates = null)
        {
            if (initialStates == null)
            {
                return;
            }

            foreach (var state in initialStates)
            {
                Validate(state, nameof(initialStates));

                if (!states.TryAdd(state.ID, state))
                {
                    throw new ArgumentException($"중복된 WorldObject 상태 ID:{state.ID}", nameof(initialStates));
                }
            }
        }

    #endregion

    #region 조회 및 수명

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 Scope의 현재 Live State를 조회한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGetState(string id, out WorldObjectState state)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                state = null;
                return false;
            }

            return states.TryGetValue(id, out state);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 아직 등록하지 않은 State를 가져온다. 객체 참조의 소유권을 인수한다.
        /// </summary>
        // ------------------------------------------------------------
        public void AddState(WorldObjectState state)
        {
            Validate(state, nameof(state));
            RejectReserved(state.ID);

            if (!states.TryAdd(state.ID, state))
            {
                throw new InvalidOperationException($"WorldObject 상태 ID가 이미 존재합니다:{state.ID}");
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 연결되지 않은 State만 다른 인스턴스로 교체한다.
        /// </summary>
        // ------------------------------------------------------------
        public void ReplaceState(WorldObjectState state)
        {
            Validate(state, nameof(state));
            RejectReserved(state.ID);

            if (!states.ContainsKey(state.ID))
            {
                throw new InvalidOperationException($"교체할 WorldObject 상태가 없습니다:{state.ID}");
            }

            states[state.ID] = state;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 연결되지 않은 State를 제거한다. 값 제거는 게임별 삭제 정책과 별개다.
        /// </summary>
        // ------------------------------------------------------------
        public bool RemoveState(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            RejectReserved(id);
            return states.Remove(id);
        }

    #endregion

    #region Scene 연결 소유권

        // ------------------------------------------------------------
        /// <summary>
        /// 동일 논리 객체에 둘 이상의 활성 Scene Binding을 허용하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        internal void Reserve(string id)
        {
            if (!reservedIDs.Add(id))
            {
                throw new InvalidOperationException($"WorldObject가 다른 Scene Binding에 연결돼 있습니다:{id}");
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 연결 수명이 종료된 ID의 독점 예약을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void Release(string id)
        {
            reservedIDs.Remove(id);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Scene 연결이 성공한 후 준비된 새 State들을 한 번에 확정한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void CommitNewStates(IEnumerable<WorldObjectState> newStates)
        {
            var pending = ListPool<WorldObjectState>.Get();
            var seen = HashSetPool<string>.Get();

            try
            {
                foreach (var state in newStates)
                {
                    Validate(state, nameof(newStates));

                    if (!reservedIDs.Contains(state.ID) || states.ContainsKey(state.ID) || !seen.Add(state.ID))
                    {
                        throw new InvalidOperationException($"새 WorldObject 상태를 확정할 수 없습니다:{state.ID}");
                    }

                    pending.Add(state);
                }

                foreach (var state in pending)
                {
                    states.Add(state.ID, state);
                }
            }
            finally
            {
                ListPool<WorldObjectState>.Release(pending);
                HashSetPool<string>.Release(seen);
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 연결된 객체가 적용한 새 State 참조를 Store에 확정한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void ReplaceBoundState(WorldObjectState previous, WorldObjectState next)
        {
            Validate(next, nameof(next));

            if (!ReferenceEquals(previous, states[next.ID]) || !reservedIDs.Contains(next.ID))
            {
                throw new InvalidOperationException($"활성 WorldObject 상태가 변경됐습니다:{next.ID}");
            }

            states[next.ID] = next;
        }

    #endregion

    #region 검증

        private void RejectReserved(string id)
        {
            if (reservedIDs.Contains(id))
            {
                throw new InvalidOperationException($"활성 Scene Binding 중에는 상태를 교체하거나 제거할 수 없습니다:{id}");
            }
        }

        private static void Validate(WorldObjectState state, string parameterName)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.ID))
            {
                throw new ArgumentException("WorldObject 상태에는 유효한 ID가 필요합니다.", parameterName);
            }
        }

    #endregion

    }
}
