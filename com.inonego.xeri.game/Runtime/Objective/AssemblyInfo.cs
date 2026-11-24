/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AssemblyInfo.cs
수정일 : 2026-11-19

# 설명
Objective 전용 EditMode 테스트에 내부 실행 계약 검증 권한을 제공한다.

# 특이사항, 제약사항
제품 어셈블리의 상태 변경 API는 외부 소비자에 공개하지 않는다.
========================================================================= BLOCK_HEADER_END */

using System;
using System.Runtime;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("inonego.Xeri.Game.TEST.EDIT")]
