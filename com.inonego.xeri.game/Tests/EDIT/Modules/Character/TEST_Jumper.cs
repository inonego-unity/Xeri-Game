/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : TEST_Jumper.cs
수정일 : 2026-11-23

# 설명
Jumper의 점프 횟수 복원과 OnJump 알림 실패 뒤의 상태 확정을 검증한다.

# 테스트 구성
 N: OnJump 알림 실패 경계
========================================================================= BLOCK_HEADER_END */

using System;
using System.Collections;
using System.Collections.Generic;

using NUnit;
using NUnit.Framework;

namespace inonego.Xeri.Game.TEST._Jumper
{
    using Controller;

    // ============================================================
    /// <summary>
    /// Jumper의 알림 실패 경계 테스트 묶음.
    /// </summary>
    // ============================================================
    public class TEST_Jumper
    {

    #region N-1: OnJump 알림 실패 경계

        // ------------------------------------------------------------
        /// <summary>
        /// <br/> OnJump 구독자가 실패해도 그 Tick의 접지 기록이 확정되어
        /// <br/> 다음 Tick이 점프 횟수를 되돌리지 않는지 검증한다.
        /// </summary>
        // ------------------------------------------------------------
        [Test]
        public void TEST_Jumper_OnJump구독자예외뒤에도_접지기록을_확정()
        {
            var jumper = new Jumper();
            jumper.MaxCount = 1;
            jumper.Init();

            EventHandler<JumpEventArgs> failing = (sender, e) => throw new InvalidOperationException("OnJump failure");
            jumper.OnJump += failing;
            jumper.Trigger();

            // 공중에서 접지로 바뀐 Tick에 버퍼된 점프가 실행되고 구독자가 실패한다.
            Assert.Throws<InvalidOperationException>(() => jumper.FixedTick(0.02f, true));
            Assert.AreEqual(0, jumper.Count);

            jumper.OnJump -= failing;
            jumper.FixedTick(0.02f, true);

            // 같은 접지 상태가 이어지는 Tick은 접지 전환이 아니므로 횟수를 복원하지 않는다.
            Assert.AreEqual(0, jumper.Count);
        }

    #endregion

    }
}
