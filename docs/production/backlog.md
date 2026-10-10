# Canonical GitHub issue map — FrostArk

All items are in [MohamedXIV/Frostark Issues](https://github.com/MohamedXIV/Frostark/issues), created on 2026-10-10. The issues' **acceptance criteria** and dependencies are authoritative for execution; roadmap describes product direction. Do not mark any Epic done merely because documentation exists.

| Parent Epic | Implementation issues |
| --- | --- |
| [E00 Governance #2](https://github.com/MohamedXIV/Frostark/issues/2) | [F01 #13](https://github.com/MohamedXIV/Frostark/issues/13) starter hygiene; [F02 #14](https://github.com/MohamedXIV/Frostark/issues/14) cloud validation; [F03 #43](https://github.com/MohamedXIV/Frostark/issues/43) Projects board |
| [E01 Core/simulation #3](https://github.com/MohamedXIV/Frostark/issues/3) | [A01 #15](https://github.com/MohamedXIV/Frostark/issues/15) pure C#; [A02 #16](https://github.com/MohamedXIV/Frostark/issues/16) clock; [A03 #17](https://github.com/MohamedXIV/Frostark/issues/17) state/commands; [A04 #18](https://github.com/MohamedXIV/Frostark/issues/18) saves; [A05 #19](https://github.com/MohamedXIV/Frostark/issues/19) Unity bridge/DI |
| [E02 Unity runtime #4](https://github.com/MohamedXIV/Frostark/issues/4) | [V02 #21](https://github.com/MohamedXIV/Frostark/issues/21) player input; [V03 #22](https://github.com/MohamedXIV/Frostark/issues/22) ship/shore scene bridge |
| [E03 Ship survival #5](https://github.com/MohamedXIV/Frostark/issues/5) | [S01 #24](https://github.com/MohamedXIV/Frostark/issues/24) heat/compartments; [S02 #25](https://github.com/MohamedXIV/Frostark/issues/25) staffing; [S03 #26](https://github.com/MohamedXIV/Frostark/issues/26) modules |
| [E04 Ground combat/AI #6](https://github.com/MohamedXIV/Frostark/issues/6) | [C01 #27](https://github.com/MohamedXIV/Frostark/issues/27) weapons; [C02 #28](https://github.com/MohamedXIV/Frostark/issues/28) perception; [C03 #29](https://github.com/MohamedXIV/Frostark/issues/29) cover; [C04 #30](https://github.com/MohamedXIV/Frostark/issues/30) encounter |
| [E05 Crew/squad #7](https://github.com/MohamedXIV/Frostark/issues/7) | [P01 #31](https://github.com/MohamedXIV/Frostark/issues/31) progression/training; [P02 #32](https://github.com/MohamedXIV/Frostark/issues/32) squad |
| [E06 Naval #8](https://github.com/MohamedXIV/Frostark/issues/8) | [N01 #33](https://github.com/MohamedXIV/Frostark/issues/33) ice/navigation; [N02 #34](https://github.com/MohamedXIV/Frostark/issues/34) naval combat |
| [E07 World/story #9](https://github.com/MohamedXIV/Frostark/issues/9) | [W01 #35](https://github.com/MohamedXIV/Frostark/issues/35) zones/sites; [W02 #36](https://github.com/MohamedXIV/Frostark/issues/36) factions; [W03 #37](https://github.com/MohamedXIV/Frostark/issues/37) mysteries |
| [E08 Content/economy #10](https://github.com/MohamedXIV/Frostark/issues/10) | [I01 #23](https://github.com/MohamedXIV/Frostark/issues/23) inventory; [I02 #38](https://github.com/MohamedXIV/Frostark/issues/38) crafting/trade |
| [E09 Visual/UI #11](https://github.com/MohamedXIV/Frostark/issues/11) | [V01 #20](https://github.com/MohamedXIV/Frostark/issues/20) art benchmark; [UI01 #39](https://github.com/MohamedXIV/Frostark/issues/39) UI/accessibility |
| [E10 Quality/release #12](https://github.com/MohamedXIV/Frostark/issues/12) | [Q01 #40](https://github.com/MohamedXIV/Frostark/issues/40) tooling; [Q02 #41](https://github.com/MohamedXIV/Frostark/issues/41) profiling; [Q03 #42](https://github.com/MohamedXIV/Frostark/issues/42) release gates |

## Dependency-ready starting work

**Ready without Unity/editor execution:** [F01 #13](https://github.com/MohamedXIV/Frostark/issues/13) and [F02 #14](https://github.com/MohamedXIV/Frostark/issues/14). Only perform F01 remote cleanup after reconciling local unpushed Unity changes—`TutorialInfo` itself is approved for removal. [F03 #43](https://github.com/MohamedXIV/Frostark/issues/43) needs a GitHub Projects creation/attach capability (currently unavailable in connected tools), **not** a Unity editor action.

Once F02 creates reproducible domain checks, A01 #15 becomes ready; then A02 #16 and A03 #17 can proceed independently. F01 unlocks V01 #20; A03 also unlocks I01 #23. Avoid treating epic parents as executable stories.

## Board status

A **dedicated native GitHub Projects v2 board is NOT created yet**. RepoPilot Dev currently exposes `list_projects`, `inspect_project`, `update_project_item`, `bind_project` etc. but no native `create_project` or `add_project_item`. Do not report a board URL/number until created and verified. Track setup in [F03 #43](https://github.com/MohamedXIV/Frostark/issues/43). Proposed board columns: Backlog, Ready, In Progress, In Review, Blocked, Done; fields: Priority P0–P3, Phase R0–R8, Type Epic/Task/Bug/Decision. RepoPilot's dependency-ready query can activate after board setup.

No GH Actions workflows should be added for these issues by default. 
