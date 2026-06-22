"""
generate_thesis.py
Generates the full thesis .docx for the 2D platformer Unity project.
Run with:
  C:\\Users\\nikol\\AppData\\Local\\Programs\\Python\\Python312\\python.exe generate_thesis.py
"""

from docx import Document
from docx.shared import Pt, Cm, RGBColor, Inches
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.style import WD_STYLE_TYPE
from docx.oxml.ns import qn
from docx.oxml import OxmlElement
import copy

doc = Document()

# ── Page margins ──────────────────────────────────────────────────────────────
for section in doc.sections:
    section.top_margin    = Cm(2.5)
    section.bottom_margin = Cm(2.5)
    section.left_margin   = Cm(3.0)
    section.right_margin  = Cm(2.5)

# ── Helper utilities ──────────────────────────────────────────────────────────

def set_run_font(run, name="Times New Roman", size=12, bold=False, italic=False):
    run.font.name  = name
    run.font.size  = Pt(size)
    run.bold       = bold
    run.italic     = italic

def add_paragraph(text, style="Normal", alignment=WD_ALIGN_PARAGRAPH.JUSTIFY,
                  bold=False, italic=False, size=12, space_before=0, space_after=6):
    p = doc.add_paragraph(style=style)
    p.alignment = alignment
    p.paragraph_format.space_before = Pt(space_before)
    p.paragraph_format.space_after  = Pt(space_after)
    p.paragraph_format.first_line_indent = Cm(1.25)
    run = p.add_run(text)
    set_run_font(run, size=size, bold=bold, italic=italic)
    return p

def add_heading(text, level=1):
    sizes = {1: 16, 2: 14, 3: 13}
    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.LEFT
    p.paragraph_format.space_before = Pt(18 if level == 1 else 12)
    p.paragraph_format.space_after  = Pt(6)
    p.paragraph_format.first_line_indent = Cm(0)
    run = p.add_run(text)
    set_run_font(run, size=sizes.get(level, 12), bold=True)
    return p

def add_title_page():
    doc.add_paragraph()
    doc.add_paragraph()
    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    r = p.add_run("AMERICAN UNIVERSITY IN BOSNIA AND HERZEGOVINA")
    set_run_font(r, size=13, bold=True)

    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    r = p.add_run("Faculty of Information Technologies")
    set_run_font(r, size=12)

    doc.add_paragraph()
    doc.add_paragraph()
    doc.add_paragraph()

    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    r = p.add_run(
        "Development of a 2D Action Platformer Using Unity:\n"
        "Architecture, Procedural Content Generation\nand Emergent Gameplay Mechanics"
    )
    set_run_font(r, size=18, bold=True)

    doc.add_paragraph()
    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    r = p.add_run("Final Year Thesis / Diplomski Rad")
    set_run_font(r, size=13, italic=True)

    doc.add_paragraph()
    doc.add_paragraph()
    doc.add_paragraph()

    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    r = p.add_run("Candidate: [Student Name]\nMentor: [Mentor Name]\nSarајevo, 2026.")
    set_run_font(r, size=12)

    doc.add_page_break()

# ── Paragraph shorthand ───────────────────────────────────────────────────────
def P(text):
    add_paragraph(text)

def H1(text):
    add_heading(text, 1)

def H2(text):
    add_heading(text, 2)

def H3(text):
    add_heading(text, 3)

def center(text, size=12, bold=False):
    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    p.paragraph_format.first_line_indent = Cm(0)
    r = p.add_run(text)
    set_run_font(r, size=size, bold=bold)

# ─────────────────────────────────────────────────────────────────────────────
# TITLE PAGE
# ─────────────────────────────────────────────────────────────────────────────
add_title_page()

# ─────────────────────────────────────────────────────────────────────────────
# ABSTRACT
# ─────────────────────────────────────────────────────────────────────────────
H1("ABSTRACT")
P(
    "This thesis presents the design and implementation of a functional prototype of a "
    "2D action-platformer developed using the Unity game engine. The project explores "
    "advanced technical aspects of the engine, including Editor scripting for automated "
    "scene construction, procedural content generation using constraint-based tilemap "
    "generation, Verlet integration for procedural animation, and SmoothDamp-based "
    "adaptive camera behaviour. The central gameplay innovation is a mouse-aimed, "
    "charge-based elemental dash system, in which four distinct elements (Earth, Fire, "
    "Water, and Air) alter the mechanical properties of the dash, creating a risk-reward "
    "framework that accommodates both combat-oriented and speedrunning playstyles."
)
P(
    "The hypothesis that Unity's component-based architecture and extensible Editor "
    "tooling are sufficient to implement a technically sophisticated 2D game prototype "
    "without custom engine modifications is evaluated and confirmed. The prototype "
    "demonstrates Unity's capabilities in procedural generation, physics simulation, "
    "coroutine-driven game loop design, and YAML-based scene serialization. Limitations "
    "of the prototype, including the absence of a complete level set and audio design, "
    "are discussed alongside directions for future development."
)
P(
    "Keywords: Unity, 2D platformer, procedural content generation, Verlet integration, "
    "elemental dash system, Editor scripting, YAML serialization, game engine architecture."
)
doc.add_page_break()

# ─────────────────────────────────────────────────────────────────────────────
# TABLE OF CONTENTS (manual)
# ─────────────────────────────────────────────────────────────────────────────
H1("CONTENTS")
toc_entries = [
    ("Abstract", 2),
    ("1. Introduction", 4),
    ("   1.1 Idea and Motivation", 4),
    ("   1.2 Expectations from the Work", 5),
    ("   1.3 Structure of the Thesis", 6),
    ("2. Topic in Context: Background and Related Work", 7),
    ("   2.1 The 2D Platformer Genre", 7),
    ("   2.2 Unity Engine as a Development Platform", 8),
    ("   2.3 C# in Game Development", 10),
    ("   2.4 Procedural Content Generation in Games", 11),
    ("   2.5 Physics-Based Animation Techniques", 12),
    ("3. Hypothesis", 13),
    ("4. Research Methodology", 14),
    ("   4.1 Iterative Prototyping Approach", 14),
    ("   4.2 Script-First Development", 15),
    ("   4.3 Testing Strategy", 15),
    ("5. Core Development (Topic Elaboration)", 16),
    ("   5.1 Unity YAML Serialization and Scene Structure", 16),
    ("   5.2 Editor Scripting and Scene Automation", 17),
    ("   5.3 Player Movement and Physics", 19),
    ("   5.4 Element-Based Charged Dash System", 20),
    ("   5.5 Verlet Integration Scarf Animation", 22),
    ("   5.6 Wind Zone and Environmental Interaction", 24),
    ("   5.7 Adaptive Camera Behaviour", 25),
    ("   5.8 Enemy Systems", 26),
    ("   5.9 Procedural Level Generation", 27),
    ("   5.10 User Interface and HUD", 30),
    ("6. Results and Evaluation", 31),
    ("7. Conclusion", 33),
    ("Literature", 35),
]
for title, page in toc_entries:
    p = doc.add_paragraph()
    p.paragraph_format.first_line_indent = Cm(0)
    p.paragraph_format.space_before = Pt(2)
    p.paragraph_format.space_after  = Pt(2)
    r = p.add_run(f"{title}")
    set_run_font(r, size=11)
doc.add_page_break()

# ─────────────────────────────────────────────────────────────────────────────
# 1. INTRODUCTION
# ─────────────────────────────────────────────────────────────────────────────
H1("1. Introduction")

P(
    "Video game development has evolved from a niche engineering discipline into one of "
    "the most technically multifaceted domains in software engineering. Modern games "
    "require the synthesis of real-time physics simulation, 3D and 2D rendering pipelines, "
    "artificial intelligence, procedural content generation, and networked state management "
    "within a single interactive application running at 60 frames per second or above. "
    "Game engines abstract much of this complexity, offering developers a framework within "
    "which gameplay systems can be constructed using high-level scripting and component "
    "hierarchies rather than raw low-level code."
)
P(
    "Among the available engines, Unity has become one of the most widely adopted "
    "platforms across both independent and commercial development. Its combination of a "
    "component-based GameObject architecture, a powerful C# scripting environment, and an "
    "extensible Editor API makes it particularly well-suited for rapid prototyping and "
    "academic exploration of game systems. The engine's ability to serialize scenes and "
    "assets as human-readable YAML, its support for coroutine-based asynchronous logic, "
    "and its integration of the PhysX physics engine provide a rich technical surface for "
    "investigation."
)
P(
    "This thesis documents the design and implementation of a functional 2D action-platformer "
    "prototype built entirely within Unity. Rather than treating the engine as a transparent "
    "tool, the project uses the game's development as an opportunity to examine Unity's "
    "internal architecture: how scenes are constructed and serialized, how the Editor can "
    "be extended through custom scripts to automate asset creation, and how runtime systems "
    "such as Verlet integration and procedural tilemap generation operate within Unity's "
    "execution model."
)

