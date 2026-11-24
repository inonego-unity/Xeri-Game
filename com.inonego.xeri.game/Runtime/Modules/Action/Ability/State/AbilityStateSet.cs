/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityStateSet.cs
수정일 : 2026-11-24

# 설명
한 Owner의 AbilityState를 Ability Key마다 하나씩 보관하고 Key 조회와 일괄 시간 진행을 제공한다.
Binding·Selector·Execution은 참조하지 않으며 State 자체의 규칙(Revision, 알림, 복제)은 각 State가 맡는다.

# 특이사항, 제약사항
저장 원본은 State 목록 하나이고 Key 조회 표는 역직렬화 뒤 다시 만든다. Key가 State 안에 있으므로 Key를 따로 저장하지 않는다.
역직렬화 중 null·Key 없음·중복 Key State는 첫 항목만 남기고 버린다.
Advance는 시작 시점의 구성원만 진행하고, 진행 중 제거된 State는 건너뛰며, State별 실패를 모아 모두 진행한 뒤 전달한다.
집합 자체의 Revision·변경 알림은 없다. 메모리 안의 복원은 CloneFrom으로 하며 Key 조회 표의 Core Dictionary.CloneFrom에 맡긴다. Key가 같은 State는 인스턴스를 유지한 채
State별 CloneFrom으로 덮어쓰고, source에만 있는 Key는 새 State로 추가하며 대상에만 있는 Key는 제거한다. 덮어쓰거나 제거할 State 중 사용 중인 것이 있으면 아무것도 바꾸기 전에 예외를 던진다.
새 State 복제가 실패하면 집합은 그대로이고, 기존 State 덮어쓰기가 실패하면 나머지를 끝까지 진행해 Key 구성과 목록 순서를 source에 맞춘 뒤 실패를 전달한다(실패한 State는 일부만 바뀔 수 있다).
FromJsonOverwrite는 같은 개수일 때 기존 State 인스턴스를 Key가 아닌 인덱스 순서로 재사용하고, 개수가 다르면 목록을 새 인스턴스로 교체한다(Unity 6000.7.0a6 측정).
AbilityState 파생 타입을 넣으면 Unity 직렬화는 AbilityState 필드만 저장한다.
Advance가 빌리는 구성원 목록은 Unity ListPool에서 가져오므로 반복 호출이 목록을 할당하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Pool;

namespace inonego.Xeri.Game
{
    // ======================================================================
    /// <summary>
    /// <br/> 한 Owner의 AbilityState를 Ability Key마다 하나씩 보관한다.
    /// <br/> Key 조회, 추가·제거와 일괄 시간 진행을 제공한다.
    /// </summary>
    // ======================================================================
    [Serializable]
    public sealed class AbilityStateSet : ISerializationCallbackReceiver, IDeepCloneable<AbilityStateSet>
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 추가 순서대로 State 목록을 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public IReadOnlyList<AbilityState> States => states;

        [SerializeField]
        private List<AbilityState> states = new();

        // ------------------------------------------------------------
        /// <summary>
        /// 보관한 State 수를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public int Count => states.Count;

        // Key 조회 표. 저장하지 않고 State 목록에서 다시 만든다.
        [NonSerialized]
        private Dictionary<AbilityKey, AbilityState> lookup = new();

    #endregion

    #region 복제

        // ------------------------------------------------------------
        /// <summary>
        /// 복제에 쓸 빈 AbilityStateSet을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityStateSet @new() => new();

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> source와 같은 Key 집합이 되도록 복제한다.
        /// <br/> 알림 없이 State마다 Revision이 1 오른다.
        /// <br/> Key가 같은 State는 인스턴스를 유지한 채 State별 CloneFrom으로 덮어쓴다.
        /// <br/> source에만 있는 Key는 새 State로 추가하고 대상에만 있는 Key는 제거한다.
        /// <br/> 목록 순서는 source를 따른다.
        /// <br/> 이 집합의 State 중 사용 중인 것이 있으면
        /// <br/> 아무것도 바꾸기 전에 InvalidOperationException을 던진다.
        /// <br/> 새 State를 복제하다 실패하면 집합은 바뀌지 않는다.
        /// <br/> 기존 State를 덮어쓰다 실패하면 나머지를 끝까지 진행하고
        /// <br/> 구성을 source에 맞춘 뒤 실패를 전달한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public void CloneFrom(AbilityStateSet source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (ReferenceEquals(source, this))
            {
                return;
            }

            // 덮어쓰거나 제거할 State가 사용 중이면 중간 상태를 만들지 않도록 먼저 거부한다.
            foreach (var state in states)
            {
                if (state.IsInUse)
                {
                    throw new InvalidOperationException("사용 중인 AbilityState가 있는 집합은 복제로 덮어쓸 수 없습니다.");
                }
            }

