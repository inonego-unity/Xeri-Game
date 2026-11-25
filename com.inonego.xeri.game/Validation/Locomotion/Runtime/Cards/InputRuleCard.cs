/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : InputRuleCard.cs
수정일 : 2026-11-22

# 설명
입력 규칙 카드. 세대 규칙, 용량(4), 같은 세대 구조 변경, 비유한 값, 정의되지 않은 연산, 없는 키 종료 같은
검증 규칙을 버튼 하나로 실행해 입력별 결과(MotionInputStatus)가 기대와 같은지 표시한다.

# 특이사항, 제약사항
사례마다 Model을 새로 만들고 명령을 한 Tick에 모두 넣어, 사례끼리 상태가 섞이지 않는다.
결과는 명령 순서대로 기대 상태와 비교한다.
========================================================================= BLOCK_HEADER_END */

using System.Collections.Generic;
using System.Text;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;
using inonego.Xeri.Game.Motion;
using inonego.Xeri.Serializable;

namespace inonego.Xeri.Game.Validation.Locomotion
{
    // ============================================================
    /// <summary>
    /// 입력 검증 규칙과 입력별 결과를 확인하는 카드.
    /// </summary>
    // ============================================================
    internal sealed class InputRuleCard : ValidationCard
    {

    #region 내부 데이터

        private readonly struct Step
        {
            public readonly MotionModifierCommand Command;
            public readonly MotionInputStatus Expected;

            public Step(in MotionModifierCommand command, MotionInputStatus expected)
            {
                Command = command;
                Expected = expected;
            }
        }

        private sealed class Case
        {
            public string Name;
            public string ExpectedText;
            public System.Func<List<Step>> Build;
            public string Result;
            public bool? Passed;
        }

    #endregion

    #region 필드

        private const int CAPACITY = 4;

        private readonly List<Case> cases = new List<Case>();
        private readonly Queue<int> queue = new Queue<int>();

        private List<Step> running;
        private int runningIndex;

        public override string Title => "6. 입력 규칙 (결과 코드)";

        public override string Summary =>
            "세대·용량(4)·구조 변경·NaN·미정의 연산·없는 키 같은 입력을 버튼으로 보내 결과 코드가 기대와 같은지 본다.";

        public override string Expected =>
            "사례마다 입력별 결과가 기대와 같아 PASS가 된다. 예외가 아니라 결과 코드로 돌아오고 나머지 입력은 계속 적용된다.";

        public override string[] Controls => new[]
        {
            "왼쪽 버튼으로 사례 하나씩 실행", "A 버튼으로 전체 실행", "R 카드 초기화"
        };

    #endregion

    #region 구성

        protected override void BuildContent()
        {
            CreateGround(new Vector3(16f, 0f, 16f));

            actors.Add(new ValidationActor("Cube", Root.transform, new Vector3(0f, 0.6f, 0f), new Color(0.6f, 0.8f, 1f), 6, CAPACITY));

            BuildCases();
        }

        private static MotionModifierCommand Apply
        (
            string key, long generation, int order = 0, NumericFOperation operation = NumericFOperation.SET,
            MotionChannel channels = MotionChannel.Planar, float x = 1f
        ) => MotionModifierCommand.Apply
        (
            new MotionSourceKey(key, Revision.FromUInt64((ulong)generation)), order, operation, MotionChannelValues.Linear(channels, new Vector3(x, 0f, 0f))
        );

        private static MotionSourceKey Key(string key, long generation) => new MotionSourceKey(key, Revision.FromUInt64((ulong)generation));

        private void AddCase(string name, string expected, System.Func<List<Step>> build)
        {
            cases.Add(new Case { Name = name, ExpectedText = expected, Build = build });
        }

