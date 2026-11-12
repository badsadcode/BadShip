# Message to Sage — Project Brief

Dear completely unsuspecting Sage,

We have an independent project: a **procedural 3D spaceship exterior generator**. Yes, we have another idea. No, the packaging does not contain an explosive device. Probably.

### What to build
A standalone, game-engine-agnostic tool to generate believable **blocky, chunky, industrial, modular** spacecraft from a seed. These are working ships, not aerodynamic fighters or sleek luxury cruisers. The user-provided Thalassa submarine illustration establishes the desired structural language: wide, layered, heavy, utilitarian, generously detailed without sacrificing strong primary masses.

### Immediate requirements
1. Procedural silhouette families with very different proportions and module arrangements.
2. Structural skeleton + meaningful modules (bridge, core, cargo, thrusters, equipment).
3. Functional placement rules (attachment, docking clearance, engine exhaust).
4. Artist-friendly GLB export and named module hierarchy.
5. Offline preview, recipe/seed save, validity checks and a gallery of candidates.
6. Independent program. Future NoorMD **NoorShip** bench is a destination, *not an immediate integration task*.

### Please avoid
- Smooth aircraft-like ships as the default; generic sausages covered in random panels.
- Endless planning/phases without a viewable first working generator.
- Ship interiors, combat, flight controls, physics or full game systems in this project.
- Claims of verified engine import without an import test.

### How to begin
Read `01_VISUAL_STYLE_BRIEF.md`, inspect reference images, then use `02_PRODUCT_DESIGN_CONCEPT.md` and `03_MODULAR_GENERATION_SYSTEM.md` to choose a minimal executable design. Produce several **visibly different**, *good-looking even in greybox*, industrial ship exteriors, export GLB, and show side-by-side renders. Tune the silhouettes before increasing surface detail. Propose pragmatic implementation decisions rather than overengineering.

### Future handoff
When the standalone generator is mature, write a self-contained technical design/handoff for another coding session to implement **NoorShip** in NoorMD, potentially in native C#. That session must not need to know this conversation. Document geometry algorithms, recipes, data and export contracts, extension points, tests, limitations and licensing of dependencies.

### Secret label
> DEFINITELY NOT A BOMB. THOUGH SHIPS MIGHT RUN ON DARK MATTER AND TORMENTED SOULS OF CHAOTIC GAME-DEV GOBLINS. HANDLE WITH CARE.

— Tomek & Noor, Bad Ideas Works 😈