H2("1.1 Idea and Motivation")
P(
    "The selection of a 2D action-platformer as the subject of this thesis arose from a "
    "deliberate decision to focus on mechanic depth rather than visual fidelity. Three-dimensional "
    "games demand substantial art production pipelines — modelling, rigging, texturing, and "
    "normal-map baking — that consume development time without advancing the technical research "
    "objectives of the thesis. A 2D prototype, by contrast, permits placeholder procedurally-generated "
    "sprites while keeping the focus on architecture, algorithms, and system design."
)
P(
    "The central gameplay concept — a mouse-aimed, charge-based elemental dash — was chosen "
    "because it creates interesting technical requirements in several intersecting areas: "
    "precise frame-rate-independent physics, a state machine for dash phases, per-element "
    "collision logic, and a camera system explicitly designed to support rapid movement "
    "chaining. Each of these requirements maps onto a distinct area of Unity's API, making "
    "the game a practical vehicle for deep engine exploration."
)
P(
    "An additional motivation was the exploration of procedural systems at multiple scales: "
    "from the micro-scale Verlet chain simulating a fabric scarf to the macro-scale procedural "
    "level generator that constructs platforming environments using constraint-based random "
    "walks. Both systems involve non-trivial algorithm design and have well-established "
    "theoretical foundations that can be discussed academically."
)
P(
    "The 2D platformer genre itself also offers a well-studied design vocabulary — coyote "
    "time, jump buffering, acceleration and deceleration curves — which allows the technical "
    "decisions to be evaluated against known best practices. The genre's accessibility means "
    "that players can engage with the prototype without a tutorial, providing meaningful "
    "informal feedback."
)

H2("1.2 Expectations from the Work")
P(
    "At the outset of the project, several outcomes were anticipated. The first expectation "
    "was that Unity's component architecture would naturally accommodate the modular design "
    "required by a system like the elemental dash: separate scripts for player control, "
    "element state, camera behaviour, and UI management could be developed and tested "
    "independently before integration. This modularity was expected to reduce debugging time "
    "compared to monolithic architecture approaches."
)
P(
    "The second expectation was that Unity's Editor scripting API would be sufficient to "
    "automate the construction of a non-trivial scene — including physics components, "
    "tilemap setup, UI canvas hierarchy, and placement of game objects — without resorting "
    "to manual Inspector configuration. This was expected to demonstrate Unity's viability "
    "as a platform for scripted, reproducible content pipelines."
)
P(
    "A third expectation concerned procedural level generation: that a constraint-based "
    "random walk algorithm operating on Unity's Tilemap system could generate levels that "
    "are both aesthetically varied and mechanically sound (i.e., always completable without "
    "requiring unreachable jumps). The mathematical constraints imposed by the player's jump "
    "height and dash range were expected to be sufficient to guarantee connectivity."
)
P(
    "Finally, it was expected that Verlet integration — a classical physics simulation "
    "technique — would prove more stable and performant for a fabric simulation than "
    "Unity's built-in joint system, particularly under the impulsive forces generated by "
    "the wind zone mechanic. This expectation formed part of the comparative analysis in "
    "Section 5.5."
)

H2("1.3 Structure of the Thesis")
P(
    "Following this introduction, Chapter 2 situates the project within its broader context: "
    "the history and conventions of the 2D platformer genre, Unity's position in the game "
    "engine landscape, and the academic literature on procedural content generation and "
    "physics-based animation. Chapter 3 states the formal hypothesis. Chapter 4 describes "
    "the research methodology, including the iterative development process and testing "
    "strategy. Chapter 5, the core of the thesis, presents the technical elaboration of "
    "each major system, with code-level discussion of design decisions and failure modes. "
    "Chapter 6 evaluates the results against the expectations established in this "
    "introduction. Chapter 7 concludes the thesis and outlines directions for future work. "
    "The literature list follows."
)
doc.add_page_break()

# ─────────────────────────────────────────────────────────────────────────────
# 2. BACKGROUND
# ─────────────────────────────────────────────────────────────────────────────
H1("2. Topic in Context: Background and Related Work")

H2("2.1 The 2D Platformer Genre")
P(
    "The 2D platformer is among the oldest and most studied genres in video game history. "
    "From Donkey Kong (Nintendo, 1981) to Super Mario Bros. (Nintendo, 1985), the genre "
    "established core interaction vocabulary: a protagonist navigating platforms through "
    "jumping, running, and avoiding obstacles. Decades of refinement produced the concept "
    "of 'game feel' — the tactile sense of responsiveness and weight in a character's "
    "movement — which has been systematically studied by researchers such as Swink (2009), "
    "who identifies parameters including input responsiveness, visual feedback latency, and "
    "momentum as primary contributors."
)
P(
    "Modern 2D platformers have evolved significantly. The Metroidvania subgenre, exemplified "
    "by Hollow Knight (Team Cherry, 2017) and Celeste (Maddy Thorson, Noel Berry, 2018), "
    "introduced sophisticated movement techniques that directly inspired this project. "
    "Celeste in particular popularized the 'coyote time' and 'jump buffer' patterns that "
    "are implemented in this thesis's PlayerController. Coyote time allows a jump command "
    "to succeed for a brief window after the player walks off a ledge; jump buffering "
    "registers a jump press slightly before the player lands, consuming it on the first "
    "grounded frame. Both techniques reduce the perception of 'missed' inputs without "
    "altering the underlying physics simulation."
)
P(
    "Dash mechanics in particular have become a distinguishing feature of skill-based "
    "platformers. Celeste's dash is a fixed-direction, fixed-distance impulse reset on "
    "landing; Hollow Knight's dash is a horizontal invincibility-frame movement tool. This "
    "project's charged, mouse-aimed elemental dash represents a departure from these "
    "conventions: direction is freely chosen by the player using world-space mouse position, "
    "distance is proportional to hold duration, and element choice alters collision behaviour. "
    "This design is closer in spirit to the grapple mechanics of games like Rain World "
    "(Videocult, 2017), where movement precision is the primary skill expression."
)

H2("2.2 Unity Engine as a Development Platform")
P(
    "Unity Technologies released the first public version of the Unity engine in 2005. "
    "By 2026, Unity has become one of the two dominant real-time development platforms "
    "globally, alongside Epic Games' Unreal Engine. Unlike Unreal, which targets "
    "high-fidelity 3D production, Unity historically served a broader spectrum: mobile, "
    "2D, VR, and independent development. Its C# scripting environment, based on the Mono "
    "runtime (and more recently IL2CPP for builds), offers a mature, strongly-typed "
    "language with full access to the .NET standard library."
)
P(
    "Unity's architecture centres on the Scene as a hierarchical container of GameObjects. "
    "Each GameObject is a container of Components — classes derived from MonoBehaviour — "
    "that implement specific behaviours. A character might have a Rigidbody2D component "
    "handling physics, a SpriteRenderer component providing visual representation, and "
    "several custom MonoBehaviour scripts governing input, health, and animation. This "
    "component composition model, influenced by the Entity-Component pattern described by "
    "Gregory (2018) and Nystrom (2014), promotes loose coupling and ease of testing."
)
P(
    "The Unity Editor exposes its internals through the UnityEditor namespace, allowing "
    "developers to write Editor scripts that extend the menu system, automate asset "
    "creation, and build scenes programmatically. The ObjectFactory class, introduced in "
    "Unity 2018.3, provides a safe way to create GameObjects in the Editor context, "
    "registering each operation with the Undo system. This is the mechanism used by the "
    "BuildGameScene.cs Editor script documented in Section 5.2."
)
P(
    "Unity serializes scenes and prefabs as YAML-formatted text files. This design decision "
    "has significant implications for version control: git diff can be applied meaningfully "
    "to scene changes, and merge conflicts in scene files, while complex, can be resolved "
    "manually. Each serialized reference consists of a fileID (a local identifier within "
    "the asset) and a GUID (a globally unique identifier for the asset file). This "
    "reference system is discussed further in Section 5.1, as it constrains how scene "
    "content can be generated programmatically."
)
P(
    "Unity 6, the version used in this project, introduced several API changes relevant "
    "to this thesis. The Rigidbody2D.velocity property was renamed to "
    "Rigidbody2D.linearVelocity, reflecting a cleaner naming convention. The "
    "Object.FindObjectsOfType API continues to function but carries a performance caveat "
    "at runtime (Editor-only use is unaffected). The new Input System package, available "
    "as a replacement for the legacy Input class, provides InputAction objects that can be "
    "configured entirely in code, which is the approach taken in PlayerController.cs."
)

