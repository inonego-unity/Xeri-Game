/* BLOCK_HEADER_BEGIN =======================================================================
파일명 : AssemblyInfo.cs
수정일 : 2026-11-12

# 설명
Xeri Game 런타임의 internal 계약을 해당 패키지의 검증 어셈블리에만 공개한다.
========================================================================= BLOCK_HEADER_END */

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("inonego.Xeri.Game.TEST.EDIT")]
[assembly: InternalsVisibleTo("inonego.Xeri.Game.TEST.PLAY")]
