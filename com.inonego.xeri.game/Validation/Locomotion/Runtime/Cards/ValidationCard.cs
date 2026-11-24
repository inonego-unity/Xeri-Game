/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ValidationCard.cs
수정일 : 2026-11-22

# 설명
검증 카드의 공통 기반. 카드 하나는 자기 구역(바닥, 도형)과 Actor, 효과, 자동 점검을 갖고
씬이 한 번에 하나만 활성화해 개별로 확인한다.

# 특이사항, 제약사항
Tick 순서는 BeforeTick(입력 수집) → Actor Prepare → Physics → Actor Complete → AfterTick(점검)이다.
카드가 비활성이면 Root가 꺼져 Rigidbody도 Physics.Simulate에 참여하지 않는다.
숫자 키 1~9와 WASD·Space는 카드가 쓰고, P·N·R·Tab은 씬이 쓴다.
========================================================================= BLOCK_HEADER_END */

using System.Collections.Generic;

using UnityEngine;

namespace inonego.Xeri.Game.Validation.Locomotion
{
    // ============================================================
    /// <summary>
    /// 검증 카드 기반 클래스.
    /// </summary>
    // ============================================================
    internal abstract class ValidationCard
    {

    #region 필드

        protected readonly List<ValidationActor> actors = new List<ValidationActor>();
        protected readonly List<ValidationEffect> effects = new List<ValidationEffect>();

        public List<ValidationCheck> Checks { get; } = new List<ValidationCheck>();
        public IReadOnlyList<ValidationActor> Actors => actors;
        public IReadOnlyList<ValidationEffect> Effects => effects;
        public ValidationActor Primary => actors.Count > 0 ? actors[0] : null;

        public GameObject Root { get; private set; }
        public Vector3 Origin { get; private set; }

        // 이 카드가 무엇을 확인하는지, 기대 결과가 무엇인지, 조작 키가 무엇인지.
        public abstract string Title { get; }
        public abstract string Summary { get; }
        public abstract string Expected { get; }
        public abstract string[] Controls { get; }

        // 카메라가 카드 중심에서 떨어지는 위치.
        public virtual Vector3 CameraOffset => new Vector3(0f, 9f, -12f);

    #endregion

    #region 구성

        // ------------------------------------------------------------
        /// <summary>
        /// 카드 구역을 만들고 초기 상태로 맞춘다.
        /// </summary>
        // ------------------------------------------------------------
        public void Build(Transform parent, Vector3 origin)
        {
            Origin = origin;
            Root = new GameObject(Title);
            Root.transform.SetParent(parent, false);
            Root.transform.position = origin;

            BuildContent();
            ResetCard();
        }

        protected abstract void BuildContent();

        // 카드 안의 바닥을 만든다. 윗면이 y=0이다.
        protected GameObject CreateGround(Vector3 size)
        {
            return ValidationFactory.CreateBox("Ground", Root.transform, new Vector3(0f, -0.5f, 0f), new Vector3(size.x, 1f, size.z), new Color(0.32f, 0.34f, 0.38f));
        }

        // 카드 구역 안의 월드 위치.
        protected Vector3 At(float x, float y, float z) => Origin + new Vector3(x, y, z);

    #endregion

    #region 초기화

        // ------------------------------------------------------------
        /// <summary>
        /// 효과·Actor·점검 횟수를 처음 상태로 되돌린다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void ResetCard()
        {
            for (var index = 0; index < effects.Count; index++)
            {
                effects[index].MarkInactive();
            }

            for (var index = 0; index < Checks.Count; index++)
            {
                Checks[index].ResetCounts();
            }
        }

    #endregion

    #region Tick

        // ------------------------------------------------------------
        /// <summary>
        /// 프레임마다 키 입력을 읽는다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void HandleInput()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Step 전에 호출한다. 효과를 갱신하고 이번 Tick의 입력을 버퍼에 넣는다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void BeforeTick(float dt)
        {
            TickEffects(dt);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// Physics Step이 끝난 뒤 호출한다. 카드 점검과 자동 판정을 한다.
        /// </summary>
        // ------------------------------------------------------------
        public void AfterTick(float dt)
        {
            OnAfterTick(dt);

            for (var index = 0; index < effects.Count; index++)
            {
                effects[index].MarkStepped();
            }

            var primary = Primary;

            if (primary == null || !primary.HasFrame)
            {
                return;
            }

            var tick = primary.LastFrame.Tick.Index;

            for (var index = 0; index < Checks.Count; index++)
            {
                Checks[index].Evaluate(tick);
            }
        }

        protected virtual void OnAfterTick(float dt)
        {
            // NONE
        }

        protected void TickEffects(float dt)
        {
            var primary = Primary;

            for (var index = 0; index < effects.Count; index++)
            {
                effects[index].Tick(primary, dt);
            }
        }

    #endregion

    #region 화면

        // ------------------------------------------------------------
        /// <summary>
        /// 카드 전용 버튼을 그린다. 왼쪽 조작 영역 안에서 호출한다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void DrawControls()
        {
            // NONE
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 카드 전용 실시간 값을 오른쪽 패널에 그린다.
        /// </summary>
        // ------------------------------------------------------------
        public virtual void DrawPanel()
        {
            // NONE
        }

    #endregion

    #region 효과 도우미

        // 켜져 있으면 끄고 꺼져 있으면 켠다.
        protected void Toggle(ValidationEffect effect)
        {
            if (effect.IsActive)
            {
                effect.Stop(Primary, false);
            }
            else
            {
                effect.Start(Primary);
            }
        }

        protected ValidationEffect AddEffect(ValidationEffect effect)
        {
            effects.Add(effect);

            return effect;
        }

        // 효과 버튼 한 줄. 눌리면 true.
        protected bool EffectButton(string key, ValidationEffect effect)
        {
            var label = $"[{key}] {effect.Name}  {(effect.IsActive ? "ON" : "off")}";

            return GUILayout.Button(label);
        }

    #endregion

    }
}