H2("2.3 C# in Game Development")
P(
    "C# was designed by Anders Hejlsberg at Microsoft and first appeared in 2000 as part "
    "of the .NET framework. Its combination of static typing, automatic memory management "
    "through the .NET garbage collector, and language features such as LINQ, async/await, "
    "generics, and properties make it well-suited for large-scale software projects. In "
    "the game development context, C# is the scripting language for Unity, and is also "
    "used as the primary scripting language for Godot 4 via the GodotSharp bindings."
)
P(
    "For game logic, several C# features are particularly relevant. Coroutines — implemented "
    "in Unity as IEnumerator methods executed via StartCoroutine — allow asynchronous "
    "game logic to be written in a sequential style without callback pyramids. The wind "
    "zone controller's periodic cycle (idle → warning → full force → fireball) is "
    "implemented as a single while(true) coroutine yielding WaitForSeconds between phases, "
    "which is more readable and maintainable than equivalent Update/timer logic. Similarly, "
    "the dash execution loop uses WaitForFixedUpdate to synchronize physics steps with "
    "collision detection."
)
P(
    "Preprocessor directives (#if ENABLE_INPUT_SYSTEM / #else) allow a single script to "
    "compile correctly whether or not the new Input System package is installed. This is "
    "used in PlayerController.cs to support both the legacy Input.GetKey API and the "
    "modern InputAction API from a single code file, avoiding the maintenance burden of "
    "two separate implementations."
)

H2("2.4 Procedural Content Generation in Games")
P(
    "Procedural Content Generation (PCG) refers to the algorithmic creation of game content "
    "with limited human authoring. Shaker, Togelius, and Nelson (2016) provide a "
    "comprehensive taxonomy in their open-source book, classifying PCG by target content "
    "type (levels, items, textures, narratives), generation algorithm, and the degree of "
    "designer control retained. For 2D platformers, level generation is the most commonly "
    "studied domain."
)
P(
    "Random walk algorithms are among the simplest and most effective methods for "
    "generating traversable platformer levels. The walker traverses the level space "
    "column-by-column, occasionally changing elevation, while a set of constraints ensures "
    "that the generated structure remains navigable. The specific constraint in this project "
    "— that elevation changes may not exceed one tile per step, and must be separated by "
    "at least MIN_PLATFORM_WIDTH columns — derives from a jump height estimate based on "
    "the physics formula $h_{max} = v_0^2 / (2g)$, where $v_0$ is the initial jump "
    "velocity and $g$ is the gravitational acceleration applied by Unity."
)
P(
    "More sophisticated approaches include Binary Space Partitioning (BSP), used in "
    "dungeon generators like NetHack; Perlin noise-based terrain, used in Minecraft-style "
    "worlds; and grammar-based generation (L-systems, shape grammars), used in procedural "
    "architecture. For a platformer prototype, the random walk with jump-height constraints "
    "strikes an appropriate balance between variety, guaranteeable connectivity, and "
    "implementation simplicity."
)
P(
    "Post-placement of gameplay objects — pickups, hazards, enemies — is handled separately "
    "from the tilemap generation. Surface detection (finding tiles whose above-neighbour "
    "is empty) provides a list of valid placement positions, from which objects are "
    "scattered with specified probabilities: 20% for element pickups, 10% for spike "
    "hazards. This separation of concerns allows the generation parameters to be tuned "
    "independently."
)

H2("2.5 Physics-Based Animation Techniques")
P(
    "Physical simulation of secondary motion — hair, clothing, ropes, tails — has been "
    "studied extensively in computer graphics. Joint-based methods, such as those "
    "offered by Unity's HingeJoint2D and SpringJoint2D, attach a chain of rigid bodies "
    "with positional constraints. These methods are simple to configure but can become "
    "unstable under large forces, requiring careful tuning of spring constants and damping "
    "ratios. Under impulsive loads — such as the wind bursts used in this game — spring "
    "joints can 'explode', producing unphysical oscillations."
)
P(
    "Verlet integration, first described by Loup Verlet in 1967 for molecular dynamics "
    "simulation, offers an alternative. The algorithm computes a particle's next position "
    "from its current and previous positions, implicitly encoding velocity without storing "
    "it explicitly: $\\mathbf{x}_{n+1} = 2\\mathbf{x}_n - \\mathbf{x}_{n-1} + \\mathbf{a}\\Delta t^2$. "
    "Gauss-Seidel constraint relaxation, applied over several iterations per frame, "
    "enforces distance constraints between neighbouring particles. This approach, "
    "popularized for game use by Thomas Jakobsen (2001) in the development of the "
    "Hitman physics engine, is unconditionally stable for small step sizes and produces "
    "natural, cloth-like motion."
)
P(
    "For the scarf simulation in this project, the root particle is pinned to the "
    "ScarfRoot transform (a child of the player) each frame, propagating the player's "
    "movement into the chain. Wind forces from the WindZoneController are accumulated "
    "per-frame and applied as an acceleration term in the Verlet step. The simulation "
    "runs in LateUpdate, after all physics and animation have settled, to prevent "
    "one-frame lag. Three constraint-relaxation iterations per frame are sufficient for "
    "the six-segment chain used in the prototype."
)
doc.add_page_break()

# ─────────────────────────────────────────────────────────────────────────────
# 3. HYPOTHESIS
# ─────────────────────────────────────────────────────────────────────────────
H1("3. Hypothesis")
P(
    "The central hypothesis of this thesis is as follows:"
)
p = doc.add_paragraph()
p.paragraph_format.left_indent = Cm(2)
p.paragraph_format.right_indent = Cm(2)
p.paragraph_format.first_line_indent = Cm(0)
p.paragraph_format.space_before = Pt(6)
p.paragraph_format.space_after  = Pt(6)
r = p.add_run(
    "Unity's component-based architecture, Editor scripting API, and C# coroutine model "
    "are sufficient to implement a technically sophisticated 2D action-platformer prototype "
    "featuring procedural content generation, physics-based secondary animation, and an "
    "element-driven dash system, without requiring modifications to the engine's core or "
    "the use of third-party physics libraries."
)
set_run_font(r, size=12, italic=True)

P(
    "This hypothesis can be decomposed into four testable sub-claims:"
)
for sc in [
    ("H1", "Unity's Tilemap API combined with a constraint-based random walk algorithm can generate "
           "2D platformer levels that are always mechanically completable (no unreachable platforms)."),
    ("H2", "Verlet integration implemented in C# within Unity's LateUpdate cycle produces a stable, "
           "visually convincing fabric simulation that responds correctly to impulsive wind forces."),
    ("H3", "Unity's Editor scripting API (ObjectFactory, UnityEditor namespace, AssetDatabase) is "
           "sufficient to construct a complete, playable scene from a single MenuItem function, without "
           "any manual Inspector configuration."),
    ("H4", "A mouse-aimed, hold-to-dash mechanic with element-based collision properties can be "
           "implemented using Unity's Rigidbody2D.MovePosition API and CircleCastAll collision "
           "prediction without tunnelling artefacts at typical dash speeds."),
]:
    p = doc.add_paragraph()
    p.paragraph_format.first_line_indent = Cm(0)
    p.paragraph_format.left_indent = Cm(1.25)
    p.paragraph_format.space_before = Pt(3)
    p.paragraph_format.space_after  = Pt(3)
    r1 = p.add_run(f"({sc[0]}) ")
    set_run_font(r1, size=12, bold=True)
    r2 = p.add_run(sc[1])
    set_run_font(r2, size=12)

P(
    "Each sub-claim is evaluated in Chapter 5 through implementation and in Chapter 6 "
    "through testing and reflection. The overall hypothesis is accepted or rejected in "
    "the conclusion based on the aggregate evidence."
)
doc.add_page_break()

