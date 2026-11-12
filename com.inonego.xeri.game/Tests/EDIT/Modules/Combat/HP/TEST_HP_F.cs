/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_HP_F.cs
수정일 : 2026-11-12

# 설명
HP_F (float 기반 체력) 유닛 테스트.
Unity Test Runner (Edit Mode) 에서 실행한다.

# 테스트 구성
 E: 기본 기능 (생성/상태 전환/힐·데미지/MaxValue/Ratio)
 V: 이벤트 (OnValueChange/OnMaxValueChange/OnStateChange/OnHeal/OnDamage)
 U: 유틸리티 (CalculateApplyAmount)
========================================================================= BLOCK_HEADER_END */

using NUnit;
using NUnit.Framework;

using inonego.Xeri.Game;

namespace inonego.Xeri.Game.TEST._HP
{

    // ============================================================
    /// <summary>
    /// HP_F (float 기반 체력) 핵심 기능 테스트.
    /// </summary>
    // ============================================================
    public class TEST_HP_F
    {

    #region E-1: 기본 생성

        [Test]
        public void TEST_HP_F_기본_생성_초기값()
        {
            var hp = new HP_F();

            Assert.AreEqual(0f, hp.Value);
            Assert.AreEqual(0f, hp.MaxValue);

            Assert.AreEqual(0.0f, hp.Ratio);

            Assert.AreEqual(HPState.Dead, hp.Current);
            Assert.IsFalse(hp.IsAlive);
            Assert.IsTrue(hp.IsDead);
        }

    #endregion

    #region E-2: 상태 변경

        [Test]
        public void TEST_HP_F_MakeAlive_MakeDead_Value_자동_상태_전환()
        {
            var hp = new HP_F();
            hp.MaxValue = 100f;

            hp.MakeAlive();

            Assert.AreEqual(HPState.Alive, hp.Current);
            Assert.AreEqual(100f, hp.Value);
            Assert.AreEqual(100f, hp.MaxValue);
            Assert.IsTrue(hp.IsAlive);
            Assert.IsFalse(hp.IsDead);

            hp.MakeDead();

            Assert.AreEqual(HPState.Dead, hp.Current);
            Assert.AreEqual(0f, hp.Value);
            Assert.IsFalse(hp.IsAlive);
            Assert.IsTrue(hp.IsDead);

            hp.Value = 50f;

            Assert.AreEqual(HPState.Alive, hp.Current);
            Assert.AreEqual(50f, hp.Value);
            Assert.IsTrue(hp.IsAlive);

            hp.Value = 0f;

            Assert.AreEqual(HPState.Dead, hp.Current);
            Assert.AreEqual(0f, hp.Value);
            Assert.IsTrue(hp.IsDead);
        }

    #endregion

    #region E-3: 힐과 데미지

        [Test]
        public void TEST_HP_F_ApplyHeal_ApplyDamage_사망후_무시()
        {
            var hp = new HP_F();
            hp.MaxValue = 100f;
            hp.MakeAlive();
            hp.Value = 50f;

            hp.ApplyHeal(30f);

            Assert.AreEqual(80f, hp.Value);
            Assert.AreEqual(0.8f, hp.Ratio);

            hp.ApplyHeal(50f);

            Assert.AreEqual(100f, hp.Value);
            Assert.AreEqual(1.0f, hp.Ratio);

            hp.ApplyDamage(40f);

            Assert.AreEqual(60f, hp.Value);
            Assert.AreEqual(0.6f, hp.Ratio);

            hp.ApplyDamage(100f);

            Assert.AreEqual(0f, hp.Value);
            Assert.AreEqual(HPState.Dead, hp.Current);
            Assert.IsTrue(hp.IsDead);

            hp.ApplyHeal(50f);

            Assert.AreEqual(0f, hp.Value);
            Assert.IsTrue(hp.IsDead);

            hp.ApplyDamage(30f);

            Assert.AreEqual(0f, hp.Value);
            Assert.IsTrue(hp.IsDead);
        }

    #endregion

    #region E-4: 최대 체력 변경

        [Test]
        public void TEST_HP_F_MaxValue_변경_클램핑_및_사망_전환()
        {
            var hp = new HP_F();
            hp.MaxValue = 100f;
            hp.MakeAlive();
            hp.Value = 75f;

            hp.MaxValue = 150f;

            Assert.AreEqual(150f, hp.MaxValue);
            Assert.AreEqual(75f,  hp.Value);
            Assert.AreEqual(0.5f, hp.Ratio);

            hp.MaxValue = 50f;

            Assert.AreEqual(50f,  hp.MaxValue);
            Assert.AreEqual(50f,  hp.Value);
            Assert.AreEqual(1.0f, hp.Ratio);

            hp.MaxValue = 0f;

            Assert.AreEqual(0f,   hp.MaxValue);
            Assert.AreEqual(0f,   hp.Value);
            Assert.AreEqual(0.0f, hp.Ratio);
            Assert.IsTrue(hp.IsDead);
        }

    #endregion

