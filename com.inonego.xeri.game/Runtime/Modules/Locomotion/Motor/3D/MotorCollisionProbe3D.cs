/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : MotorCollisionProbe3D.cs
수정일 : 2026-11-21

# 설명
Unity 3D 충돌 콜백으로 전달되는 실제 접촉 지점을 재사용 Buffer에 보관한다.

# 특이사항, 제약사항
Host가 Physics Step 전후에 Motor.Begin/CompletePhysicsStep을 호출해야 한다.
Probe 비활성화 시 이전 접촉을 폐기한다. Physics World Step은 직접 실행하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;

using UnityEngine;

using inonego;
using inonego.Xeri;
using inonego.Xeri.Game;

namespace inonego.Xeri.Game.Motor
{
    // ============================================================
    /// <summary>
    /// <br/> Rigidbody의 실제 3D 접촉을 수집합니다.
    /// <br/> 한 Step에서 최대 32개를 보관합니다.
    /// </summary>
    // ============================================================
    [DisallowMultipleComponent]
    public sealed class MotorCollisionProbe3D : MonoBehaviour
    {

    #region 필드

        private const int MAX_CONTACTS = 32;

        private readonly ContactPoint[] contacts = new ContactPoint[MAX_CONTACTS];
        private readonly ContactPoint[] incoming = new ContactPoint[MAX_CONTACTS];
        internal long ObservationRevision => observationRevision;
        private long observationRevision;

        // ------------------------------------------------------------
        /// <summary>
        /// 마지막 Step의 저장된 접촉 개수입니다.
        /// </summary>
        // ------------------------------------------------------------
        public int ContactCount => contactCount;
        private int contactCount;

        // ------------------------------------------------------------
        /// <summary>
        /// 버퍼 상한으로 일부 접촉이 생략되었는지 표시합니다.
        /// </summary>
        // ------------------------------------------------------------
        public bool Truncated => truncated;
        private bool truncated;

    #endregion

    #region 접촉 보관

        internal void Clear()
        {
            contactCount = 0;
            truncated = false;
        }

        internal int ReadContacts(ContactPoint[] results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            var count = Math.Min(contactCount, results.Length);
            Array.Copy(contacts, results, count);
            return count;
        }

    #endregion

    #region Unity 이벤트

        private void OnDisable()
        {
            // 비활성 구간에는 관측하지 않았으므로 이전 Step의 표본을 무효화한다.
            observationRevision = unchecked(observationRevision + 1);
            Clear();
        }

        private void OnCollisionEnter(Collision collision) => Collect(collision);

        private void OnCollisionStay(Collision collision) => Collect(collision);

        private void Collect(Collision collision)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            var count = collision.GetContacts(incoming);
            var writable = Math.Min(count, MAX_CONTACTS - contactCount);

            for (var index = 0; index < writable; index++)
            {
                contacts[contactCount++] = incoming[index];
            }

            if (count > writable || collision.contactCount > count)
            {
                truncated = true;
            }
        }

    #endregion

    }
}