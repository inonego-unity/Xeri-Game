/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AbilityBindings.cs
수정일 : 2026-11-08

# 설명
XOrdered의 우선순위·동순위 등록 순서로 부분 Binding Layer를 합성해 현재 Entry의 Ability를 해석한다.
각 Layer는 Lease로 등록·해제하며, 고유 원소의 단순 집합이 아닌 동적 Binding 구성이다.
Base/Equipment/Form 같은 Layer 용도는 프로젝트가 정의한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

namespace inonego.Xeri.Game
{
    using Xeri;
    using Xeri.Serializable;

    // ============================================================
    /// <summary>
    /// Entry별 Ability와 우선순위를 보관하는 부분 Layer.
    /// </summary>
    // ============================================================
    public sealed class AbilityBindingLayer<TDefinition>
    where TDefinition : class
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// Layer의 고정 우선순위를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public int Priority { get; }

        private readonly Dictionary<AbilityEntryKey, AbilityInstance<TDefinition>> binding = new();

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 지정된 우선순위의 Binding Layer를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public AbilityBindingLayer(int priority)
        {
            Priority = priority;
        }

    #endregion

    #region 진입점 등록·해제 및 조회

        // ------------------------------------------------------------
        /// <summary>
        /// 하나의 Entry에 연결된 Ability를 설정하거나 교체한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Set
        (
            AbilityEntryKey entryKey,
            AbilityInstance<TDefinition> instance
        )
        {
            if (!entryKey.IsValid)
            {
                throw new ArgumentException("유효한 Ability Entry Key가 필요합니다.", nameof(entryKey));
            }

            binding[entryKey] = instance ?? throw new ArgumentNullException(nameof(instance));
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 이 Layer에 등록된 Entry 하나를 제거한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Remove(AbilityEntryKey entryKey) => binding.Remove(entryKey);

        // ------------------------------------------------------------
        /// <summary>
        /// 이 Layer에 직접 등록된 Entry만 해석한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryResolve
        (
            AbilityEntryKey entryKey,
            out AbilityInstance<TDefinition> instance
        ) => binding.TryGetValue(entryKey, out instance);

        // ------------------------------------------------------------
        /// <summary>
        /// Layer가 가진 Entry Key를 호출자 목록에 수집한다.
        /// </summary>
        // ------------------------------------------------------------
        internal void CollectEntryKey(List<AbilityEntryKey> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            foreach (var pair in binding)
            {
                destination.Add(pair.Key);
            }
        }

    #endregion

    }

    // ======================================================================
    /// <summary>
    /// <br/> Binding Layer의 등록·해제와 Entry별 Ability 조회를 관리한다.
    /// <br/> 높은 Priority, 동순위에서는 최근 등록 Layer가 우선한다.
    /// <br/> Lease 해제 시 하위 Layer의 Binding이 다시 노출된다.
    /// </summary>
    // ======================================================================
    public sealed class AbilityBindings<TDefinition>
    where TDefinition : class
    {

    #region 내부 데이터

        // ============================================================
        /// <summary>
        /// Entry Key의 결정적인 Ordinal 정렬을 제공한다.
        /// </summary>
        // ============================================================
        private sealed class EntryKeyComparer : IComparer<AbilityEntryKey>
        {

            // ------------------------------------------------------------
            /// <summary>
            /// 두 Entry Key를 Ordinal 문자열 순서로 비교한다.
            /// </summary>
            // ------------------------------------------------------------
            public int Compare(AbilityEntryKey x, AbilityEntryKey y) => StringComparer.Ordinal.Compare(x.Value, y.Value);
        }

    #endregion

    #region 필드

        private static readonly IComparer<AbilityEntryKey> entryKeyComparer = new EntryKeyComparer();

        private readonly XOrdered<int, AbilityBindingLayer<TDefinition>> layer = new();
        private readonly List<AbilityEntryKey> entryKeyScratch = new();

    #endregion

    #region 계층 등록 및 해제

        // ------------------------------------------------------------
        /// <summary>
        /// Layer를 등록하고 해제 시 원상 복귀하는 Lease를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public Lease AcquireLayer(AbilityBindingLayer<TDefinition> bindingLayer)
        {
            if (bindingLayer == null)
            {
                throw new ArgumentNullException(nameof(bindingLayer));
            }

            if (layer.Contains(bindingLayer))
            {
                throw new InvalidOperationException("같은 Ability Binding Layer를 중복 등록할 수 없습니다.");
            }

            layer.Add(bindingLayer.Priority, bindingLayer);
            return new Lease(() => layer.Remove(bindingLayer));
        }

        // ----------------------------------------------------------------------
        /// <summary>
        /// 단일 Entry의 임시 Override Layer를 만들고 해제용 Lease를 반환한다.
        /// </summary>
        // ----------------------------------------------------------------------
        public Lease AcquireOverride
        (
            AbilityEntryKey entryKey,
            AbilityInstance<TDefinition> instance,
            int priority
        )
        {
            var overrideLayer = new AbilityBindingLayer<TDefinition>(priority);
            overrideLayer.Set(entryKey, instance);
            return AcquireLayer(overrideLayer);
        }

    #endregion

    #region 바인딩 조회 및 수집

        // ------------------------------------------------------------
        /// <summary>
        /// 우선순위가 높고 동순위면 나중에 등록된 Layer의 Ability를 선택한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool TryResolve
        (
            AbilityEntryKey entryKey,
            out AbilityBinding<TDefinition> binding
        )
        {
            binding = default;
            if (!entryKey.IsValid)
            {
                return false;
            }

            // 높은 우선순위부터, 동순위에서는 최근 등록 Layer부터 검사한다.
            for (var index = layer.Count - 1; index >= 0; index--)
            {
                if (!layer[index].Value.TryResolve(entryKey, out var instance))
                {
                    continue;
                }

                binding = new AbilityBinding<TDefinition>(entryKey, instance);
                return true;
            }

            return false;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 전체 Entry를 해석하고 우선순위가 반영된 결과로 대상 목록을 교체한다.
        /// </summary>
        // ------------------------------------------------------------
        public void ResolveAll
        (
            List<AbilityBinding<TDefinition>> destination
        )
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            entryKeyScratch.Clear();

            for (var layerIndex = 0; layerIndex < layer.Count; layerIndex++)
            {
                layer[layerIndex].Value.CollectEntryKey(entryKeyScratch);
            }

            entryKeyScratch.Sort(entryKeyComparer);

            var hasPrevious = false;
            var previous = default(AbilityEntryKey);

            for (var index = 0; index < entryKeyScratch.Count; index++)
            {
                var entryKey = entryKeyScratch[index];
                if (hasPrevious && entryKey == previous)
                {
                    continue;
                }

                if (TryResolve(entryKey, out var binding))
                {
                    destination.Add(binding);
                }

                previous = entryKey;
                hasPrevious = true;
            }

            entryKeyScratch.Clear();
        }

    #endregion

    }
}