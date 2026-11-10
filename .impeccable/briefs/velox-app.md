# Surface brief: Velox (whole desktop app, WPF)

Scope: every page of the Velox window (src/Velox/UI). Mode: Operate.
Audience: Windows PC gamers at their desk, evening, low light. Two equal profiles: the average player
(Overview + "Apply recommended") and the enthusiast (Optimizations, every value visible).
Job: get the PC to the recommended setup and always be able to go back.
Proof: Velox backs up every original value, shows the real Windows state, ships no placebo tweaks.
Constraints: single .exe, Windows-installed fonts only (Bahnschrift, Segoe UI Variable, Segoe Fluent Icons);
.NET Framework 4.8 WPF; English + Spanish; standard Windows controls; keep every feature.
Anti-goals (user): no neon/RGB glow, no aggressive angular shapes, no "BOOST"/+FPS claims, nothing that looks like a game HUD.
Open decision: two readable state labels per optimization (Windows default -> Velox), EN + ES; BASE comes from the
backup when one exists; raw values only where Velox really reads them.

## Direction contract

THESIS: The PC has a baseline (your original values) and a setup (what Velox changes), like a race car's setup sheet
in the pits. Refuses the category default: a "% optimized" score ring on a navy/neon-gradient dashboard.

OWN-WORLD: Warm graphite ground #101113, surfaces #17191C/#1D1F23, paper-white text #F2F1EC, hairline rules
#2A2D32. Signal orange #FF5A1F only on action buttons and the active-nav tick. State colours with one meaning each:
green applied, amber pending, red risk. No gradients. Bahnschrift SemiCondensed for headings, column heads and every
figure (tabular); Segoe UI Variable for prose. Ruled rows over boxed cards; radii 6-10px.

STORY: The visitor reads what Velox would change, from what to what, applies it in one click, and sees at all times
how many changes are saved and how to return to base.

FIRST VIEWPORT: Overview = a setup-sheet header (CPU, GPU, RAM, drive in Bahnschrift figures) over a ruled table of
recommended changes with SETTING | BASE -> VELOX columns; "Apply N" in orange at the table foot; a persistent line
"N changes saved - Back to base" beside it. Sidebar left, live monitor below it, as today.

FORM: Racing setup sheet (position 1 of my ordered list). seed: none (unavailable)
Signature interaction: the BASE -> VELOX delta on every optimization row and on Overview; pending rows show the delta
in amber until applied.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
