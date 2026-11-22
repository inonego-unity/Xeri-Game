/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : ValidationOverlay.cs
수정일 : 2026-11-22

# 설명
검증 씬 화면(IMGUI)에 합성 결과, 효과 표, 자동 점검 결과, 입력 결과 로그를 그리는 도구.

# 특이사항, 제약사항
프리팹이나 UI 자산 없이 OnGUI만 쓴다. 호출자가 GUILayout 영역(BeginArea) 안에서 부른다.
값 표시는 문자열 서식이라 매 프레임 할당하지만 검증 씬 전용이다.
========================================================================= BLOCK_HEADER_END */

using System.Collections.Generic;
using System.Text;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;

namespace inonego.Xeri.Game.Validation.Locomotion
{
    // ============================================================
    /// <summary>
    /// 검증 씬 화면 그리기 도구.
    /// </summary>
    // ============================================================
    internal static class ValidationOverlay
    {

    #region 스타일

        private static GUIStyle label;
        private static GUIStyle title;
        private static GUIStyle centered;

        public static readonly Color PassColor = new Color(0.45f, 1f, 0.5f);
        public static readonly Color FailColor = new Color(1f, 0.4f, 0.4f);
        public static readonly Color DimColor = new Color(0.65f, 0.65f, 0.7f);

        // ------------------------------------------------------------
        /// <summary>
        /// 라벨·제목 스타일을 한 번 만든다.
        /// </summary>
        // ------------------------------------------------------------
        public static void EnsureStyles()
        {
            if (label != null)
            {
                return;
            }

            label = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 12, wordWrap = true };
            label.margin = new RectOffset(2, 2, 0, 0);
            label.padding = new RectOffset(2, 2, 0, 0);

            title = new GUIStyle(label) { fontSize = 14, fontStyle = FontStyle.Bold };
            centered = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
        }

        public static string Hex(Color color) => ColorUtility.ToHtmlStringRGB(color);

        public static string Paint(string text, Color color) => $"<color=#{Hex(color)}>{text}</color>";

        public static string V(Vector3 value) => $"({value.x:F2}, {value.y:F2}, {value.z:F2})";

    #endregion

    #region 기본 요소

