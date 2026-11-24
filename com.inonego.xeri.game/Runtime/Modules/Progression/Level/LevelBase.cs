/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : LevelBase.cs
수정일 : 2026-05-01

# 설명
레벨 시스템의 추상 기반 클래스. LevelUpEventArgs 구조체도 여기에 정의한다.
레벨은 0부터 시작하며, LimitMax와 FullMax 중 작은 값이 실질적인 Max가 된다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    using Serializable; // inonego.Xeri.Serializable

    [Serializable]
    public struct LevelUpEventArgs
    {
        public int Level;
    }

    // =======================================================================================
    /// <summary>
    /// <br/>게임에서 레벨과 경험치를 관리하기 위한 클래스입니다.
    /// <br/>레벨은 0부터 시작합니다.
    /// </summary>
    // =======================================================================================
    [Serializable]
    public abstract class LevelBase : ILevel, IReadOnlyLevel
    {

    #region 필드

        [SerializeField]
        protected RangeValue<int> value = new(0, (0, 0));

        [SerializeField]
        protected int limitMax = 0;

        public virtual int Value
        {
            get => this.value.Base;
            set => this.value.Base = value;
        }

        public int Min => 0;
        public int Max => Mathf.Min(LimitMax, FullMax);

        public int LimitMax
        {
            get => limitMax;
            set
            {
                var (prev, next) = (this.limitMax, value);

                if (prev == next) return;

                this.limitMax = next;

                // 최대 레벨을 업데이트합니다.
                this.value.Range.Base = (Min, Max);
            }
        }

        public abstract int FullMax { get; }

        // ------------------------------------------------------------
        /// <summary>
        /// 레벨 업을 제한할지를 결정합니다.
        /// </summary>
        // ------------------------------------------------------------
        [SerializeField]
        private bool blockLevelUp = false;
        public bool BlockLevelUp
        {
            get => blockLevelUp;
            set => blockLevelUp = value;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 레벨에서 다음 레벨로 넘어갈 수 있는지를 반환합니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool CanLevelUp => Min <= Value && Value <= Max - 1 && !BlockLevelUp;

    #endregion

    #region 생성자

        protected LevelBase()
        {
            // Reset()은 파생 클래스에서 FullMax 설정 후 호출해야 함
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 내부 값 변경 이벤트를 외부 OnValueChange로 중계한다.
        /// </summary>
        // ------------------------------------------------------------
        private void OnBaseChangeHandler(object sender, ValueChangeEventArgs<int> e)
        {
            OnValueChange?.Invoke(this, e);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// LimitMax를 FullMax로 초기화하고 이벤트 핸들러를 등록한다.
        /// </summary>
        // ------------------------------------------------------------
        protected void Reset()
        {
            LimitMax = FullMax;

            // 이벤트 핸들러 중복 등록 방지
            value.OnBaseChange -= OnBaseChangeHandler;
            value.OnBaseChange += OnBaseChangeHandler;
        }

    #endregion

    #region 이벤트

        public virtual event EventHandler<LevelUpEventArgs> OnLevelUp;
        public virtual event ValueChangeEventHandler<int> OnValueChange;

    #endregion

    #region 메서드

        // ------------------------------------------------------------
        /// <summary>
        /// 레벨 업 시킵니다.
        /// </summary>
        // ------------------------------------------------------------
        public void LevelUp(int amount = 1)
        {
            for (int i = 0; i < amount; i++)
            {
                if (CanLevelUp)
                {
                    Value++;

                    OnLevelUp?.Invoke(this, new() { Level = Value });
                }
                else
                {
                    break;
                }
            }
        }

    #endregion

    }
}
