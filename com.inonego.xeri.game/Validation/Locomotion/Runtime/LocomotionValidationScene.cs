/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : LocomotionValidationScene.cs
수정일 : 2026-11-22

# 설명
Locomotion(Motion·Motor·Movement) 통합 검증 씬의 진입점. 단순 큐브와 바닥으로 카드별 구역을 코드로 만들고
한 번에 카드 하나만 활성화해 개별로 확인하게 한다. 화면(OnGUI)에 카드 목록, 조작, 합성 결과, 자동 점검을 보여준다.

# 특이사항, 제약사항
Physics를 Script 모드로 바꿔 고정 Tick(기본 0.02초)마다 Motion 합성 → Motor 적용 → Physics.Simulate 순서를 직접 돌린다.
종료할 때 원래 Physics 모드로 되돌린다. Input System만 쓴다.
키: Tab/PageUp·PageDown 카드 전환, P 일시정지, N 한 Tick, R 카드 초기화, [ ] 속도.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

namespace inonego.Xeri.Game.Validation.Locomotion
{
    // ============================================================
    /// <summary>
    /// Locomotion 검증 씬을 코드로 구성하고 Tick을 돌리는 진입 컴포넌트.
    /// </summary>
    // ============================================================
    public sealed class LocomotionValidationScene : MonoBehaviour
    {

    #region 필드

        // ------------------------------------------------------------
        /// <summary>
        /// 시뮬레이션 Tick 간격(초).
        /// </summary>
        // ------------------------------------------------------------
        [SerializeField]
        private float tickDelta = 0.02f;

        private const float CARD_SPACING = 80f;
        private const int MAX_TICKS_PER_FRAME = 8;

        private static readonly float[] speeds = { 0.1f, 0.25f, 0.5f, 1f, 2f };

        private readonly List<ValidationCard> cards = new List<ValidationCard>();

        private SimulationMode previousMode;
        private Camera viewCamera;
        private int activeIndex = -1;
        private int speedIndex = 3;
        private float accumulator;
        private bool paused;
        private bool stepOnce;
        private string error;
        private Vector2 controlScroll;
        private Vector2 panelScroll;

    #endregion

    #region 수명

        private void Awake()
        {
            // Physics는 Tick 안에서 한 번씩만 직접 실행한다.
            previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;

            EnsureCamera();
            BuildCards();
            Select(0);
        }

        private void OnDestroy()
        {
            Physics.simulationMode = previousMode;
        }

        private void EnsureCamera()
        {
            viewCamera = Camera.main;

            if (viewCamera == null)
            {
                var cameraObject = new GameObject("Validation Camera") { tag = "MainCamera" };

                viewCamera = cameraObject.AddComponent<Camera>();
            }

            viewCamera.fieldOfView = 50f;
            viewCamera.nearClipPlane = 0.1f;
            viewCamera.farClipPlane = 400f;
        }

        private void BuildCards()
        {
            var root = new GameObject("Locomotion Validation Cards").transform;

            root.SetParent(transform, false);

            cards.Add(new CompositionCard());
            cards.Add(new FreeChannelCard());
            cards.Add(new PlatformCard());
            cards.Add(new RollbackCard());
            cards.Add(new OperationCard());
            cards.Add(new InputRuleCard());
            cards.Add(new LeaseCard());

            for (var index = 0; index < cards.Count; index++)
            {
                cards[index].Build(root, new Vector3(index * CARD_SPACING, 0f, 0f));
                cards[index].Root.SetActive(false);
            }
        }

    #endregion

    #region 카드 전환

        private void Select(int index)
        {
            if (cards.Count == 0)
            {
                return;
            }

            index = (index % cards.Count + cards.Count) % cards.Count;

            if (activeIndex >= 0)
            {
                cards[activeIndex].Root.SetActive(false);
            }

            activeIndex = index;

            var card = cards[activeIndex];

            card.Root.SetActive(true);
            card.ResetCard();
            error = null;
            accumulator = 0f;

            SnapCamera();
        }

        private ValidationCard Active => activeIndex >= 0 ? cards[activeIndex] : null;

    #endregion

    #region 업데이트