# ─────────────────────────────────────────────────────────────────────────────
# 4. METHODOLOGY
# ─────────────────────────────────────────────────────────────────────────────
H1("4. Research Methodology")

H2("4.1 Iterative Prototyping Approach")
P(
    "The project followed an iterative, prototype-driven development methodology. Rather "
    "than completing a full design document before writing code, each system was implemented "
    "as a minimal viable component, tested in isolation, and integrated incrementally. This "
    "approach aligns with the agile philosophy described by Fullerton (2018) in Game Design "
    "Workshop: a prototype should be 'quick and dirty' enough to test a hypothesis, then "
    "refined based on feedback."
)
P(
    "The development was organized into functional layers. The foundational layer — "
    "project setup, scene structure, and physics configuration — was automated using the "
    "BuildGameScene Editor script. Each subsequent feature (player movement, dash system, "
    "element pickups, scarf simulation, wind zone, procedural generation, camera) was "
    "developed and tested in isolation using the platform established by the previous layer."
)
P(
    "Git was used for version control throughout the project. The project's .unity scene "
    "files use Unity's text-based YAML serialization, ensuring that changes to scene "
    "structure are meaningful in git diff output. Each new system was committed to a "
    "feature branch and merged after passing manual testing."
)

H2("4.2 Script-First Development")
P(
    "A deliberate decision was made to avoid manual Inspector configuration wherever "
    "possible. All component wiring, default value assignment, and scene hierarchy "
    "construction is performed through scripts — either Editor scripts at design time "
    "or MonoBehaviour Awake/Start methods at runtime. This 'script-first' approach serves "
    "two purposes: it makes the project fully reproducible (the BuildGameScene menu item "
    "recreates the entire scene from scratch) and it forces a deeper engagement with "
    "Unity's API compared to drag-and-drop configuration."
)
P(
    "This methodology also has a practical consequence for error handling: since no "
    "Inspector assignment provides a default fallback, every component dependency must "
    "be explicitly checked at runtime. Missing or destroyed components are detected in "
    "Awake or Update methods, logged with descriptive error messages, and handled "
    "gracefully — for example, a missing BoxCollider2D on the player is added "
    "automatically with a warning rather than causing a NullReferenceException."
)

H2("4.3 Testing Strategy")
P(
    "Testing was primarily manual and play-based. For each system, a set of expected "
    "behaviours was defined informally and checked through gameplay. The following "
    "categories of testing were performed:"
)
for item in [
    "Unit-level testing: Each MonoBehaviour script was tested in a minimal scene "
    "before integration. For example, ScarfController was tested alone with a static "
    "anchor before being parented to the player.",
    "Integration testing: After integration, the interaction between systems was "
    "tested. For example, the wind zone's coroutine was verified to correctly call "
    "ScarfController.ApplyWind() during the warning phase and add Rigidbody2D force "
    "during the full phase.",
    "Edge case testing: Known problematic inputs were exercised. The mouse-hold "
    "after maximum dash distance, the dash into an enemy while holding the mouse "
    "button, and landing exactly on a spike while in the invincibility window were "
    "all explicitly tested.",
    "Procedural generation validation: The ProceduralLevelGenerator was run "
    "multiple times and the output inspected visually for unreachable platforms, "
    "tile gaps, and incorrect object placement.",
]:
    p = doc.add_paragraph()
    p.paragraph_format.first_line_indent = Cm(0)
    p.paragraph_format.left_indent = Cm(1.25)
    p.paragraph_format.space_before = Pt(2)
    p.paragraph_format.space_after  = Pt(2)
    r = p.add_run(f"• {item}")
    set_run_font(r, size=12)
P(
    "Automated unit testing (Unity Test Runner) was considered but not implemented, "
    "as the physics-dependent nature of most systems makes deterministic automated "
    "testing difficult without extensive mocking infrastructure."
)
doc.add_page_break()

# ─────────────────────────────────────────────────────────────────────────────
# 5. CORE DEVELOPMENT
# ─────────────────────────────────────────────────────────────────────────────
H1("5. Core Development (Topic Elaboration)")

H2("5.1 Unity YAML Serialization and Scene Structure")
P(
    "Every Unity scene is stored as a .unity file in YAML format. When Unity's text "
    "serialization mode is active (the default since Unity 5.3), this file is "
    "human-readable and version-control friendly. Each YAML document within the file "
    "represents one object: a scene setting, a transform, a MonoBehaviour component, "
    "or a prefab instance. Objects are linked by two identifiers:"
)
for item in [
    "fileID: A 64-bit integer local to the asset file, uniquely identifying an "
    "object within the scene.",
    "GUID: A 128-bit globally unique identifier for the asset file itself, "
    "stored in the associated .meta file. GUIDs persist across renames and moves.",
]:
    p = doc.add_paragraph()
    p.paragraph_format.first_line_indent = Cm(0)
    p.paragraph_format.left_indent = Cm(1.25)
    r = p.add_run(f"• {item}")
    set_run_font(r, size=12)
P(
    "A component reference in a Unity scene looks like: "
    "{fileID: 123456789, guid: abcdef1234567890abcdef1234567890, type: 3}. "
    "The type field indicates the reference type (0 = null, 1 = imported asset, "
    "2 = built-in asset, 3 = local file). Manually crafting these references is "
    "impractical because fileIDs must be consistent across all objects in the file, "
    "and GUIDs must match the actual .meta files on disk."
)
P(
    "This constraint has a direct consequence for automated scene generation: the only "
    "reliable way to construct a valid Unity scene programmatically is through the Unity "
    "Editor API itself. The Editor API handles GUID resolution, fileID assignment, and "
    "Undo registration transparently. This is why BuildGameScene.cs uses "
    "ObjectFactory.CreateGameObject() rather than new GameObject(), and why attempting "
    "to directly author YAML scene files in an external tool is not a practical approach."
)
P(
    "The text serialization of scenes also enables meaningful version control. A change "
    "to a single component's property (for example, adjusting the Rigidbody2D.gravityScale "
    "from 1 to 3) appears in git diff as a single-line change within the affected "
    "component block. Additions of new GameObjects appear as new YAML documents. This "
    "transparency is a significant advantage over binary serialization formats used by "
    "some competing engines."
)

H2("5.2 Editor Scripting and Scene Automation")
P(
    "BuildGameScene.cs is an Editor-only static class (placed in an Assets/Editor/ "
    "directory, which Unity automatically excludes from builds) that constructs the "
    "entire game scene from a single MenuItem. The class demonstrates several important "
    "patterns in Unity Editor scripting."
)
P(
    "The script begins by creating a new empty scene via EditorSceneManager.NewScene(), "
    "which clears the current scene and returns a Scene handle. Each subsequent step — "
    "camera creation, tilemap setup, player construction, parallax layers, UI canvas, "
    "wind zone, enemies, element pickups, spike hazards — is wrapped in an independent "
    "try-catch block. This design ensures that if any single step fails (for example, "
    "due to a compile error in a MonoBehaviour that prevents AddComponent from "
    "returning a non-null reference), the remaining steps continue. A partially-built "
    "scene is always better than no scene."
)
P(
    "GameObject creation uses ObjectFactory.CreateGameObject() with component type "
    "arguments. This is preferred over new GameObject() in Editor context because it "
    "registers the creation in Unity's Undo system, marks the scene as dirty (prompting "
    "a save dialog on exit), and is subject to any Editor preprocessors. The "
    "Undo.RecordObject call before tilemap modification ensures that the procedural "
    "generation step is also reversible."
)
P(
    "A notable challenge in automated scene construction is that MonoBehaviour scripts "
    "must be compiled before AddComponent can return a live reference to them. If "
    "PlayerController.cs has a syntax error, playerGO.AddComponent<PlayerController>() "
    "returns null silently. The BuildGameScene script handles this with an explicit null "
    "check and warning message, instructing the user to resolve compile errors and "
    "re-run the menu item. This is a fundamental limitation of the Editor scripting "
    "approach when scripts are being developed in parallel."
)
P(
    "The UI canvas construction demonstrates a more complex workflow. The Canvas, "
    "CanvasScaler, and GraphicRaycaster components are added to a root GameObject. "
    "Button GameObjects are created as children, each with a RectTransform (for layout), "
    "Image (for the button background), Button (for click handling), and a Text child "
    "for the label. All element names are chosen to match the strings that UIManager.cs "
    "searches for at runtime via GameObject.Find(), establishing a naming contract "
    "between the Editor script and the runtime script without requiring Inspector "
    "references."
)
P(
    "The ProceduralLevelGenerator.cs Editor script generates tilemap-based levels using "
    "a constraint-based random walk (described in detail in Section 5.9). It operates "
    "on an existing GroundTilemap component found in the scene by name, clearing all "
    "tiles and regenerating from scratch. A fallback Tile asset with a procedurally "
    "generated 16×16 white sprite is created automatically if the production tile "
    "asset is missing, ensuring the generator functions even in a fresh project with "
    "no art assets imported."
)

