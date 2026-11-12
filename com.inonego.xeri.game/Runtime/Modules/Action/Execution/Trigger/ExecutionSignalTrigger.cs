/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ExecutionSignalTrigger.cs
수정일 : 2026-10-31

# 설명
첫 Signal 수신과 Offset 뒤의 한 번 발동을 평가하는 조건.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

namespace inonego.Xeri.Game
{
    // ============================================================
    /// <summary>
    /// 특정 Execution Signal의 수신을 조건으로 삼는 Trigger 정의.
    /// </summary>
    // ============================================================
    [Serializable]
    public sealed class ExecutionSignalTriggerDefinition : ExecutionTriggerDefinition
    {

    #region 설정

        // ------------------------------------------------------------
        /// <summary>
        /// 감지 대상 Signal의 키.
        /// </summary>
        // ------------------------------------------------------------
        public string SignalKey => signalKey;

        [SerializeField]
        private string signalKey = string.Empty;

        // ------------------------------------------------------------
        /// <summary>
        /// Signal 수신 후 충족까지의 시간 간격.
        /// </summary>
        // ------------------------------------------------------------
        public float Offset => offset;

        [SerializeField, Min(0f)]
        private float offset = 0f;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// Unity 직렬화용 기본 Signal 조건을 구성한다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionSignalTriggerDefinition()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 감지할 Signal과 지연 시간을 지정한다.
        /// </summary>
        // ------------------------------------------------------------
        public ExecutionSignalTriggerDefinition(string signalKey, float offset = 0f)
        {
            this.signalKey = signalKey;
            this.offset = offset;
            Normalize(float.PositiveInfinity);
        }

    #endregion

    #region 생성·정규화

        // ------------------------------------------------------------
        /// <summary>
        /// 현재 Signal 조건을 가진 실행별 Trigger를 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        public override ExecutionTrigger CreateTrigger() => new ExecutionSignalTrigger(this);

        // ------------------------------------------------------------
        /// <summary>
        /// Signal 키의 공백과 음수 Offset을 보정한다.
        /// </summary>
        // ------------------------------------------------------------
        public override void Normalize(float maximumDuration)
        {
            signalKey = signalKey?.Trim() ?? string.Empty;
            offset = Mathf.Max(0f, offset);
        }

    #endregion

    }

    // ============================================================
    /// <summary>
    /// 등록한 Signal의 수신과 소비 여부를 관리하는 Trigger 실행 상태.
    /// </summary>
    // ============================================================
    internal sealed class ExecutionSignalTrigger : ExecutionTrigger
    {

    #region 실행별 설정·상태

        private readonly string signalKey;
        private readonly float offset;
        private float signalTime = 0f;
        private bool hasSignal = false;
        private bool hasFired = false;

    #endregion

    #region 생성자

        // ------------------------------------------------------------
        /// <summary>
        /// 실행별 Signal 키와 Offset을 복사하여 생성한다.
        /// </summary>
        // ------------------------------------------------------------
        internal ExecutionSignalTrigger(ExecutionSignalTriggerDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            signalKey = definition.SignalKey;
            offset = definition.Offset;
        }

    #endregion

    #region 신호 수신과 평가

        // ------------------------------------------------------------
        /// <summary>
        /// 이전 실행의 Signal 수신 정보를 초기화한다.
        /// </summary>
        // ------------------------------------------------------------
        public override void Begin()
        {
            signalTime = 0f;
            hasSignal = false;
            hasFired = false;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 관찰 대상 Signal 수신 여부를 실행 상태에 기록한다.
        /// </summary>
        // ------------------------------------------------------------
        public override void Signal(in ExecutionSignal signal, float time)
        {
            if
            (
                hasSignal ||
                !signal.IsValid ||
                !string.Equals(signal.Key, signalKey, StringComparison.Ordinal)
            )
            {
                return;
            }

            signalTime = Mathf.Max(0f, time);
            hasSignal = true;
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Signal 수신과 소비 조건을 검사해 발생 여부를 반환한다.
        /// </summary>
        // ------------------------------------------------------------
        public override bool TryEvaluate
        (
            float previousTime,
            float currentTime,
            out float effectiveTime
        )
        {
            effectiveTime = hasSignal ? signalTime + offset : 0f;

            if (hasFired || !hasSignal || !(previousTime <= currentTime) || !(currentTime >= effectiveTime))
            {
                return false;
            }

            hasFired = true;
            return true;
        }

    #endregion

    }
}
