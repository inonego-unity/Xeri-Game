# Combat

HP의 Alive/Dead 상태, Faction의 관계 판정, Status의 스택·만료는 Entity 등록 수명과 별개입니다.

- [HP](hp.md)
- [Faction](faction.md)
- [Status](status.md)

HP가 Dead가 됐다고 Entity가 자동 Despawn되는 것은 아닙니다. 이런 연동은 프로젝트 Host 또는 Registry Binding 정책으로 구성합니다.