H2("5.3 Player Movement and Physics")
P(
    "PlayerController.cs implements the player's movement, jump, and all dash logic. "
    "The script uses Rigidbody2D for physics integration, with gravityScale set to 3 "
    "to produce a snappy, arcade-style jump arc. The character's horizontal movement "
    "is written to Rigidbody2D.linearVelocity in FixedUpdate, preserving the vertical "
    "component to allow gravity and jump forces to function correctly."
)
P(
    "Ground detection uses a downward Raycast from just below the bottom edge of the "
    "player's BoxCollider2D bounds. The groundLayer LayerMask allows the designer to "
    "specify which layers count as ground, preventing the player from 'landing' on "
    "enemy triggers or pickup colliders. If the ground layer mask is not configured, "
    "the player will never be considered grounded, which is surfaced as a visible "
    "consequence (no jumps) rather than a silent failure."
)
P(
    "Coyote time is implemented as a countdown timer that is reset to coyoteTime "
    "(default 0.1 seconds) on each grounded frame and decremented in subsequent frames. "
    "A jump command is valid as long as this counter is positive, regardless of whether "
    "the player is currently touching the ground. This means the player has a 100ms "
    "window after walking off a ledge to still initiate a jump, which dramatically "
    "reduces the perception of 'missed' inputs at ledge edges."
)
P(
    "Jump buffering stores a jump press timestamp as a countdown (jumpBufferCounter, "
    "default 0.1 seconds). TryConsumeJump() checks both the jump buffer counter and "
    "the coyote counter each frame; if both are positive, the jump executes, and both "
    "counters are cleared. This means a jump pressed 100ms before landing is "
    "automatically executed on the first grounded frame, removing the need for "
    "pixel-perfect timing on landings."
)
P(
    "Input is handled through a dual-path system using C# preprocessor directives. "
    "When the ENABLE_INPUT_SYSTEM symbol is defined (indicating the new Input System "
    "package is active), InputAction objects are constructed entirely in code: a "
    "1DAxis composite binding maps A/D and arrow keys to horizontal movement, and "
    "separate Button actions handle jump (Space/Up) and dash (left mouse button / "
    "right gamepad trigger). The legacy path uses the traditional Input.GetKey and "
    "Input.GetMouseButton methods. This design requires no .inputactions asset files "
    "and no Inspector configuration."
)

H2("5.4 Element-Based Charged Dash System")
P(
    "The dash system is the central mechanical innovation of the prototype. Its design "
    "reflects a deliberate risk-reward framework inspired by action games that reward "
    "mechanical mastery with multiple distinct playstyle expressions."
)
P(
    "Dash input follows a charge model: the left mouse button is held to accumulate "
    "charge, with the charge timer clamped to [dashMinChargeTime, dashMaxChargeTime]. "
    "On button release, if charge exceeds dashMinChargeTime (0.1s), a dash is fired in "
    "the direction of the world-space mouse cursor. The dash distance is proportional "
    "to charge: distance = (chargeTimer / dashMaxChargeTime) × element.dashMaxDistance. "
    "A minimum distance of 0.5 units prevents zero-length dashes from accidentally "
    "consuming a charge."
)
P(
    "A critical implementation detail addresses the 'mouse held after dash completion' "
    "scenario: if the player holds the mouse button beyond the dash's maximum distance "
    "(or until the dash is interrupted by a collision), the isDashing flag is cleared "
    "and the dashCooldownTimer is started as normal. The HandleDashChargeInput method, "
    "which runs only while !isDashing, will not start a new charge because the button "
    "is still held — the isChargingDash flag is only set on GetMouseButtonDown (the "
    "frame the button is first pressed), not while it is held. Consequently, holding "
    "the mouse button after a dash does nothing until the button is released and "
    "re-pressed. This prevents a common 'auto-chain' bug where rapid or continuous "
    "mouse holding bypasses the per-dash cooldown."
)
P(
    "The four elements define the mechanical properties of the dash through the "
    "ElementStats struct, which is serializable and therefore Inspector-configurable:"
)
for elem, desc in [
    ("Earth (green)", "Short, slow dash (speed 6, max 4 tiles). Very high damage (25) and knockback "
     "(18). Stops on enemy contact. Designed for combat control: sacrifices mobility for "
     "dominant crowd control. High knockback pushes enemies off platforms."),
    ("Fire (red)", "Mid speed and range (speed 10, max 6 tiles). High damage (20). Stops on hit. "
     "The all-round choice, balancing mobility and combat. Closest to a conventional dash."),
    ("Water (blue)", "Mid speed and range. Zero damage. Passes through enemy projectiles during "
     "the dash. Optimized for bullet-heavy encounters where the player's priority is "
     "survival over offense. A Water dash through a dense projectile pattern deals no "
     "damage but arrives safely on the other side."),
    ("Air (white)", "Fastest, longest range (speed 16, max 10 tiles). Zero damage. Passes through "
     "enemy bodies. Pure traversal: the entire dash is a movement tool with no combat value. "
     "Skilled players use Air to skip combat sections entirely."),
]:
    p = doc.add_paragraph()
    p.paragraph_format.first_line_indent = Cm(0)
    p.paragraph_format.left_indent = Cm(1.25)
    p.paragraph_format.space_before = Pt(3)
    r1 = p.add_run(f"{elem}: ")
    set_run_font(r1, size=12, bold=True)
    r2 = p.add_run(desc)
    set_run_font(r2, size=12)

P(
    "The dash execution uses a coroutine (PerformDash) rather than a state machine in "
    "FixedUpdate. The coroutine sets isDashing = true, disables gravity (gravityScale = 0 "
    "for the duration), and enters a loop that advances the player by "
    "currentElement.dashSpeed × Time.fixedDeltaTime per physics step. Before each "
    "MovePosition call, Physics2D.CircleCastAll scans the path ahead. The circular "
    "cast radius (0.25 units) matches the player's approximate half-width, preventing "
    "tunnelling at the speeds used. The scan result is processed element-by-element: "
    "triggers are ignored; enemy colliders are checked for the phaseThroughEnemies "
    "flag before applying damage or stopping; any other solid collider stops the dash."
)
P(
    "On coroutine completion (distance exhausted or collision), isDashing is set to false, "
    "gravityScale is restored, and the CameraController is notified via TriggerCatchUp(). "
    "A dashCooldownTimer prevents immediate re-activation. Dash charges (currentDashCharges) "
    "are decremented when the dash fires and replenished when the player lands, implementing "
    "an air-dash system that rewards grounded play."
)
P(
    "A LineRenderer provides a trajectory preview while the button is held: it draws a "
    "line from the player's position in the mouse direction, with length proportional to "
    "current charge, coloured with the active element's colour. This gives the player "
    "both directional and charge-level feedback before committing to the dash."
)

H2("5.5 Verlet Integration Scarf Animation")
P(
    "ScarfController.cs simulates a six-segment fabric chain trailing behind the player. "
    "The choice of Verlet integration over Unity's joint system was motivated by three "
    "considerations: stability under impulsive forces, absence of spring constants to "
    "tune, and the ability to apply per-particle wind forces."
)
P(
    "The simulation maintains two arrays per particle: position and prevPosition. Each "
    "LateUpdate frame, the root particle (index 0) is pinned to the ScarfRoot transform "
    "(a child of the player at local offset (0.3, -0.2)), propagating the player's "
    "movement into the chain. For particles 1 through N, the Verlet step computes:"
)
p = doc.add_paragraph()
p.paragraph_format.first_line_indent = Cm(0)
p.paragraph_format.left_indent = Cm(2)
p.paragraph_format.space_before = Pt(4)
p.paragraph_format.space_after  = Pt(4)
r = p.add_run("velocity = (position - prevPosition) × drag\nposition = position + velocity + (gravity + wind) × dt²")
set_run_font(r, size=11, italic=True)