            // Key 구성과 State 덮어쓰기는 Core Dictionary.CloneFrom이 정하고 목록 순서는 그 결과로 맞춘다.
            try
            {
                lookup.CloneFrom(source.lookup);
            }
            finally
            {
                SyncOrder(source);
            }
        }

        // 조회 표가 source와 같은 Key 구성이 되었을 때만 목록을 source 순서의 현재 State로 다시 채운다.
        private void SyncOrder(AbilityStateSet source)
        {
            if (lookup.Count != source.states.Count)
            {
                return;
            }

            foreach (var sourceState in source.states)
            {
                if (!lookup.ContainsKey(sourceState.Key))
                {
                    return;
                }
            }

            states.Clear();

            foreach (var sourceState in source.states)
            {
                states.Add(lookup[sourceState.Key]);
            }
        }

    #endregion

    #region 조회

        // ------------------------------------------------------------
        /// <summary>
        /// key의 State를 찾는다. 유효하지 않은 key는 찾지 못한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryGet(AbilityKey key, out AbilityState state)
        {
            if (!key.IsValid)
            {
                state = null;
                return false;
            }

            return lookup.TryGetValue(key, out state);
        }

    #endregion

    #region 추가와 제거

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> key의 State를 반환하고, 없으면 새로 만들어 추가한다.
        /// <br/> key가 유효하지 않으면 ArgumentException을 던진다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityState GetOrAdd(AbilityKey key)
        {
            if (!key.IsValid)
            {
                throw new ArgumentException("유효한 Ability Key가 필요합니다.", nameof(key));
            }

            if (lookup.TryGetValue(key, out var state))
            {
                return state;
            }

            state = new AbilityState(key);
            Append(state);

            return state;
        }

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 구성한 State를 추가한다.
        /// <br/> Key가 없거나 같은 Key의 State가 이미 있으면 ArgumentException을 던진다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public void Add(AbilityState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (!state.Key.IsValid)
            {
                throw new ArgumentException("유효한 Ability Key를 가진 State만 추가할 수 있습니다.", nameof(state));
            }

            if (lookup.ContainsKey(state.Key))
            {
                throw new ArgumentException("같은 Ability Key의 State가 이미 있습니다.", nameof(state));
            }

            Append(state);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> key의 State를 제거하고 제거 여부를 반환한다.
        /// <br/> 사용 중인 State는 제거하지 않고 InvalidOperationException을 던진다.
        /// <br/> Binding에 연결된 Instance는 바꾸지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool Remove(AbilityKey key)
        {
            if (!TryGet(key, out var state))
            {
                return false;
            }

            if (state.IsInUse)
            {
                throw new InvalidOperationException("사용 중인 AbilityState는 제거할 수 없습니다.");
            }

            lookup.Remove(key);
            states.Remove(state);

            return true;
        }

        // 조회 표와 목록에 함께 넣는다.
        private void Append(AbilityState state)
        {
            lookup.Add(state.Key, state);
            states.Add(state);
        }

    #endregion

    #region 시간 진행

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/> 모든 State를 deltaTime만큼 진행한다.
        /// <br/> 진행 중 추가된 State는 이번 진행에 넣지 않고, 제거된 State는 건너뛴다.
        /// <br/> 한 State의 예외가 나머지 진행을 막지 않으며 모두 진행한 뒤 예외를 전달한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        public void Advance(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            var members = ListPool<AbilityState>.Get();
            var failures = new ExceptionBuffer();

            try
            {
                members.AddRange(states);

                foreach (var state in members)
                {
                    if (!IsMember(state))
                    {
                        continue;
                    }

                    try
                    {
                        state.Advance(deltaTime);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                }
            }
            finally
            {
                ListPool<AbilityState>.Release(members);
            }

            // State 예외 하나는 원래 스택 추적을 유지한 채 다시 던진다.
            failures.ThrowWhenAny("AbilityState 진행 중 여러 State가 실패했습니다.");
        }

        // 아직 이 집합에 있는 같은 인스턴스인지 확인한다.
        private bool IsMember(AbilityState state)
        {
            return lookup.TryGetValue(state.Key, out var current) && ReferenceEquals(current, state);
        }

    #endregion

    #region 직렬화 복원

        // ------------------------------------------------------------
        /// <summary>
        /// 직렬화 전에 할 일이 없다.
        /// </summary>
        // ------------------------------------------------------------
        public void OnBeforeSerialize()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> 복원된 State 목록으로 Key 조회 표를 다시 만든다.
        /// <br/> null·Key 없음·중복 Key State는 첫 항목만 남기고 버린다.
        /// </summary>
        // ------------------------------------------------------------
        public void OnAfterDeserialize()
        {
            states ??= new List<AbilityState>();
            lookup ??= new Dictionary<AbilityKey, AbilityState>();
            lookup.Clear();

            var kept = 0;

            for (var index = 0; index < states.Count; index++)
            {
                var state = states[index];

                if (state == null || !state.Key.IsValid || !lookup.TryAdd(state.Key, state))
                {
                    continue;
                }

                states[kept++] = state;
            }

            states.RemoveRange(kept, states.Count - kept);
        }

    #endregion

    }
}