        // ------------------------------------------------------------
        /// <summary>
        /// 제목 한 줄을 그린다.
        /// </summary>
        // ------------------------------------------------------------
        public static void Title(string text)
        {
            GUILayout.Label(text, title);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 본문 한 줄을 그린다.
        /// </summary>
        // ------------------------------------------------------------
        public static void Line(string text)
        {
            GUILayout.Label(text, label);
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 색을 입힌 본문 한 줄을 그린다.
        /// </summary>
        // ------------------------------------------------------------
        public static void Line(string text, Color color)
        {
            GUILayout.Label(Paint(text, color), label);
        }

    #endregion

    #region 합성 결과

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막 Step의 합성 결과와 Body 상태를 그린다.
        /// </summary>
        // ------------------------------------------------------------
        public static void DrawFrame(ValidationActor actor)
        {
            Title("합성 결과 (MotionFrame)");

            if (!actor.HasFrame)
            {
                Line("아직 Step 전", DimColor);

                return;
            }

            var frame = actor.LastFrame;

            Line($"Tick {frame.Tick.Index}  StepVersion {frame.StepVersion}  적용 {(actor.LastApplied ? "성공" : Paint("실패", FailColor))}  Valid {frame.IsValid}");
            Line($"Planar    {Mode(frame.PlanarMode)}  {V(frame.Planar)}");
            Line($"Elevation {Mode(frame.ElevationMode)}  {frame.Elevation:F2}");
            Line($"Rotation  {Mode(frame.RotationMode)}  {V(frame.Rotation)}");
            Line($"Impulse(Free 증분) {V(frame.Impulse)}");
            Line($"ActionID {frame.ActionID}  시작 Tick {frame.ActionStartTick}  Up {V(frame.Up)}");
            Line($"Body 위치 {V(actor.Body.position)}");
            Line($"Body 속도 {V(actor.Body.linearVelocity)}  |v| {actor.Body.linearVelocity.magnitude:F2}");
        }

        private static string Mode(MotionChannelMode mode) =>
            mode == MotionChannelMode.Set ? Paint("Set ", new Color(1f, 0.85f, 0.3f)) : Paint("Free", new Color(0.5f, 0.85f, 1f));

    #endregion

    #region 효과 표

        // ------------------------------------------------------------
        /// <summary>
        /// 카드 효과의 활성·보임·남은 시간을 표로 그린다.
        /// </summary>
        // ------------------------------------------------------------
        public static void DrawEffects(IReadOnlyList<ValidationEffect> effects, ValidationActor actor)
        {
            Title("효과 (이름 · Order · 연산 · 상태)");

            var any = false;

            for (var index = 0; index < effects.Count; index++)
            {
                var effect = effects[index];

                if (!effect.IsActive)
                {
                    continue;
                }

                any = true;

                var visible = !actor.Model.TryGetModifier(effect.Source, out var state) || state.IsVisible;
                var remaining = effect.Duration > 0f ? $"  남은 {effect.Timer.RemainingTime:F2}s{(effect.Timer.IsPaused ? " (정지)" : string.Empty)}" : string.Empty;
                var hidden = visible ? string.Empty : Paint("  가려짐", FailColor);

                Line($"{Paint("■", effect.Color)} {effect.Name}  o{effect.Order}  {effect.Operation}{remaining}{hidden}");
            }

            if (!any)
            {
                Line("활성 효과 없음", DimColor);
            }
        }

    #endregion

    #region 점검과 로그

        // ------------------------------------------------------------
        /// <summary>
        /// 자동 점검 항목과 누적 결과를 그린다.
        /// </summary>
        // ------------------------------------------------------------
        public static void DrawChecks(IReadOnlyList<ValidationCheck> checks)
        {
            Title("자동 점검");

            if (checks.Count == 0)
            {
                Line("이 카드는 눈으로 확인합니다", DimColor);

                return;
            }

            for (var index = 0; index < checks.Count; index++)
            {
                var check = checks[index];
                var state = check.State == ValidationCheckState.Pass ? Paint("PASS", PassColor)
                    : check.State == ValidationCheckState.Fail ? Paint("FAIL", FailColor)
                    : Paint("----", DimColor);
                var counts = check.FailCount > 0
                    ? $"  {Paint($"실패 {check.FailCount}", FailColor)} / 통과 {check.PassCount}"
                    : $"  통과 {check.PassCount}";

                Line($"{state} {check.Name}{counts}");

                if (check.State == ValidationCheckState.Fail && !string.IsNullOrEmpty(check.Detail))
                {
                    Line($"     {check.Detail}", FailColor);
                }
                else if (check.FailCount > 0 && !string.IsNullOrEmpty(check.LastFailure))
                {
                    Line($"     마지막 실패: {check.LastFailure}", DimColor);
                }
            }
        }

        // ------------------------------------------------------------
        /// <summary>
        /// 입력별 결과 기록을 그린다.
        /// </summary>
        // ------------------------------------------------------------
        public static void DrawLog(List<string> log)
        {
            Title("입력 결과 기록 (Updated 제외)");

            if (log.Count == 0)
            {
                Line("기록 없음", DimColor);

                return;
            }

            var builder = new StringBuilder();

            for (var index = Mathf.Max(0, log.Count - 8); index < log.Count; index++)
            {
                builder.AppendLine(log[index]);
            }

            Line(builder.ToString().TrimEnd());
        }

    #endregion

    #region 월드 라벨

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> Actor 위에 활성 효과를 색 막대와 이름으로 띄운다.
        /// <br/> scale은 OnGUI에서 적용한 화면 배율이다.
        /// </summary>
        // ------------------------------------------------------------
        public static void DrawWorldLabels(Camera camera, ValidationActor actor, IReadOnlyList<ValidationEffect> effects, float scale)
        {
            if (camera == null)
            {
                return;
            }

            var screen = camera.WorldToScreenPoint(actor.Body.position + Vector3.up * 1.4f);

            if (screen.z <= 0f)
            {
                return;
            }

            // GUI 행렬이 화면 배율만큼 확대돼 있으므로 화면 좌표를 같은 배율로 나눈다.
            var x = screen.x / scale;
            var y = (Screen.height - screen.y) / scale;
            var row = 0;

            GUI.Label(new Rect(x - 100f, y - 18f, 200f, 18f), Paint(actor.Name, Color.white), centered);

            for (var index = 0; index < effects.Count; index++)
            {
                var effect = effects[index];

                if (!effect.IsActive)
                {
                    continue;
                }

                row++;

                GUI.Label(new Rect(x - 100f, y - 18f - row * 15f, 200f, 16f), Paint($"■ {effect.Name} o{effect.Order}", effect.Color), centered);
            }
        }

    #endregion

    }
}
