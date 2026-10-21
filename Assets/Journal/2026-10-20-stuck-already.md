---
date: 2026-10-20
author: Wiley Wiggins
project: "Phrenologist"
---

# Stuck Already

OK, finished the catlike coding tutorial and I have no click effect, I'm not seeing the debug lines from the camera and no console messages. Bluh.

*update: I just forgot to add a mesh collider to the model*

Ok, the initial effect from the tutorial is a slow, rubbery, jello like deformation.

That came out too subtle, so I put a `force` multiplier back on the input script, alongside `depth`, `radius`, and a `maxDepth` cap on the deformer.

Next I wanted the head to rock a little when punched, so I gave it a Rigidbody and set it on a kinematic cube. It fell straight through (How have I forgotten all this stuff so quickly?) Unity won't simulate a concave Mesh Collider on a dynamic Rigidbody, and ticking Convex didn't work either because the head is over the 256-polygon limit for convex hulls. The fix was to split physics from clicking:

- An empty `Head` parent holds the dynamic Rigidbody plus simple box, sphere, and capsule (neck) colliders for physics (box & capsule are in child objects).
- The model is a child with its detailed concave Mesh Collider for clicks, plus its own kinematic Rigidbody so that collider is allowed.

The input script now also applies an `impulse` at the hit point, to the first non-kinematic Rigidbody up the hierarchy, so off-center hits rock the head.

Then only a small patch of the lower face could be dented. The raycast hits the first collider in its path, and the physics box and sphere were big enough to cover most of the face, so those were catching the clicks. Shrinking them inside the mesh fixed it. Behavior is pretty good now!