        private void Update()
        {
            HandleGlobalInput();

            var card = Active;

            if (card == null)
            {
                return;
            }

            card.HandleInput();

            if (paused && !stepOnce)
            {
                return;
            }

            accumulator += stepOnce ? tickDelta : Time.deltaTime * speeds[speedIndex];
            stepOnce = false;

            var guard = 0;

            while (accumulator >= tickDelta && guard++ < MAX_TICKS_PER_FRAME)
            {
                accumulator -= tickDelta;

                RunTick(card);
            }

            if (guard >= MAX_TICKS_PER_FRAME)
            {
                accumulator = 0f;
            }
        }

        private void HandleGlobalInput()
        {
            if (ValidationInput.Pressed(Key.Tab) || ValidationInput.Pressed(Key.PageDown))
            {
                Select(activeIndex + 1);
            }

            if (ValidationInput.Pressed(Key.PageUp))
            {
                Select(activeIndex - 1);
            }

            if (ValidationInput.Pressed(Key.P))
            {
                paused = !paused;
            }

            if (ValidationInput.Pressed(Key.N))
            {
                paused = true;
                stepOnce = true;
            }

            if (ValidationInput.Pressed(Key.R))
            {
                ResetActive();
            }

            if (ValidationInput.Pressed(Key.RightBracket))
            {
                speedIndex = Mathf.Min(speeds.Length - 1, speedIndex + 1);
            }

            if (ValidationInput.Pressed(Key.LeftBracket))
            {
                speedIndex = Mathf.Max(0, speedIndex - 1);
            }
        }

        private void ResetActive()
        {
            Active?.ResetCard();
            error = null;
            accumulator = 0f;
        }

        private void RunTick(ValidationCard card)
        {
            try
            {
                var dt = tickDelta;

                card.BeforeTick(dt);

                for (var index = 0; index < card.Actors.Count; index++)
                {
                    card.Actors[index].Prepare(dt);
                }

                Physics.Simulate(dt);

                for (var index = 0; index < card.Actors.Count; index++)
                {
                    card.Actors[index].Complete();
                }

                card.AfterTick(dt);
            }
            catch (Exception exception)
            {
                // 계약 위반 예외도 화면에 남겨 확인할 수 있게 하고 시뮬레이션은 멈춘다.
                error = exception.ToString();
                paused = true;

                Debug.LogException(exception);
            }
        }

        private void LateUpdate()
        {
            var card = Active;

            if (card == null)
            {
                return;
            }

            for (var index = 0; index < card.Actors.Count; index++)
            {
                card.Actors[index].UpdateVisuals();
            }

            FollowCamera(card, false);
        }

    #endregion

    #region 카메라

        private void SnapCamera()
        {
            if (Active != null)
            {
                FollowCamera(Active, true);
            }
        }

        private void FollowCamera(ValidationCard card, bool snap)
        {
            var focus = card.Primary != null ? card.Primary.Body.position : card.Origin;

            // 카드 중심을 기준으로 하되 큐브가 멀어지면 따라간다.
            var target = card.Origin + (focus - card.Origin) * 0.5f + Vector3.up * 1f;
            var position = target + card.CameraOffset;
            var rotation = Quaternion.LookRotation(target - position, Vector3.up);
            var blend = snap ? 1f : 1f - Mathf.Exp(-6f * Time.deltaTime);

            viewCamera.transform.SetPositionAndRotation
            (
                Vector3.Lerp(viewCamera.transform.position, position, blend),
                Quaternion.Slerp(viewCamera.transform.rotation, rotation, blend)
            );
        }

    #endregion

    #region 화면

        private void OnGUI()
        {
            ValidationOverlay.EnsureStyles();

            var card = Active;

            if (card == null)
            {
                return;
            }

            // 해상도에 따라 글자 크기를 맞춘다. 기준 높이는 720이다.
            var scale = Mathf.Max(1f, Screen.height / 720f);

            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            var width = Screen.width / scale;
            var height = Screen.height / scale;

            for (var index = 0; index < card.Actors.Count; index++)
            {
                ValidationOverlay.DrawWorldLabels(viewCamera, card.Actors[index], card.Effects, scale);
            }

            DrawLeft(card, height);
            DrawTop(card, width);
            DrawRight(card, width, height);
            DrawBottom(card, width, height);
        }