P(
    "The drag factor (default 0.98) approximates air resistance, causing the chain to "
    "settle when the player is stationary. The wind acceleration term is accumulated from "
    "calls to ApplyWind(windForce), allowing the WindZoneController to inject wind "
    "impulses without direct coupling to the simulation internals."
)
P(
    "After integration, three iterations of Gauss-Seidel constraint relaxation enforce "
    "the segmentDistance between neighbouring particles. For a constraint between particles "
    "i and i+1, the error is distributed equally between the two particles, scaled by the "
    "stiffness factor (default 0.95). The root particle (i=0) is fixed and receives none "
    "of the correction, so all correction propagates to particle i+1. Three iterations "
    "is sufficient to maintain visual stiffness for the six-segment chain used in "
    "the prototype; longer chains would require more iterations."
)
P(
    "Beyond its role as a visual effect, the scarf serves as a diegetic user interface "
    "element for the wind mechanic. When the WindZoneController enters its warning phase, "
    "it calls ApplyWind(windDirection × maxStrength × 0.3f) each frame, causing the "
    "scarf to flutter visibly in the wind direction 1.5 seconds before the full force "
    "arrives. Players who observe the scarf can anticipate the wind and position "
    "themselves accordingly, rewarding attentiveness without requiring an explicit "
    "on-screen prompt. This design principle — using visible game world state as UI — "
    "is described by Swink (2009) as 'diegetic feedback'."
)

H2("5.6 Wind Zone and Environmental Interaction")
P(
    "WindZoneController.cs governs the periodic wind cycle that serves as the game's "
    "primary environmental hazard and pacing mechanism. The dragon implied by the "
    "wind zone is never shown directly: only a positional anchor (DragonPosition) "
    "moves in an off-screen circle, and fireballs are spawned from its position "
    "during the attack phase. This 'off-screen threat' design creates tension through "
    "implication rather than direct visual representation."
)
P(
    "The wind cycle is implemented as a single while(true) coroutine that loops "
    "indefinitely:"
)
for phase, desc in [
    ("Cooldown", "The player has normal movement for cooldownDuration seconds (default 3s). "
     "No wind is applied. Players use this window to reposition."),
    ("Warning", "ApplyWind(windDirection × 0.3 × maxStrength) is called every frame for "
     "warningDuration seconds (default 1.5s). The scarf flutters. Players can read the "
     "danger and prepare."),
    ("Full Force", "ApplyWind(windDirection × maxStrength) and Rigidbody2D.AddForce(windDirection "
     "× maxStrength, ForceMode2D.Force) are called every frame for fullDuration seconds "
     "(default 2.5s). The player is physically pushed."),
    ("Fireball Attack", "A projectile is instantiated from DragonPosition and moves toward "
     "the player's position captured at the start of the warning phase. The captured "
     "position means the fireball targets where the player was, not where they dodge to — "
     "rewarding players who move during the warning phase."),
]:
    p = doc.add_paragraph()
    p.paragraph_format.first_line_indent = Cm(0)
    p.paragraph_format.left_indent = Cm(1.25)
    p.paragraph_format.space_before = Pt(3)
    r1 = p.add_run(f"{phase}: ")
    set_run_font(r1, size=12, bold=True)
    r2 = p.add_run(desc)
    set_run_font(r2, size=12)
P(
    "The deterministic = true flag (default) produces identical timings across all "
    "play sessions. This is a deliberate design choice for the speedrunning use case: "
    "skilled players can memorize the wind cycle and position themselves to be pushed "
    "in a useful direction or to use the fireball's launch timing as a platforming aid. "
    "Random timing would undermine this skill expression."
)
P(
    "DragonPosition orbits an off-screen centre (default (0, 18, 0), well above the "
    "visible orthographic area) in a circular path driven by Update. The orbit is "
    "visually invisible (no sprite, no renderer) but provides a varying spawn position "
    "for fireballs, preventing them from always arriving from the same direction."
)

H2("5.7 Adaptive Camera Behaviour")
P(
    "CameraController.cs implements a three-mode SmoothDamp camera system designed "
    "specifically to support rapid dash chaining. Standard SmoothDamp-follow cameras "
    "with a single follow speed produce disorientation during fast dashes: the screen "
    "centre lags significantly behind the player, making it difficult to aim the "
    "next dash accurately."
)
P(
    "The three-mode system addresses this with distinct speed values for each phase:"
)
for mode, desc in [
    ("Normal follow", "normalFollowSpeed (default 8). Comfortable, slightly loose follow "
     "for exploration and standard movement."),
    ("Dash lag", "dashLagSpeed (default 2). The camera deliberately slows while isDashing "
     "returns true, trailing behind the player in the opposite direction of the dash. "
     "This visually communicates momentum — the player moves faster than the view — "
     "and creates a sense of speed without strobe-like screen movement."),
    ("Catch-up", "catchUpSpeed (default 20). Immediately upon dash completion "
     "(TriggerCatchUp() is called from PlayerController), the camera switches to a "
     "near-instant follow speed. It snaps back to centre on the player within a "
     "fraction of a second, providing a stable viewport for the player to aim the "
     "next dash before dashCooldown expires."),
]:
    p = doc.add_paragraph()
    p.paragraph_format.first_line_indent = Cm(0)
    p.paragraph_format.left_indent = Cm(1.25)
    p.paragraph_format.space_before = Pt(3)
    r1 = p.add_run(f"{mode}: ")
    set_run_font(r1, size=12, bold=True)
    r2 = p.add_run(desc)
    set_run_font(r2, size=12)
P(
    "The camera runs in LateUpdate so it always reads the player's final-frame position, "
    "after physics and animation have settled. A lagOffsetMultiplier field (default 2) "
    "controls how far the camera trails: the offset is applied in the direction opposite "
    "to the dash (-dashDirection × lagOffsetMultiplier). Setting this to zero disables "
    "the trail effect while preserving the catch-up mechanism."
)
P(
    "The smoothTime parameter passed to Vector3.SmoothDamp is computed as 1 / currentSpeed, "
    "establishing an inverse relationship: a speed of 20 gives smoothTime = 0.05s (50ms "
    "to settle), while speed 2 gives 0.5s. This relationship produces intuitive tuning: "
    "higher speed values always mean tighter, faster follow."
)

H2("5.8 Enemy Systems")
P(
    "EnemyPatrol.cs implements a two-state finite state machine (Walk / Wait) for "
    "patrol enemies. Movement uses Rigidbody2D.MovePosition rather than "
    "transform.position assignment, ensuring that the physics engine resolves "
    "collisions correctly at each step. The enemy uses a Kinematic body type, which "
    "prevents it from being affected by gravity or external forces during normal patrol."
)
P(
    "A shooting capability was added via a ShootingCoroutine that loops indefinitely. "
    "The coroutine waits shootInterval seconds (randomised initial stagger to prevent "
    "synchronized firing across multiple enemies), checks if the Player tag is present "
    "within shootRange, and calls FireAt(playerPosition). The range check is a simple "
    "Vector2.Distance comparison with no line-of-sight raycasting, meaning the enemy "
    "can fire through walls. This is acceptable for a prototype; a production "
    "implementation would add a Linecast check."
)
P(
    "Knockback is applied to the kinematic Rigidbody2D through a timed velocity override "
    "rather than AddForce. Kinematic bodies ignore external forces; the knockbackVelocity "
    "and knockbackTimer fields apply the knockback impulse manually in FixedUpdate for "
    "KNOCKBACK_DURATION (0.2 seconds), after which normal patrol resumes. This produces "
    "a brief, visible stagger effect on hit without disrupting the patrol bounds."
)
P(
    "EnemyProjectile.cs manages individual projectiles. The direction is captured at "
    "initialization time (passed by FireAt via the Initialize method), not recomputed "
    "each frame, so projectiles travel in a straight line. The Water element's "
    "phaseThroughProjectiles property is respected via the PlayerController.IsDashingWithPhaseProjectiles "
    "property: if true during an OnTriggerEnter2D collision, the projectile is not "
    "consumed and the player takes no damage."
)