        private void BuildCases()
        {
            const MotionInputStatus Accepted = MotionInputStatus.Accepted;

            AddCase("새 항목 시작", "Accepted", () => new List<Step>
            {
                new Step(Apply("A", 1), Accepted)
            });
            AddCase("같은 세대 갱신", "Accepted, Updated", () => new List<Step>
            {
                new Step(Apply("A", 1), Accepted), new Step(Apply("A", 1, x: 2f), MotionInputStatus.Updated)
            });
            AddCase("큰 세대 교체", "Accepted, Accepted", () => new List<Step>
            {
                new Step(Apply("A", 1), Accepted), new Step(Apply("A", 2), Accepted)
            });
            AddCase("작은 세대 거절", "Accepted, StaleGeneration", () => new List<Step>
            {
                new Step(Apply("A", 2), Accepted), new Step(Apply("A", 1), MotionInputStatus.StaleGeneration)
            });
            AddCase("같은 세대 채널 변경", "Accepted, InvalidInput", () => new List<Step>
            {
                new Step(Apply("A", 1), Accepted),
                new Step(Apply("A", 1, channels: MotionChannel.Planar | MotionChannel.Elevation), MotionInputStatus.InvalidInput)
            });
            AddCase("같은 세대 Order 변경", "Accepted, InvalidInput", () => new List<Step>
            {
                new Step(Apply("A", 1), Accepted), new Step(Apply("A", 1, order: 5), MotionInputStatus.InvalidInput)
            });
            AddCase("같은 세대 충격 여부 변경", "Accepted, InvalidInput", () => new List<Step>
            {
                new Step(Apply("A", 1), Accepted),
                new Step(MotionModifierCommand.Impulse(Key("A", 1), 0, MotionChannelValues.Linear(MotionChannel.Planar, Vector3.right)), MotionInputStatus.InvalidInput)
            });
            AddCase("켠 채널의 NaN", "InvalidInput", () => new List<Step>
            {
                new Step(MotionModifierCommand.Apply(Key("A", 1), 0, NumericFOperation.SET, MotionChannelValues.Linear(MotionChannel.Planar, new Vector3(float.NaN, 0f, 0f))), MotionInputStatus.InvalidInput)
            });
            AddCase("꺼진 채널의 NaN은 무시", "Accepted", () => new List<Step>
            {
                new Step(MotionModifierCommand.Apply(Key("A", 1), 0, NumericFOperation.SET, new MotionChannelValues(MotionChannel.Planar, Vector3.one, float.NaN)), Accepted)
            });
            AddCase("정의되지 않은 연산(99)", "InvalidInput", () => new List<Step>
            {
                new Step(Apply("A", 1, operation: (NumericFOperation)99), MotionInputStatus.InvalidInput)
            });
            AddCase("없는 키 End", "NotFound", () => new List<Step>
            {
                new Step(MotionModifierCommand.End(Key("A", 1)), MotionInputStatus.NotFound)
            });
            AddCase("다른 세대 End", "Accepted, NotFound, Accepted", () => new List<Step>
            {
                new Step(Apply("A", 2), Accepted), new Step(MotionModifierCommand.End(Key("A", 1)), MotionInputStatus.NotFound),
                new Step(MotionModifierCommand.End(Key("A", 2)), Accepted)
            });
            AddCase("용량 초과 (4)", "Accepted x4, CapacityReached", () => new List<Step>
            {
                new Step(Apply("A", 1), Accepted), new Step(Apply("B", 1), Accepted), new Step(Apply("C", 1), Accepted),
                new Step(Apply("D", 1), Accepted), new Step(Apply("E", 1), MotionInputStatus.CapacityReached)
            });
            AddCase("가득 찬 뒤 같은 키 교체는 허용", "Accepted x4, Accepted", () => new List<Step>
            {
                new Step(Apply("A", 1), Accepted), new Step(Apply("B", 1), Accepted), new Step(Apply("C", 1), Accepted),
                new Step(Apply("D", 1), Accepted), new Step(Apply("A", 2), Accepted)
            });
            AddCase("Clear 뒤 End", "Accepted, Accepted, NotFound", () => new List<Step>
            {
                new Step(Apply("A", 1), Accepted), new Step(MotionModifierCommand.Clear(), Accepted),
                new Step(MotionModifierCommand.End(Key("A", 1)), MotionInputStatus.NotFound)
            });
            AddCase("기본값 명령", "InvalidInput", () => new List<Step>
            {
                new Step(default(MotionModifierCommand), MotionInputStatus.InvalidInput)
            });
            AddCase("충격을 같은 키·세대로 두 번", "Accepted, Updated", () => new List<Step>
            {
                new Step(MotionModifierCommand.Impulse(Key("J", 1), 0, MotionChannelValues.Linear(MotionChannel.Planar, Vector3.right)), Accepted),
                new Step(MotionModifierCommand.Impulse(Key("J", 1), 0, MotionChannelValues.Linear(MotionChannel.Planar, Vector3.left)), MotionInputStatus.Updated)
            });
        }