        private void DrawLeft(ValidationCard card, float height)
        {
            GUILayout.BeginArea(new Rect(8f, 8f, 250f, height - 16f), GUI.skin.box);

            ValidationOverlay.Title("Locomotion 검증");

            for (var index = 0; index < cards.Count; index++)
            {
                var previous = GUI.backgroundColor;

                GUI.backgroundColor = index == activeIndex ? new Color(0.4f, 0.8f, 1f) : previous;

                if (GUILayout.Button(cards[index].Title))
                {
                    Select(index);
                }

                GUI.backgroundColor = previous;
            }

            GUILayout.Space(6f);
            ValidationOverlay.Title("시간");

            GUILayout.BeginHorizontal();

            if (GUILayout.Button(paused ? "▶ 재생 (P)" : "❚❚ 정지 (P)"))
            {
                paused = !paused;
            }

            if (GUILayout.Button("한 Tick (N)"))
            {
                paused = true;
                stepOnce = true;
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("◀ 느리게 ( [ )"))
            {
                speedIndex = Mathf.Max(0, speedIndex - 1);
            }

            if (GUILayout.Button("빠르게 ( ] ) ▶"))
            {
                speedIndex = Mathf.Min(speeds.Length - 1, speedIndex + 1);
            }

            GUILayout.EndHorizontal();
            ValidationOverlay.Line($"속도 x{speeds[speedIndex]:0.##}  Tick {tickDelta:0.###}s");

            if (GUILayout.Button("카드 초기화 (R)"))
            {
                ResetActive();
            }

            GUILayout.Space(6f);
            ValidationOverlay.Title("이 카드 조작");

            controlScroll = GUILayout.BeginScrollView(controlScroll);
            card.DrawControls();
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        private void DrawTop(ValidationCard card, float width)
        {
            var areaWidth = Mathf.Min(560f, width - 250f - 340f - 40f);

            GUILayout.BeginArea(new Rect(266f, 8f, areaWidth, 180f), GUI.skin.box);

            ValidationOverlay.Title(card.Title);
            ValidationOverlay.Line(card.Summary);
            ValidationOverlay.Line($"기대: {card.Expected}", new Color(1f, 0.92f, 0.6f));

            GUILayout.Space(4f);

            for (var index = 0; index < card.Controls.Length; index++)
            {
                ValidationOverlay.Line(card.Controls[index], ValidationOverlay.DimColor);
            }

            GUILayout.EndArea();
        }

        private void DrawRight(ValidationCard card, float width, float height)
        {
            GUILayout.BeginArea(new Rect(width - 340f, 8f, 332f, height - 16f), GUI.skin.box);
            panelScroll = GUILayout.BeginScrollView(panelScroll);

            if (!string.IsNullOrEmpty(error))
            {
                ValidationOverlay.Title("예외로 정지");
                ValidationOverlay.Line(error, ValidationOverlay.FailColor);
            }

            if (card.Primary != null)
            {
                ValidationOverlay.DrawFrame(card.Primary);

                GUILayout.Space(4f);

                if (card.Effects.Count > 0)
                {
                    ValidationOverlay.DrawEffects(card.Effects, card.Primary);

                    GUILayout.Space(4f);
                }

                card.DrawPanel();

                GUILayout.Space(4f);
                ValidationOverlay.DrawLog(card.Primary.Log);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawBottom(ValidationCard card, float width, float height)
        {
            var areaWidth = Mathf.Min(560f, width - 250f - 340f - 40f);
            var areaHeight = Mathf.Min(220f, height * 0.38f);

            GUILayout.BeginArea(new Rect(266f, height - areaHeight - 8f, areaWidth, areaHeight), GUI.skin.box);

            ValidationOverlay.DrawChecks(card.Checks);

            if (card.Checks.Count > 0 && GUILayout.Button("점검 횟수 지우기"))
            {
                for (var index = 0; index < card.Checks.Count; index++)
                {
                    card.Checks[index].ResetCounts();
                }
            }

            GUILayout.EndArea();
        }

    #endregion

    }
}