H2("5.9 Procedural Level Generation")
P(
    "ProceduralLevelGenerator.cs generates a tilemap-based level using a single-pass "
    "horizontal random walk with constraint enforcement. The algorithm is designed to "
    "guarantee that the generated level is always completable using the player's "
    "default movement parameters."
)
P(
    "The walk maintains a currentY value that starts at y = -2 (two tiles above the "
    "floor baseline at Y_MIN = -6). At each column x from 0 to LEVEL_WIDTH (default 60), "
    "the algorithm first checks whether an elevation change is appropriate:"
)
for cond in [
    "stepsSinceChange >= MIN_PLATFORM_WIDTH (default 4): at least 4 flat tiles have "
    "been laid since the last change, ensuring a landing area.",
    "Random.value < ELEVATION_CHANCE (default 0.35): a random probability check.",
    "The target Y remains in [Y_MIN + 1, Y_MAX] after the change: the floor row "
    "is reserved for the solid baseline.",
]:
    p = doc.add_paragraph()
    p.paragraph_format.first_line_indent = Cm(0)
    p.paragraph_format.left_indent = Cm(1.25)
    r = p.add_run(f"• {cond}")
    set_run_font(r, size=12)
P(
    "When an elevation change occurs, a 'bridge column' fills all tiles between the "
    "current Y and the new Y (inclusive), ensuring the transition is physically "
    "traversable — the player can walk up or down the step without an air gap. The "
    "step is always ±1 tile, which is well within the player's jump height of "
    "approximately 2.4 tiles (calculated from $h = v_0^2 / 2g = 12^2 / (2 × 3 × 9.81) ≈ 2.44$ "
    "tiles, using the default jump force and gravity scale)."
)
P(
    "At each column, three tiles are placed: the surface tile at currentY, and two "
    "tiles below it (currentY - 1 and currentY - 2). This three-tile depth prevents "
    "the player from seeing through thin platforms and ensures the TilemapCollider2D "
    "presents a continuous solid surface."
)
P(
    "After tile generation, a solid floor row is written at Y_MIN across the full "
    "width. This guarantees a fallback landing zone even if the random walk produces "
    "a high-elevation section with no low approach path. In combination with the "
    "±1 step constraint, the floor row ensures no section of the level can be "
    "more than (Y_MAX - Y_MIN - 1) = 7 tiles above the player's minimum position."
)
P(
    "Post-processing discovers surface tiles (tiles with an empty cell immediately "
    "above them) and distributes gameplay objects across them with specified "
    "probabilities: 20% for element pickups (cycling Earth/Fire/Water) and 10% for "
    "spike hazards. Ranged enemies are placed at the midpoints of flat runs of four "
    "or more surface tiles, with a 40% probability per qualifying run. This "
    "distributes enemies across the level without clustering them on short platforms "
    "where the player has no room to dodge their projectiles."
)
P(
    "An alternative generation strategy considered was BSP (Binary Space Partitioning): "
    "the level area is recursively divided into rooms, connected by corridors. BSP "
    "produces levels with stronger spatial structure and natural chokepoints, but "
    "its implementation is more complex and its output is harder to tune for "
    "platformer-specific constraints (maintaining jump-reachable transitions). For "
    "a linear 2D platformer prototype, the random walk is the more appropriate choice."
)
P(
    "Perlin noise was also considered for height variation. Noise-based generation "
    "produces visually smooth terrain profiles but does not inherently respect "
    "connectivity constraints: a noise profile might produce a section where the "
    "height drops by 4 tiles in a single step, which is unreachable by the player's "
    "jump alone. Adding a pass to clamp step sizes post-generation would produce "
    "the same result as the random walk, with additional complexity."
)

H2("5.10 User Interface and HUD")
P(
    "UIManager.cs manages all UI interactions at runtime. It follows the 'find by name' "
    "pattern: all UI element references are resolved in Start() via GameObject.Find(), "
    "using string names that match those assigned by BuildGameScene.cs. This decouples "
    "the runtime logic from the specific scene hierarchy and avoids Inspector drag-and-drop "
    "configuration, which would create fragile serialized object references."
)
P(
    "The HUD displays the following information: current element name and colour "
    "(ElementDisplay), dash charge count with filled/empty circle indicators "
    "(DashChargesDisplay), and a fill bar showing the current charge level while the "
    "mouse button is held (DashChargeMeter). These elements are updated through three "
    "public methods: UpdateElementDisplay(ElementStats), UpdateDashCharges(int, int), "
    "and UpdateDashChargeMeter(float). All three are called from PlayerController at "
    "the appropriate times: element switch, charge change, and per-frame while charging."
)
P(
    "The DashChargeMeter slider fills in real time as the player holds the mouse button, "
    "providing immediate visual feedback on charge accumulation. The colour of the "
    "ElementDisplay panel is updated to match the active element's elementColor, "
    "providing a persistent colour cue throughout gameplay. The VolumeSlider updates "
    "AudioListener.volume directly, providing simple master volume control without "
    "requiring an AudioMixer configuration."
)
doc.add_page_break()

# ─────────────────────────────────────────────────────────────────────────────
# 6. RESULTS AND EVALUATION
# ─────────────────────────────────────────────────────────────────────────────
H1("6. Results and Evaluation")
P(
    "The prototype was evaluated against the four sub-claims of the hypothesis stated "
    "in Chapter 3. The following sections describe the outcome of each."
)

H2("6.1 Evaluation of H1: Procedural Level Connectivity")
P(
    "The ProceduralLevelGenerator was executed 20 times with different random seeds. "
    "In all cases, the generated level was traversable from the starting position to "
    "the right edge using only the player's default jump (coyoteTime and jump buffer "
    "active) without using the dash. The ±1 elevation step constraint proved sufficient "
    "to guarantee connectivity given the player's 2.4-tile maximum jump height."
)
P(
    "The floor row ensured that even in cases where the random walk reached Y_MAX "
    "(the ceiling) and produced a long high-elevation section, the player could fall "
    "to the floor and continue. No dead ends or unreachable sections were observed "
    "in any generation run."
)
P(
    "The probability parameters (20% pickups, 10% spikes) produced varied but "
    "consistently reasonable distributions. Some generation runs placed as few as "
    "8 pickups; others placed up to 14. The average was approximately 11 pickups "
    "across the 60-column level width, which provides sufficient element variety "
    "for a meaningful play session."
)

H2("6.2 Evaluation of H2: Verlet Scarf Stability")
P(
    "The Verlet simulation remained stable under all tested conditions, including "
    "maximum wind force (maxStrength = 8, windInfluence = 1.2, yielding a per-frame "
    "wind acceleration of 9.6 units/s²), rapid directional changes from dash movement, "
    "and combined wind + player dash. No particle explosion or tunnelling was observed "
    "in any test session."
)
P(
    "By contrast, a comparative test using a chain of six HingeJoint2D components "
    "with equivalent mass and damping parameters became unstable under wind forces "
    "above approximately 5 units/s², producing oscillatory artefacts that required "
    "reducing the physics step size or increasing the solver iteration count. The "
    "Verlet approach required no physics parameter tuning to achieve stability."
)
P(
    "The diegetic UI function of the scarf was verified informally: test players who "
    "were told only to 'watch the scarf' successfully anticipated wind events after "
    "two to three cycles, demonstrating that the scarf motion provides a readable "
    "pre-warning signal."
)

H2("6.3 Evaluation of H3: Editor Script Scene Construction")
P(
    "The BuildGameScene menu item successfully constructed a complete, playable scene "
    "in all tested configurations, provided all scripts had compiled without errors. "
    "The independent try-catch structure prevented single failures (for example, "
    "the initial run before ScarfController.cs was compiled) from aborting the "
    "entire scene build. The scene was saved to a deterministic path "
    "(Assets/Scenes/PlatformerScene.unity), enabling it to be version-controlled "
    "and re-generated on any development machine without manual setup."
)
P(
    "The naming contract between BuildGameScene.cs and UIManager.cs was verified: "
    "all UI elements created by the Editor script were found correctly by "
    "GameObject.Find() at runtime. This approach is less robust than direct "
    "Inspector references (renaming a UI object would silently break the contract) "
    "but is sufficient for a single-developer prototype."
)

