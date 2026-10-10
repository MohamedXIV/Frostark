# Characters, crew, training and progression

## Role of people

The player is a **physical protagonist**, not an abstract captain avatar. Each persistent survivor may have profession, experience, skills, traits, condition, cold tolerance, morale, relationships, optional history and equipment. Typical roster **8–30 survivors** is a design range, not an enforced maximum. Every person can matter; losing a skilled engineer or doctor is a world-state event, not an anonymous population decrement.

## Dual assignment — one person, different duties

- **Ship assignment:** current shipboard station/job, shift, duty priority, rest state and replacement policy.
- **Expedition assignment:** optional landing-party membership, tactical role, armor/clothing, weapons, carried gear and supplies.
- **Persistent abilities:** skill proficiencies and perks common to both contexts.

There are no hard disjoint worker/soldier populations. A medic with a rifle can accompany a scavenging party, but that removes a clinician from shipboard care while away. Ship task availability and speed must update immediately and remain simulated in the background.

## Progression direction — proposed, not locked

Prefer **skill use + training + a few meaningful perks** rather than inflated universal HP and damage from character level. Candidate skills: rifles, sidearms, heavy weapons, stealth, leadership, scavenging, survival, navigation, engineering, medicine, cooking and repair. Equipment proficiency and hard environmental gear progression allow entry into hazardous sites.

Player could have broader perk/leadership choices; crew advancement is smaller but individually meaningful. A universal numerical level and final tree scope are **unsettled**. XP progression from repetitive actions needs anti-grind/diminishing-returns evaluation.

## Training is production

Training takes world time, space, instructors, gear/supplies and rest. It competes with repairs, medical work and watch duty. Theory/practice may have different caps; field experience improves combat application. Mentorship could reduce risk of losing a unique specialist. Avoid training that gives mastery from passive idling.

## Conditions and social consequences

Health, injuries, sickness, fatigue, hunger, morale, cold exposure and stress influence task suitability. Crew schedules and optional relationships create conflicts. Outcomes are visible and inspectable. Death may be permanent; the player's death model (game over vs succession) remains open.

## Data contract

CharacterId, traits, skills/proficiency state, perk unlocks, current duty/expedition, equipment, health/cold/fatigue/morale and persistent relations are serialized world data. The same character cannot be assigned to mutually exclusive active tasks; every move of staff is a validated command.

## Acceptance scenarios

Remove the only engineer while mid-repair; train a replacement; embark a medic and verify care changes aboard; apply wound/cold penalties on return; sleep while off-duty crew rest but engine watch remains staffed; save/reload unchanged assignments and proficiency.
