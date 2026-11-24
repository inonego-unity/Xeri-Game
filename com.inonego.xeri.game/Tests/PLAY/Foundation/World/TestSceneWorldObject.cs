/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TestSceneWorldObject.cs
수정일 : 2026-11-13

# 설명
PlayMode 테스트에서 Scene에 생성할 수 있는 IWorldObject MonoBehaviour와 상태.
WorldObjectSceneBinding의 Unity Scene 수명 결합을 검증한다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game.TEST._World
{
    // ============================================================
    /// <summary>
    /// 테스트 Unity Scene에서 지속 값을 소비하는 MonoBehaviour.
    /// </summary>
    // ============================================================
    public sealed class TestSceneWorldObject : MonoBehaviour, IWorldObject, IWorldObjectCurrentStateReceiver
    {

    #region 상태

        // ------------------------------------------------------------
        /// <summary>
        /// World Object의 안정 ID.
        /// </summary>
        // ------------------------------------------------------------
        public string ID => id;

        [SerializeField]
        private string id = "";

        [SerializeField]
        private int initialValue = 0;

        // ------------------------------------------------------------
        /// <summary>
        /// 상태 연결 여부.
        /// </summary>
        // ------------------------------------------------------------
        public bool IsBound => state != null;

        // ------------------------------------------------------------
        /// <summary>
        /// 연결된 지속 상태의 값.
        /// </summary>
        // ------------------------------------------------------------
        public int Value => state?.Value ?? 0;

        [NonSerialized]
        private TestSceneWorldState state = null;

        // ------------------------------------------------------------
        /// <summary>
        /// 정상 Live 상호작용으로 변경한 횟수.
        /// </summary>
        // ------------------------------------------------------------
        public int LiveChangeCount { get; private set; }

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 확정 State를 적용한 횟수.
        /// </summary>
        // ------------------------------------------------------------
        public int CurrentApplyCount { get; private set; }

        // ------------------------------------------------------------
        /// <summary>
        /// State 참조를 교체한 직후 적용 실패를 유도한다.
        /// </summary>
        // ------------------------------------------------------------
        public bool FailCurrentApply { get; set; }

    #endregion

    #region 초기화

        // ------------------------------------------------------------
        /// <summary>
        /// Scene 객체의 authoring 값을 초기화한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Configure(string worldObjectID, int initialValue)
        {
            id = worldObjectID;
            this.initialValue = initialValue;
        }

    #endregion

    #region 상태 연결

        // ------------------------------------------------------------
        /// <summary>
        /// 테스트 Domain Factory에서 호출할 지속 상태 생성 메서드.
        /// </summary>
        // ------------------------------------------------------------
        public WorldObjectState CreateState() => new TestSceneWorldState(ID, initialValue);

        // ------------------------------------------------------------
        /// <summary>
        /// 복원 또는 새 상태를 Scene 객체에 연결한다.
        /// </summary>
        // ------------------------------------------------------------
        public void BindState(WorldObjectState value)
        {
            if (value is not TestSceneWorldState next || next.ID != ID || state != null)
            {
                throw new InvalidOperationException("Scene 객체의 상태 타입 또는 ID가 일치하지 않습니다.");
            }

            state = next;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 지속 상태를 보존한 채 Scene 객체에서 연결을 해제한다.
        /// </summary>
        // ------------------------------------------------------------
        public void UnbindState()
        {
            state = null;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 연결된 지속 상태를 게임 동작으로 변경한다.
        /// </summary>
        // ------------------------------------------------------------
        public void Change(int value)
        {
            if (state == null)
            {
                throw new InvalidOperationException("상태가 연결되지 않았습니다.");
            }

            LiveChangeCount++;
            state.Value = value;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 확정 State가 이 객체에 적용 가능한지 변경 없이 검사한다.
        /// </summary>
        // ------------------------------------------------------------
        public void ValidateCurrentState(WorldObjectState next)
        {
            if (state == null || next is not TestSceneWorldState || next.ID != ID)
            {
                throw new ArgumentException("현재 State 타입 또는 ID가 일치하지 않습니다.", nameof(next));
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Live 변경 효과를 다시 실행하지 않고 현재 State를 교체한다.
        /// </summary>
        // ------------------------------------------------------------
        public void ApplyCurrentState(WorldObjectState next)
        {
            state = (TestSceneWorldState)next;
            CurrentApplyCount++;

            if (FailCurrentApply)
            {
                throw new InvalidOperationException("현재 State 부분 적용 실패");
            }
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// Scene 수명과 분리돼 저장 가능한 테스트 상태.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class TestSceneWorldState : WorldObjectState
    {

    #region 값

        // ------------------------------------------------------------
        /// <summary>
        /// 게임이 변경하는 지속 값.
        /// </summary>
        // ------------------------------------------------------------
        public int Value
        {
            get => value;
            set => this.value = value;
        }

        [SerializeField]
        private int value = 0;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 지정한 ID와 초기값으로 상태를 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public TestSceneWorldState(string id, int value) : base(id)
        {
            this.value = value;
        }

    #endregion

    }
}
