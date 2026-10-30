---
date: 2026-10-30
author: Wiley Wiggins
project: "Phrenologist"
---

# Plugging It In

From last night, moved the vertex mappings to their own resource file.
Random high values as a start, get the changed indexes, measure changes in world units, which region are they in, ding the appropriate stat.

Added some serialized tuning controls to meshdeformer

## Region Mappings

From `Assets/Resources/HeadRegions.txt`:

| Region | Stat | Vertices | Why |
| --- | --- | --- | --- |
| Chin | Strength | 1153 | "Sense of Dignity, Will/Strength" |
| Nose | Dexterity | 1391 | reaction to shock? *Erlebnis* |
| Smile Lines | Constitution | 761 | "Health, Heart, Vitality" |
| Center Brow | Intelligence | 842 | memory |
| Upper Left Forehead | Intelligence | 544 | wit |
| Upper Right Forehead | Intelligence | 501 | wit |
| Left Center | Wisdom | 736 | temples, judgment |
| Right Center | Wisdom | 699 | temples, judgment |
| High Crown | Wisdom | 379 | arbitrary |
| Mouth | Charisma | 1405 | "empathy, wish fulfillment" |

Testing with a nose punch zeroed out a bunch of stats in a way that scared me that the wobbling model registered as vertex movements, but they're coming from a comparison between displacedVertices and originalVertices and both are relative to the mesh and only get changed by AddDent.

Taking a pass to put every vertex in just one reason seems to have fixed it.

I think I am calling it on the vertex stat association. Looks good, ship it. Tomorrow I'll tune the gamefeel, add the juice, all that.

Builds are in [Builds/2026-10-30](../../Builds/2026-10-30/)