    #endregion

    #region 초기화

        public override void ResetCard()
        {
            base.ResetCard();

            queue.Clear();
            running = null;

            for (var index = 0; index < cases.Count; index++)
            {
                cases[index].Result = null;
                cases[index].Passed = null;
            }

            if (Primary != null)
            {
                Primary.ResetState(At(0f, 0.6f, 0f));
            }
        }

    #endregion

    #region Tick

        public override void BeforeTick(float dt)
        {
            if (running == null && queue.Count > 0)
            {
                runningIndex = queue.Dequeue();

                // 사례마다 새 Model에서 시작하고 명령을 한 Tick에 모두 넣는다.
                Primary.ResetState(At(0f, 0.6f, 0f));

                running = cases[runningIndex].Build();

                for (var index = 0; index < running.Count; index++)
                {
                    Primary.Buffer.Add(running[index].Command);
                }
            }

            Primary.Buffer.Add(new MotionIntent(MotionChannel.Planar, Vector3.zero));
        }

        protected override void OnAfterTick(float dt)
        {
            if (running == null)
            {
                return;
            }

            var results = Primary.LastResults;
            var current = cases[runningIndex];
            var builder = new StringBuilder();
            var passed = results.Count == running.Count;

            for (var index = 0; index < running.Count && index < results.Count; index++)
            {
                var actual = results[index].Status;

                builder.Append(actual);
                builder.Append(index + 1 < running.Count ? ", " : string.Empty);

                passed &= actual == running[index].Expected;
            }

            current.Result = builder.ToString();
            current.Passed = passed;
            running = null;
        }

    #endregion

    #region 화면

        public override void DrawControls()
        {
            if (GUILayout.Button("[A] 전체 실행"))
            {
                for (var index = 0; index < cases.Count; index++)
                {
                    queue.Enqueue(index);
                }
            }

            for (var index = 0; index < cases.Count; index++)
            {
                var current = cases[index];

                if (GUILayout.Button($"{index + 1}. {current.Name}"))
                {
                    queue.Enqueue(index);
                }

                if (current.Passed.HasValue)
                {
                    var color = current.Passed.Value ? ValidationOverlay.PassColor : ValidationOverlay.FailColor;
                    var label = current.Passed.Value ? "PASS" : "FAIL";

                    ValidationOverlay.Line($"   {label}  결과 {current.Result}", color);

                    if (!current.Passed.Value)
                    {
                        ValidationOverlay.Line($"   기대 {current.ExpectedText}", ValidationOverlay.DimColor);
                    }
                }
                else
                {
                    ValidationOverlay.Line($"   기대 {current.ExpectedText}", ValidationOverlay.DimColor);
                }
            }
        }

        public override void DrawPanel()
        {
            var pass = 0;
            var fail = 0;

            for (var index = 0; index < cases.Count; index++)
            {
                if (cases[index].Passed == true)
                {
                    pass++;
                }
                else if (cases[index].Passed == false)
                {
                    fail++;
                }
            }

            ValidationOverlay.Title("입력 규칙 사례");
            ValidationOverlay.Line($"실행한 사례 {pass + fail}/{cases.Count}  PASS {pass}  FAIL {fail}", fail > 0 ? ValidationOverlay.FailColor : ValidationOverlay.PassColor);
        }

    #endregion

    }
}