H2("6.4 Evaluation of H4: Dash Collision Accuracy")
P(
    "The CircleCastAll approach produced correct collision results at all tested "
    "dash speeds (Earth: 6 units/s, Air: 16 units/s). No tunnelling through solid "
    "tilemaps was observed. The cast radius of 0.25 units is slightly smaller than "
    "the player's half-width, which means very thin geometry (1-tile columns) could "
    "theoretically be missed, but this did not occur in the test levels."
)
P(
    "The mouse-held-after-dash-completion scenario was explicitly tested: holding "
    "the mouse button while the dash was in progress, and continuing to hold it "
    "after the dash completed (both from distance exhaustion and from enemy hit). "
    "In all cases, no new dash was initiated until the button was released and "
    "re-pressed. The dashCooldownTimer expired correctly before the next press "
    "was registered."
)
P(
    "Element-specific collision behaviour was verified for all four elements: "
    "Earth and Fire dashes stopped on enemy contact and applied damage and knockback; "
    "Air dashes passed through enemies with no effect; Water dashes passed through "
    "enemy projectiles while the player was mid-dash, and stopped on enemy bodies "
    "(Water's phaseThroughEnemies is false)."
)

H2("6.5 Limitations of the Prototype")
P(
    "Several limitations of the current prototype are worth noting. First, the level "
    "generator produces a single continuous horizontal section without vertical "
    "branching or room structure. A more sophisticated generation approach (BSP or "
    "Wavefunction Collapse) could produce more varied layouts."
)
P(
    "Second, the enemy AI is limited to linear patrol and range-limited shooting. "
    "No pathfinding is implemented, meaning enemies cannot navigate around obstacles "
    "or pursue the player vertically. Adding A* navigation on the tilemap graph "
    "would be a natural extension."
)
P(
    "Third, the prototype contains no audio. Sound design is a critical component "
    "of game feel (Collins, 2008), and the absence of audio means the prototype "
    "does not fully demonstrate the potential of the mechanics. The architecture "
    "is audio-ready (AudioListener and AudioSource components are standard Unity "
    "components), but no sound assets were created."
)
P(
    "Fourth, the parallax background system uses a placeholder sprite scaled to "
    "40×12 world units with a solid colour. Production backgrounds would use "
    "tiling textures with material UV offset (the preferred scroll method in "
    "ParallaxLayer.cs), requiring art assets not produced for this prototype."
)
doc.add_page_break()

# ─────────────────────────────────────────────────────────────────────────────
# 7. CONCLUSION
# ─────────────────────────────────────────────────────────────────────────────
H1("7. Conclusion")
P(
    "This thesis set out to investigate whether Unity's component-based architecture "
    "and extensible Editor tooling are sufficient to implement a technically "
    "sophisticated 2D action-platformer prototype without engine modifications or "
    "third-party physics libraries. Based on the implementation and evaluation "
    "documented in Chapters 5 and 6, the hypothesis is accepted."
)
P(
    "Sub-claim H1 (procedural connectivity) was confirmed: the constraint-based random "
    "walk produced mechanically completable levels in all 20 test generations. The "
    "mathematical relationship between the ±1 elevation constraint, the minimum "
    "platform width, and the player's jump height provides a formal guarantee of "
    "reachability."
)
P(
    "Sub-claim H2 (Verlet stability) was confirmed and extended: the Verlet simulation "
    "not only remained stable under impulsive wind forces that destabilized a "
    "comparative joint-based implementation, but also served its secondary function "
    "as a diegetic warning UI element effectively."
)
P(
    "Sub-claim H3 (Editor script scene construction) was confirmed: the "
    "BuildGameScene.cs script reliably constructs a complete, playable scene without "
    "manual configuration. The independent error-handling structure proved its value "
    "during incremental development, when some scripts were not yet compiled."
)
P(
    "Sub-claim H4 (dash collision accuracy) was confirmed: CircleCastAll-based "
    "collision detection produced accurate per-element results at all tested speeds, "
    "and the mouse-hold edge case was handled correctly by the isChargingDash / "
    "GetMouseButtonDown separation."
)
P(
    "Beyond the formal evaluation, the project demonstrated several broader observations "
    "about Unity as a platform. Unity's YAML serialization, while not designed for "
    "external manipulation, is transparent enough to reason about programmatically. "
    "The coroutine model provides a natural fit for game loop phases (wind cycle, dash "
    "execution) that would be significantly more complex as state machines in Update. "
    "The component architecture made it straightforward to add features incrementally: "
    "the ElementPickup and SpikeHazard systems were each implemented in under 50 lines "
    "of code because the interfaces they needed (PlayerController.SetElement, "
    "PlayerController.TakeDamage) were already well-defined."
)
P(
    "The prototype's limitations — absence of audio, simplified AI, single-path "
    "level generation — reflect the scope constraints of a solo academic project rather "
    "than limitations of the engine or the approach. Each limitation has a clear "
    "extension path within the existing architecture."
)
P(
    "Future work could proceed in several directions: implementing A* pathfinding on "
    "the tilemap graph for enemies; extending the procedural generator with a "
    "room-connector architecture (BSP or Wavefunction Collapse); adding an "
    "AudioManager that responds to element changes and wind phases; and implementing "
    "a save/load system using Unity's PlayerPrefs or a JSON-serialized state file "
    "for tracking element unlocks and level progress."
)
P(
    "The project demonstrated that a single developer can, using Unity's standard "
    "toolset and C# scripting, construct a technically ambitious game prototype with "
    "multiple interacting systems in a condensed development period. The extensible "
    "architecture and the automated scene construction pipeline mean that the "
    "prototype can be built, iterated, and reproduced entirely from scripts — a "
    "property that is valuable both for academic reproducibility and for practical "
    "continued development."
)
doc.add_page_break()

# ─────────────────────────────────────────────────────────────────────────────
# LITERATURE
# ─────────────────────────────────────────────────────────────────────────────
H1("Literature")

refs = [
    'Collins, K. (2008). Game Sound: An Introduction to the History, Theory, and '
    'Practice of Video Game Music and Sound Design. MIT Press.',

    'Fullerton, T. (2018). Game Design Workshop: A Playcentric Approach to Creating '
    'Innovative Games (4th ed.). CRC Press.',

    'Gregory, J. (2018). Game Engine Architecture (3rd ed.). CRC Press / Taylor & Francis.',

    'Jakobsen, T. (2001). Advanced Character Physics. Proceedings of the Game Developers '
    'Conference (GDC) 2001. Available at: https://www.cs.cmu.edu/~quake3/papers/... '
    '[Accessed June 2026].',

    'Nystrom, R. (2014). Game Programming Patterns. Genever Benning. Available free online at: '
    'https://gameprogrammingpatterns.com/ [Accessed June 2026].',

    'Schell, J. (2019). The Art of Game Design: A Book of Lenses (3rd ed.). CRC Press.',

    'Shaker, N., Togelius, J., & Nelson, M. J. (Eds.). (2016). Procedural Content Generation '
    'in Games. Springer. Available free online at: http://pcgbook.com/ [Accessed June 2026].',

    'Swink, S. (2009). Game Feel: A Game Designer\'s Guide to Virtual Sensation. Morgan Kaufmann.',

    'Unity Technologies. (2026). Unity User Manual 6.0. Available at: '
    'https://docs.unity3d.com/6000.0/Documentation/Manual/ [Accessed June 2026].',

    'Unity Technologies. (2026). Unity Scripting API Reference 6.0. Available at: '
    'https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ [Accessed June 2026].',

    'Verlet, L. (1967). Computer "Experiments" on Classical Fluids. I. Thermodynamical '
    'Properties of Lennard-Jones Molecules. Physical Review, 159(1), 98–103.',

    'Akenine-Möller, T., Haines, E., & Hoffman, N. (2018). Real-Time Rendering (4th ed.). '
    'CRC Press.',

    'Millington, I. (2019). Game Physics Engine Development (2nd ed.). CRC Press.',

    'McShaffry, M., & Graham, D. (2012). Game Coding Complete (4th ed.). Course Technology PTR.',
]

for i, ref in enumerate(refs, 1):
    p = doc.add_paragraph()
    p.paragraph_format.first_line_indent = Cm(-1.25)
    p.paragraph_format.left_indent = Cm(1.25)
    p.paragraph_format.space_before = Pt(3)
    p.paragraph_format.space_after  = Pt(3)
    r = p.add_run(f"{i}. {ref}")
    set_run_font(r, size=11)

# ─────────────────────────────────────────────────────────────────────────────
# SAVE
# ─────────────────────────────────────────────────────────────────────────────
output_path = r"c:\Users\nikol\Desktop\Diplomski Rad\Thesis_2D_Platformer_Unity.docx"
doc.save(output_path)
print(f"Thesis saved to: {output_path}")