    #region E-5: Ratio 계산

        [Test]
        public void TEST_HP_F_Ratio_다양한_값_및_MaxValue_0()
        {
            var hp = new HP_F();
            hp.MaxValue = 100f;
            hp.MakeAlive();

            hp.Value = 0f;
            Assert.AreEqual(0.0f, hp.Ratio);

            hp.Value = 25f;
            Assert.AreEqual(0.25f, hp.Ratio);

            hp.Value = 50f;
            Assert.AreEqual(0.5f, hp.Ratio);

            hp.Value = 75f;
            Assert.AreEqual(0.75f, hp.Ratio);

            hp.Value = 100f;
            Assert.AreEqual(1.0f, hp.Ratio);

            hp.MaxValue = 0f;
            Assert.AreEqual(0.0f, hp.Ratio);
        }

    #endregion

    #region V-1: 이벤트 발생

        [Test]
        public void TEST_HP_F_이벤트_통합()
        {
            var hp = new HP_F();

            bool valueChangeFired    = false;
            bool maxValueChangeFired = false;
            bool stateChangeFired    = false;
            bool healFired           = false;
            bool damageFired         = false;

            HP_F valueChangeSender    = null;
            HP_F maxValueChangeSender = null;
            HP_F stateChangeSender    = null;
            HP_F healSender           = null;
            HP_F damageSender         = null;

            ValueChangeEventArgs<float>   valueChangeArgs = default;
            ValueChangeEventArgs<float>   maxValueArgs    = default;
            ValueChangeEventArgs<HPState> stateChangeArgs = default;
            HPApplyEventArgs<float>       healArgs        = default;
            HPApplyEventArgs<float>       damageArgs      = default;

            void Reset()
            {
                valueChangeFired = maxValueChangeFired = stateChangeFired = healFired = damageFired = false;

                valueChangeSender = maxValueChangeSender = stateChangeSender = healSender = damageSender = null;

                valueChangeArgs = maxValueArgs = default;
                stateChangeArgs = default;
                healArgs = damageArgs = default;
            }

            hp.OnValueChange    += (sender, e) => { valueChangeFired    = true; valueChangeSender    = sender as HP_F; valueChangeArgs = e; };
            hp.OnMaxValueChange += (sender, e) => { maxValueChangeFired = true; maxValueChangeSender = sender as HP_F; maxValueArgs    = e; };
            hp.OnStateChange    += (sender, e) => { stateChangeFired    = true; stateChangeSender    = sender as HP_F; stateChangeArgs = e; };
            hp.OnHeal           += (sender, e) => { healFired           = true; healSender           = sender as HP_F; healArgs        = e; };
            hp.OnDamage         += (sender, e) => { damageFired         = true; damageSender         = sender as HP_F; damageArgs      = e; };

            // OnMaxValueChange
            hp.MaxValue = 100f;

            Assert.IsTrue(maxValueChangeFired);
            Assert.AreEqual(hp,   maxValueChangeSender);
            Assert.AreEqual(0f,   maxValueArgs.Previous);
            Assert.AreEqual(100f, maxValueArgs.Current);

            Reset();

            // OnStateChange
            hp.MakeAlive();

            Assert.IsTrue(stateChangeFired);
            Assert.AreEqual(hp,            stateChangeSender);
            Assert.AreEqual(HPState.Dead,  stateChangeArgs.Previous);
            Assert.AreEqual(HPState.Alive, stateChangeArgs.Current);

            Reset();

            // OnValueChange
            hp.Value = 50f;

            Assert.IsTrue(valueChangeFired);
            Assert.AreEqual(hp,   valueChangeSender);
            Assert.AreEqual(100f, valueChangeArgs.Previous);
            Assert.AreEqual(50f,  valueChangeArgs.Current);

            Reset();

            // OnHeal
            hp.ApplyHeal(30f);

            Assert.IsTrue(healFired);
            Assert.AreEqual(hp,  healSender);
            Assert.AreEqual(30f, healArgs.Amount);

            Reset();

            // OnDamage
            hp.ApplyDamage(20f);

            Assert.IsTrue(damageFired);
            Assert.AreEqual(hp,  damageSender);
            Assert.AreEqual(20f, damageArgs.Amount);
        }

    #endregion

    #region U-1: CalculateApplyAmount

        [Test]
        public void TEST_HP_F_CalculateApplyAmount_비율_타입별()
        {
            var hp = new HP_F();
            hp.MaxValue = 100f;
            hp.MakeAlive();
            hp.Value = 60f;

            float amount1 = hp.CalculateApplyAmount(0.5f, HPApplyRatioType.ByValue);

            Assert.AreEqual(30f, amount1);

            float amount2 = hp.CalculateApplyAmount(0.5f, HPApplyRatioType.ByMaxValue);

            Assert.AreEqual(50f, amount2);

            float amount3 = hp.CalculateApplyAmount(0.5f, HPApplyRatioType.ByMissingValue);

            Assert.AreEqual(20f, amount3);
        }

    #endregion

    }

}
