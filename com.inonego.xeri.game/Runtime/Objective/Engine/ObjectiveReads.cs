/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ObjectiveReads.cs
수정일 : 2026-11-26

# 설명
평가에서 Condition이 읽은 다른 Objective와 Fact 소스의 집합을 담고 비교한다.

# 특이사항, 제약사항
기본값(default)은 읽은 대상을 기록하지 않는 상태이며, 이때 Add와 Clear는 아무 일도 하지 않는다.
Create로 만든 것만 기록하고, 구조체 복사본은 같은 집합을 가리키므로 기록을 시작할 때는 Create 한 번으로 만들어 넘긴다.
기록하지 않는 상태와 기록했지만 비어 있는 상태는 IsTracking으로 구분하며, 내용 비교에서는 둘 다 빈 집합이다.
Equals·GetHashCode를 정의하지 않으며 딕셔너리 키로 쓰지 않는다. 내용 비교는 IsEquivalentTo로 한다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using inonego;
using inonego.Xeri;

namespace inonego.Xeri.Game.Objective
{
    // ============================================================
    /// <summary>
    /// 평가가 읽은 (Scope, 이름) 집합을 담는 값 형식 래퍼.
    /// </summary>
    // ============================================================
    internal readonly struct ObjectiveReads
    {

    #region 생성

        private readonly HashSet<(string Scope, string Name)> set;

        private ObjectiveReads(HashSet<(string Scope, string Name)> set)
        {
            this.set = set;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 읽은 대상을 기록하는 빈 집합을 만든다.
        /// </summary>
        // ------------------------------------------------------------
        internal static ObjectiveReads Create() => new(new HashSet<(string Scope, string Name)>());

        // ----------------------------------------------------------------------
        /// <summary>
        /// <br/>내용을 새 집합으로 복사한다. 원본이 바뀌어도 복사본은 바뀌지 않는다.
        /// <br/>비어 있으면 기록하지 않는 상태(default)를 돌려준다.
        /// </summary>
        // ----------------------------------------------------------------------
        internal ObjectiveReads Copy()
        {
            return Count == 0 ? default : new(new HashSet<(string Scope, string Name)>(set));
        }

    #endregion

    #region 기록

        // ------------------------------------------------------------
        /// <summary>
        /// 읽은 대상을 기록하는 상태인지 여부. default는 기록하지 않는다.
        /// </summary>
        // ------------------------------------------------------------
        internal bool IsTracking => set != null;

        internal int Count => set?.Count ?? 0;

        // ------------------------------------------------------------
        /// <summary>
        /// 읽기 전용 View로 노출한다. default는 빈 View다.
        /// </summary>
        // ------------------------------------------------------------
        internal ReadOnlySetView<(string Scope, string Name)> View => set == null ? default : new(set);

        // 기록하지 않는 상태에서는 아무 일도 하지 않는다.
        internal void Add(string scope, string name)
        {
            set?.Add((scope, name));
        }

        // 기록하지 않는 상태에서는 아무 일도 하지 않는다.
        internal void Clear()
        {
            set?.Clear();
        }

    #endregion

    #region 비교

        // --------------------------------------------------------------------------------
        /// <summary>
        /// <br/>두 집합의 내용이 같은지 비교한다. 기록하지 않는 상태는 빈 집합으로 본다.
        /// <br/>BCL의 SetEquals는 Unity에서 할당하므로 개수를 비교한 뒤 원소를 직접 확인한다.
        /// </summary>
        // --------------------------------------------------------------------------------
        internal bool IsEquivalentTo(ObjectiveReads other)
        {
            int count = Count;
            if (count != other.Count)
            {
                return false;
            }

            if (count == 0)
            {
                return true;
            }

            foreach (var key in set)
            {
                if (!other.set.Contains(key))
                {
                    return false;
                }
            }

            return true;
        }

    #endregion

    }
}
