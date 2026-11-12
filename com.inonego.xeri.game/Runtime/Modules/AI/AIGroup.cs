/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AIGroup.cs
수정일 : 2026-08-16

# 설명
여러 Entity를 하나의 AI 판단 단위로 묶는 독립 런타임 집단.
IEntity.Group과 Faction 관계, 공유 인지 상태와 전투 정책은 소유하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ======================================================================
    /// <summary>
    /// IEntity.Group과 독립된 AI 판단 집단의 식별자와 구성원 수명을 소유한다.
    /// </summary>
    // ======================================================================
    [Serializable]
    public sealed class AIGroup : IReadOnlyAIGroup, IDisposable
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// AIGroup을 구분하는 프로젝트 지정 식별자.
        /// </summary>
        // ------------------------------------------------------------
        public ulong ID => id;

        [SerializeField]
        private ulong id = 0UL;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 등록된 구성원 수.
        /// </summary>
        // ------------------------------------------------------------
        public int MemberCount => MemberMap.Count;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 AIGroup 수명이 종료되었는지 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsDisposed => isDisposed;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 구성원을 Entity Key 오름차순의 읽기 전용 열거로 노출한다.
        /// </summary>
        // ------------------------------------------------------------
        public IEnumerable<IReadOnlyEntity> Members => MemberMap.Values;

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 구성원 Key와 Entity 참조를 Key 오름차순으로 보관하는 런타임 사전.
        /// </summary>
        // ------------------------------------------------------------
        private SortedDictionary<ulong, IReadOnlyEntity> MemberMap => members ??= new();

        [NonSerialized]
        private SortedDictionary<ulong, IReadOnlyEntity> members = new();

        [NonSerialized]
        private bool isDisposed = false;

    #endregion

    #region 이벤트

        // ------------------------------------------------------------
        /// <summary>
        /// AIGroup 수명이 종료된 뒤 호출된다.
        /// </summary>
        // ------------------------------------------------------------
        public event Action<IReadOnlyAIGroup> OnDisposed = null;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 지정 식별자로 AIGroup을 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AIGroup(ulong id) : base()
        {
            this.id = id;
        }

    #endregion

    #region 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// 지정 Entity Key가 현재 구성원인지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool ContainsMember(ulong entityKey)
        {
            return MemberMap.ContainsKey(entityKey);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지정 Entity Key의 현재 구성원을 찾는다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryFindMember(ulong entityKey, out IReadOnlyEntity member)
        {
            return MemberMap.TryGetValue(entityKey, out member);
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> Spawned Entity를 AIGroup 구성원으로 등록한다.
        /// <br/> 동일 객체의 중복 등록은 false를 반환하고 같은 Key의 다른 객체는 거부한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool AddMember(IReadOnlyEntity member)
        {
            EnsureNotDisposed();

            if (member == null)
            {
                throw new ArgumentNullException(nameof(member));
            }

            if (!member.HasKey || member.SpawnState != SpawnState.Spawned)
            {
                throw new InvalidOperationException
                (
                    "Spawned 상태에서 유효한 Entity Key를 가진 객체만 AIGroup에 등록할 수 있습니다."
                );
            }

            var key = member.Key;

            if (MemberMap.TryGetValue(key, out var current))
            {
                if (ReferenceEquals(current, member))
                {
                    return false;
                }

                throw new InvalidOperationException
                (
                    $"Entity Key {key}에는 이미 다른 AIGroup 구성원이 등록되어 있습니다."
                );
            }

            MemberMap.Add(key, member);
            return true;
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> 지정 Entity 참조를 AIGroup에서 제거한다.
        /// <br/> Entity Key가 이미 해제된 경우에도 참조 일치로 기존 구성원을 찾는다.
        /// </summary>
        // ----------------------------------------------------------------------
        public bool RemoveMember(IReadOnlyEntity member)
        {
            EnsureNotDisposed();

            if (member == null)
            {
                throw new ArgumentNullException(nameof(member));
            }

            ulong foundKey = 0UL;
            var found = false;

            foreach (var pair in MemberMap)
            {
                if (!ReferenceEquals(pair.Value, member)) continue;

                foundKey = pair.Key;
                found = true;
                break;
            }

            if (!found) return false;

            MemberMap.Remove(foundKey);
            return true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지정 Entity Key의 구성원을 AIGroup에서 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool RemoveMember(ulong entityKey)
        {
            EnsureNotDisposed();
            return MemberMap.Remove(entityKey);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 모든 AIGroup 구성원을 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        public void ClearMembers()
        {
            EnsureNotDisposed();
            MemberMap.Clear();
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/> AIGroup 수명을 terminal 종료하고 모든 구성원을 해제한다.
        /// <br/> 종료 뒤에는 구성원 변경을 다시 허용하지 않는다.
        /// </summary>
        // ----------------------------------------------------------------------
        public void Dispose()
        {
            if (isDisposed) return;

            // 종료 상태와 구성원 해제를 먼저 확정해 callback 재진입이 수명을 다시 변경하지 못하게 한다.
            isDisposed = true;
            MemberMap.Clear();

            var callbacks = OnDisposed;
            OnDisposed = null;
            if (callbacks == null) return;

            List<Exception> failures = null;

            foreach (Action<IReadOnlyAIGroup> callback in callbacks.GetInvocationList())
            {
                try
                {
                    callback.Invoke(this);
                }
                catch (Exception exception)
                {
                    failures ??= new List<Exception>();
                    failures.Add(exception);
                }
            }

            if (failures == null) return;

            throw failures.Count == 1
                ? failures[0]
                : new AggregateException("일부 AIGroup 종료 callback이 실패했습니다.", failures);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// AIGroup 수명이 아직 유효한지 확인한다.
        /// </summary>
        // ------------------------------------------------------------
        private void EnsureNotDisposed()
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(AIGroup));
            }
        }

    #endregion

    }
}
