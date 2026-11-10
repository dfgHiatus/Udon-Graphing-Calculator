# Graphing Calculator for VRChat (Udon)

A Desmos-style graphing calculator for VRChat worlds, written in UdonSharp. All math (parsing, evaluation, calculus, regression, curve and region sampling) runs inside Udon; shaders only draw what Udon computes.

- Unity 2022.3.22f1, VRChat Worlds SDK 3.10.6 (UdonSharp 1.x included)
- PC and Quest/Android
- No external libraries and no network calls

---

## Contents

1. [Quick start](#quick-start)
2. [Using the calculator](#using-the-calculator)
3. [Expression syntax reference](#expression-syntax-reference)
4. [Multiplayer behaviour](#multiplayer-behaviour)
5. [Configuration and performance](#configuration-and-performance)
6. [How it works](#how-it-works)
7. [File layout](#file-layout)
8. [Extending the calculator](#extending-the-calculator)
9. [Known limitations](#known-limitations)
10. [Troubleshooting](#troubleshooting)
11. [Project status](#project-status)

---

## Quick start

### Install with the VRChat Creator Companion (VCC)

1. Open **https://dfghiatus.github.io/Udon-Graphing-Calculator/** and click **Add to VCC**.
   Or, in VCC: *Settings → Packages → Add Repository* and paste
   `https://dfghiatus.github.io/Udon-Graphing-Calculator/index.json`.
2. In your world project, choose *Manage Project* and add **Udon Graphing Calculator**.

The package is `com.dfghiatus.udon-graphing-calculator`. It requires VRChat Worlds SDK 3.10 or newer and Unity 2022.3.

### Add it to a scene

1. **Import TextMesh Pro essentials** if you haven't yet: *Window → TextMeshPro → Import TMP Essential Resources*.
2. Either:
   - drag `Packages/Udon Graphing Calculator/Runtime/GraphingCalculator.prefab` into your scene, or
   - run **Tools → Graphing Calculator → Build Calculator In Scene**, which creates a fresh `GraphingCalculator` at (0, 0, 3).
3. The calculator faces −Z, toward a player at the default spawn. Move or rotate the root object as you like; everything scales with it.
4. Save the scene, then test with **Play** (ClientSim) or *Build & Test*.

Running the builder again creates a second calculator, so delete the old one first if you rebuild.

The wall is about 3.1 m wide and 2.5 m tall:

| Part | Position (root-local) | Size |
|---|---|---|
| Graph board | centre (0.5, 1.95) | 2.0 × 1.5 m |
| Expression list | centre (−1.0, 1.95) | 0.9 × 1.5 m |
| Keypad | centre (0.02, 0.82) | 2.95 × 0.64 m |
| "How to use" info panel | centre (−2.45, 1.6), turned 25° inward | 1.1 × 1.86 m |
| "How it works" info panel | centre (2.5, 1.6), turned 25° inward | 1.1 × 1.86 m |

### Info panels

Two floating panels sit beside the calculator:

- **Left, "How to use":** what the calculator is, how to type, things to try, graph controls, and how sharing works. At the bottom is a **read-only text field with the GitHub URL**. VRChat worlds can't open a web browser, so players click the field and press Ctrl+C (desktop) to copy the link, or read it off the panel.
- **Right, "How it works":** a plain-language explanation of the compile → evaluate → sample → draw pipeline, time-slicing and sync.

They are plain world-space canvases with no scripts. Delete either one if you don't want it. To recreate them, select a calculator and run **Tools → Graphing Calculator → Add Info Panels To Selected Calculator**. The text lives in `GraphCalculatorBuilder.cs` (`HowToUseText`, `HowItWorksText`).

---

## Using the calculator

### Expression list (left panel)

Each row holds one expression. There are up to **16 expressions**, with 7 rows visible at a time.

| Control | What it does |
|---|---|
| Tap a row | Select it for editing (a blue caret shows the cursor) |
| Coloured circle | Show or hide that graph |
| **color** button | Cycle through the 8 colours |
| **×** | Delete the row |
| **+ expr / + table / + note** | Add a row below the selected one |
| **▲ / ▼** | Scroll the list |
| **Clear all** | Remove every expression (can be undone) |
| **+ add slider** | Appears when an expression uses an undefined variable; adds `name=1` above it |

Under each row you'll see a **result line**: the value (`= 3.14159`), a regression result, or a red error message.

**Sliders.** A row like `a=2` becomes a slider. Drag it to change the value, and every graph that depends on `a` updates. Tap the **min / max / step** labels to type new bounds; empty bounds default to ±10 (widened to fit the value). **►** animates the slider back and forth; **||** stops it and writes the current value back into the text.

**Tables.** *+ table* creates a table with columns `x_1` and `y_1`. While a table is selected, the table editor covers the list:
- Tap a cell to edit it. Enter or **↓ next** moves down, adding a row when needed.
- **+ row / + column / − column / ▲ / ▼ / Done**.
- Named headers (`x_1`, `y_1`, …) become list variables you can use elsewhere, for example `y_1 ~ m x_1 + b`.
- A header that is a formula (such as `x_1^2`) makes a computed column.
- Each column after the first is plotted as points against the first column.

**Notes.** A row that starts with `"` is plain text and isn't evaluated.

### Keypad (bottom)

Three pages, chosen with the tabs on the left:

- **123**: digits, variables, operators, brackets, comparisons, `π`, `√`, `∫`, `Σ`, `Π`, `d/dx`, common functions and cursor keys.
- **abc**: letters, Greek letters (α β γ δ ε λ μ σ φ ω ρ τ θ), cursor keys and arithmetic.
- **func**: trig and hyperbolic functions, logs, rounding, combinatorics, statistics, calculus templates, piecewise templates, complex-number functions and list helpers.

Keys that insert templates such as `sin()` put the cursor inside the brackets. **DEL** deletes one character, or a whole function name like `sin(` at once; pressing DEL on an empty row deletes the row. **CLR** clears the current field. **Enter** adds a new row. **↑ / ↓** move between rows.

**Typing with a keyboard.** The box under the tabs (*Tap to type with keyboard*) opens VRChat's keyboard. Its text replaces the selected expression. Plain-text spellings work: `pi`, `theta`, `sqrt(x)`, `<=`, `>=`, `sum(n=1,10,n^2)`.

### Graph board

| Control | What it does |
|---|---|
| Drag on the graph | Pan |
| **+ / −** | Zoom in / out (×1.5) |
| **⌂** | Reset view (centre 0,0, x from −10 to 10) |
| **C off / C on** | Complex mode |
| **RAD / DEG** | Angle mode for trig functions |
| **Grid** | Hide or show grid lines (the axes stay) |
| **Unlocked / Locked** | Lock editing to the current owner |
| **Undo / Redo** | Up to 30 steps |

The status line at the bottom left shows who is editing and *computing…* while graphs are being resampled.

---

## Expression syntax reference

The syntax follows Desmos as closely as plain-text input allows.

### Numbers, variables and constants

- Numbers: `3`, `2.5`, `.5`. Scientific notation isn't supported, because `e` is Euler's number.
- Variables are **single letters** with an optional subscript: `a`, `b_1`, `x_{12}`, `θ`, `α`.
  Multi-letter text is read as a product: `ab` = `a·b`. The exceptions are function names and the words `pi`, `tau`, `theta`, `infinity` and Greek letter names.
- Constants: `π` (`pi`), `e`, `τ` (= 2π), `∞` (`infinity`), and `i` in complex mode.
- Reserved for graphing: `x`, `y`, `t` (parametric), `θ` and `r` (polar).

### Operators

| Syntax | Meaning |
|---|---|
| `+ - * /` | Arithmetic (`·`, `×`, `÷`, `−` also accepted) |
| `^` | Power, right-associative: `2^3^2` = 2⁹ |
| `2x`, `3(x+1)`, `x sin(x)` | Implicit multiplication |
| `!` | Factorial (gamma function for non-integers) |
| `\|x\|` | Absolute value |
| `( )`, `{ }` | Grouping: `e^{-x}` is the same as `e^(-x)` |
| `<  >  <=  >=  =` | Comparisons (`≤`, `≥` also accepted); chains like `0<x<2` work |

`-2^2` = −4. `1/2x` = (1/2)·x. Negative bases with odd-root exponents give real results: `(-8)^(1/3)` = −2.

### Functions

| Category | Functions |
|---|---|
| Trig | `sin cos tan csc sec cot` |
| Inverse trig | `arcsin arccos arctan arccsc arcsec arccot` (also `asin`, …), and `sin^-1(x)` |
| Hyperbolic | `sinh cosh tanh csch sech coth arcsinh arccosh arctanh` |
| Roots, exp, logs | `sqrt` or `√`, `cbrt`, `nthroot(x, n)`, `exp`, `ln`, `log` (base 10), `log_b(x)` (e.g. `log_2(8)`), `log(x, b)` |
| Rounding | `floor ceil round` (`round(x, digits)` too), `abs sign mod(a, b)` |
| Two-argument | `arctan(y, x)`, `nCr(n, r)`, `nPr(n, r)` |
| Lists / statistics | `total mean median min max length stdev var gcd lcm`. They take a list or several arguments: `max(3, 7, 2)` |
| Special | `erf` |
| Complex | `real imag conj arg` (and `abs` = modulus) |

You can write function powers like `sin^2(x)`, and trig/log functions accept arguments without brackets: `sin 2x` = sin(2x).

### Defining things

| Example | Result |
|---|---|
| `a = 3` | Variable with a **slider** |
| `b = 2a + 1` | Computed variable (its value is shown) |
| `f(x) = x^2 + 1` | Function. Multiple parameters work: `g(x, y) = xy` |
| `L = [1, 2, 3]` | List variable |
| `P = (3, 4)` | Point variable (plotted). Use `P.x`, `P.y` |

Definitions can come in any order. Circular definitions are reported as errors. Definitions can't use `x` or `y`; use a function instead.

### Graph types

| You type | It draws |
|---|---|
| `y = x^2`, or just `x^2` | Explicit curve y = f(x) |
| `x = y^2 + 1` | Explicit curve x = g(y) |
| `y < x`, `y >= x^2 - 2` | Explicit inequality: dashed boundary for `<`/`>`, solid for `<=`/`>=`, shaded region |
| `x^2 + y^2 = 4`, `sin(x) = cos(y)` | Implicit curve |
| `x^2 + y^2 < 9` | Implicit inequality region |
| `0 < x < 2` | Region from a compound condition (shading only) |
| `r = 1 + cos(θ)`, `r < 2` | Polar curve or region (θ from 0 to 12π by default) |
| `(cos(t), sin(t))` | Parametric curve (t from 0 to 1 by default) |
| `(1, 2)`, `[(1,1), (2,4)]`, `(L, L^2)` | Points |
| `1 + 2` | Value only (shown as `= 3`) |

**Domains and restrictions.** Add braces after an expression:
- `y = x^2 {0 < x < 1}` restricts the graph.
- `(cos(t), sin(t)) {0 <= t <= 2π}` and `r = θ {0 <= θ <= 4π}` set the parameter range.

**Polar inside implicit equations.** `r` and `θ` can be mixed with `x` and `y`, for example `r < 2 + sin(5θ)`. There `θ` is the angle from `atan2`, so only one turn is drawn.

### Piecewise

```
{x < 0: -x, x}             // -x when x<0, otherwise x
{x < 0: -1, x < 1: 0, 1}   // chained conditions
{x > 0: 1}                 // undefined where the condition is false
```

### Lists

```
[1, 2, 3]       [1...10]       [1, 3, ..., 11]
L[2]            (lists are 1-indexed)
y = [1, 2, 3]x  (one curve per element: broadcasting)
total(L), mean([1...10]), max(L)
```

Lists can't contain lists. List filters (`L[L>2]`) and comprehensions (`for`) aren't supported.

### Calculus

| Syntax | Notes |
|---|---|
| `d/dx(x^3)`, `d/dt(...)` | Numerical derivative (five-point stencil) |
| `f'(x)`, `f''(x)` | Derivatives of user functions |
| `∫_0^1 x^2 dx`, `∫_{a}^{b} f(t) dt` | Numerical integral (composite Gauss–Legendre). Infinite bounds work: `∫_0^∞ e^{-x} dx` |
| `Σ_{n=1}^{10} n^2`, `sum(n=1, 10, n^2)` | Sum (up to 20,000 terms) |
| `Π_{n=1}^{5} n`, `prod(...)` | Product |

The body of `Σ`, `Π` and `d/dx` extends over the current product term: `Σ_{n=1}^{3} n + 1` = (Σ n) + 1. The integral body runs up to the `dx`.

Accuracy (measured): `f''(3)` for x²+1 gives 2; `∫₀^∞ e^{-x}dx` = 0.9999999997; `∫ e^{-x²}` over ℝ = 1.7724543 (exact value 1.7724539).

### Regression

```
y_1 ~ m x_1 + b
y_1 ~ a e^{k x_1}
```

Any undefined names on either side become fitted parameters, found with Levenberg–Marquardt (up to 8 parameters and 2048 rows). The result line shows the parameter values and R². Fitted parameters can be used in other expressions, and the model is plotted as a curve.

### Complex mode (C on)

- `i` is defined. `√(-1)` = `i`, `ln(-1)` = `πi`, `e^(iπ)` = −1, `(1+2i)(3-i)` = 5 + 5i.
- Values that aren't real are plotted as points (re, im).
- Curves are drawn only where the value is real.
- Comparisons use real parts; non-real values count as false.

### Degree mode (DEG)

Trig functions take degrees and inverse trig functions return degrees. In polar graphs, θ is in degrees, including domain bounds.

---

## Multiplayer behaviour

- The calculator uses **manual sync**. Expressions, slider bounds, colours, visibility, view, settings and animation state are shared with everyone in the instance, and late joiners receive the current state.
- **Anyone can edit.** Pressing a key, dragging the graph or moving a slider takes ownership. If two people edit at once, the last write wins.
- **Lock** restricts editing to the current owner. Others see "Locked by …".
- Each client computes graphs itself. Only text and a few numbers go over the network, at most every 0.2 s while editing.
- Slider value changes take a fast path: no recompile, just re-evaluation of what depends on them.
- Animated sliders are driven by server time, so they stay in step on every client without extra network traffic.
- The cursor position, selected row, list scroll and keypad page are local to each player.

---

## Configuration and performance

Udon runs code far more slowly than normal C#, so plotting is **time-sliced**: each frame, the engine samples graphs for a fixed budget, then continues next frame. Graphs refine progressively.

Settings in the Inspector:

| Component | Field | PC default | Quest default |
|---|---|---|---|
| GraphCalculator | Work Budget Ms / Quest Work Budget Ms | 3 ms | 2 ms |
| GCEngine | Base Samples / Quest Base Samples | 384 | 224 |
| GCEngine | Max Curve Points / Quest Max Curve Points | 3000 | 1600 |
| GCEngine | Implicit Res / Quest Implicit Res (grid columns, max 255) | 160 | 96 |
| GCEngine | Refine Levels | 5 | 5 |

Quest values are chosen automatically on Android builds.

Tips:
- Higher budgets mean faster updates and lower frame rate. Lower budgets mean smoother frames and slower refinement.
- Implicit equations are the most expensive: one evaluation per grid node, e.g. 161 × 121 on PC.
- Panning reuses existing geometry, since curves are sampled two view-widths wide and shader uniforms move them. Resampling happens only when you leave the sampled area or zoom by more than about 1.6×.
- Sums, products and integrals multiply cost: an integral evaluates its body 30 times per point (80 with infinite bounds).

---

## How it works

```
 expression text ──► GCCompiler ──► bytecode ──► GCEngine ──► meshes / textures ──► shaders
                    (parse,         (int[] +      (vectorised   (per expression)     (draw only)
                     classify,       double[])     evaluator,
                     compile)                      sampling)
        ▲                                                │
        └──────────── GraphCalculator (UI, input, sync, view, labels) ◄──┘
```

### GraphCalculator (controller)
- Owns the expression state: text, slider bounds, colours, hidden flags, animation.
- Handles every UI event. Each button carries a `CalcKey` component that forwards its key string (e.g. `i:sin(\`)`, `enter`, `sel:3`) to `OnKey`.
- Renders expression text as TextMeshPro rich text: superscripts, subscripts, `·`, `≤`, the caret.
- Serialises state for sync, keeps undo/redo history, updates grid uniforms and axis labels, and calls `engine.Work()` every frame.
- Text edits are recompiled 0.2 s after typing pauses.

### GCCompiler (parser and compiler)
1. **Tokenizer**: longest-match keywords (`sinh` before `sin`), single-letter variables with subscripts, `d/dx`, Unicode symbols. Number parsing doesn't depend on locale.
2. **Recursive-descent parser** produces an AST stored in flat arrays, since UdonSharp has no custom classes.
3. **Classification** decides what each row is (definition, function, plot type, value, regression, table) based on its form and which free variables (x, y, t, θ, r) it uses.
4. **Dependency ordering**: definitions are compiled in topological order, and cycles are reported.
5. **Code generation**: user functions are inlined; calculus bodies, reducer arguments and list elements become inline sub-programs.

Each program starts with a header listing its list sources, used for broadcasting. Points are compiled one component at a time (x program, y program), so points never exist at runtime.

### GCEngine (evaluator and sampler)
- **Vectorised stack machine**: each instruction processes up to 256 samples at once, spreading Udon's per-instruction overhead. Constants stay scalar until they meet a vector, and constant subexpressions are computed once.
- Separate **real and complex** paths. The real path handles odd roots of negative numbers (`(-8)^(1/3)` = −2).
- **Lists**: broadcasting runs a program once per element, or vectorised over elements for data lists. Reducers (`total`, `mean`, …) and indexing evaluate their own sub-programs.
- **Definitions** are evaluated into a parameter table and a list pool, and regression fits run at the same point.
- **Curve sampler** (explicit, polar, parametric): uniform samples, then adaptive midpoint refinement where segments are long or bend sharply, plus edge-finding where the function becomes undefined. Afterwards it detects discontinuities (asymptotes, jumps) and splits the line there.
- **Implicit sampler**: evaluates F = left − right on a grid, converts it to a distance estimate F/|∇F| measured in grid cells, flags discontinuities, and uploads the result to an RGBAHalf texture.
- **Mesh building**: thick-line quads (4 vertices per segment), fill quads and point quads. Coordinates are stored relative to the sampled view, so floats stay precise at any zoom.

### Shaders (`Shaders/`)
| Shader | Purpose |
|---|---|
| `GraphGrid` | Board background, major/minor grid that fades when dense, axes |
| `GraphLine` | Anti-aliased thick segments with round caps, optional dashes |
| `GraphFill` | Translucent inequality shading |
| `GraphPoint` | Anti-aliased point dots |
| `GraphImplicit` | Bilinear-filters the distance texture, draws the zero contour and shades regions |

All shaders support single-pass stereo instancing for VR and clip to the board. They do no expression math.

---

## File layout

The calculator ships as a VPM package. In this repository it lives at `Packages/com.dfghiatus.udon-graphing-calculator/`:

```
com.dfghiatus.udon-graphing-calculator/
├── package.json                   VPM / UPM manifest (name, version, dependencies)
├── README.md                      this file
├── CHANGELOG.md
├── LICENSE.md                     Apache-2.0
├── Runtime/
│   ├── DfgHiatus.GraphingCalculator.asmdef        runtime assembly
│   ├── DfgHiatus.GraphingCalculator.USharp.asset  tells UdonSharp to compile that assembly
│   ├── GraphingCalculator.prefab  ready-made calculator with info panels
│   ├── Scripts/                   UdonSharp runtime (each .cs has a matching .asset program)
│   │   ├── GraphCalculator.cs     controller: UI, input, sync, view, sliders, tables, undo
│   │   ├── GCCompiler.cs          tokenizer, parser, classifier, bytecode compiler
│   │   ├── GCEngine.cs            evaluator, calculus, regression, samplers, mesh/texture output
│   │   └── CalcKey.cs             forwards a UI event to GraphCalculator.OnKey
│   ├── Shaders/                   GraphGrid, GraphLine, GraphFill, GraphPoint, GraphImplicit
│   └── Materials/                 used by the prefab and the builder
└── Editor/
    ├── DfgHiatus.GraphingCalculator.Editor.asmdef
    └── GraphCalculatorBuilder.cs  Tools → Graphing Calculator menu (build, info panels)
```

Repository-only files (not part of the package):

```
Assets/GraphingCalculatorDev/Editor/   development helpers (below)
.github/workflows/release.yml          builds a release zip and publishes the VPM listing
.github/scripts/build_listing.py       generates the VPM listing (index.json) from GitHub releases
.github/vpm-listing.json               listing name, id, author and URL
Website/index.html                     listing page with the "Add to VCC" button
```

### Releasing a new version

1. Bump `version` in `package.json` and add a `CHANGELOG.md` entry. Commit and push.
2. On GitHub, run the **Build Release** workflow (*Actions → Build Release → Run workflow*). It:
   - zips the package folder as `com.dfghiatus.udon-graphing-calculator-<version>.zip`,
   - creates a GitHub release tagged with the version, with the zip and `package.json` attached,
   - rebuilds `index.json` from **all** releases and deploys it with the website to GitHub Pages.
3. VCC users see the update the next time VCC refreshes the repository.

**One-time setup:** in the repository's *Settings → Pages*, set **Source** to **GitHub Actions**. Without that, the deploy step fails.

The listing can also be rebuilt on its own with the **Build VPM Listing** workflow, for example after editing or deleting a release.

### Development helpers (safe to delete)

These live in `Assets/GraphingCalculatorDev/`, are editor-only, and aren't part of the package or a world build.

- **GCDevCommands.cs** watches `Temp/gc_cmd.txt` for commands:
  - `test`: runs about 120 test expressions in C# and writes results to `Temp/gc_out.txt`.
  - `build`: runs the builder.
  - `infopanels`: adds or replaces the info panels inside the package prefab.
  - `play` / `stop`: enter or leave Play mode.
  - `shot`: renders the calculator to `Temp/gc_shot.png`.
  - `key …`: presses keys on the running calculator.

  The test suite is the quickest way to check math changes.
- **GCDumpUdonNodes.cs** writes every Udon-exposed API to `Temp/udon_nodes.txt` (delete that file to regenerate it). Use it to check whether a Unity or .NET member is available before using it in UdonSharp.

---

## Extending the calculator

**A new one-argument function** (example: `sinc`)
1. `GCCompiler.cs`: add a `F_SINC` id inside the one-argument range (raise `F_LAST1`), and register it in `InitKeywords()` with `AddKw("sinc", T_FUNC, F_SINC, "")`.
2. `GCEngine.cs`: add a case to `F1R` (real) and to `C1` (complex; otherwise it returns undefined for complex inputs).
3. Optionally add a key in `GraphCalculatorBuilder.FuncPage()`, then rebuild the calculator.

**A new two-argument function**: use an id at 50 or above, add it to `F2R` and `C2`, and register the keyword.

**A new list reducer**: use an id from 80 to 89 (see `IsReducer`), and implement it in `OpReduce`.

**UdonSharp rules to follow** (the compiler fails or misbehaves otherwise):
- Only `UdonSharpBehaviour` classes. No custom classes or structs, generics, lambdas, LINQ or try/catch.
- Mark methods that recurse, directly or indirectly, with `[RecursiveMethod]`.
- Many APIs aren't exposed. Check `Temp/udon_nodes.txt`. Pitfalls already hit in this project:
  - `long % long` and `~long` aren't available.
  - `TextMeshPro.alignment` isn't available; cast to `TMP_Text`.
  - `TextMeshPro.transform` isn't available; use `.gameObject.transform`.
- Don't use `double.ToString()` for display: it follows the player's locale (`1,5`). Use `engine.FormatNum` / `FormatFixed`.
- Use only glyphs the default font has (WGL4 set). For example `▶`, `⚠` and `ℂ` show as boxes.

---

## Known limitations

- Plain-text input only. No 2D math editor, and text isn't positioned by tapping it (use ← →).
- Up to 16 expressions. Tables have up to 6 columns and 64 rows (4 × 8 visible).
- List filters, list comprehensions, `sort`/`join`, recursive functions, 3D, images, folders and graph tracing aren't implemented.
- Implicit curves are limited by grid resolution: very thin features can disappear, and polar-implicit θ covers one turn only.
- Explicit-curve discontinuity detection is heuristic, so very steep but continuous parts may occasionally break.
- `x(t)` / `y(t)` domains must be constants. Ranges in lists (`[1...n]`) must not depend on x.
- If two players edit at exactly the same moment, the last write wins.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| Builder says TMP essentials are missing | *Window → TextMeshPro → Import TMP Essential Resources* |
| Builder says a program asset is missing | Wait for UdonSharp to finish compiling and check the Console for U# errors. Each script needs its `.asset` next to it. |
| "Method is not exposed to Udon" | You used an API Udon doesn't allow. Check `Temp/udon_nodes.txt`. |
| Graphs appear slowly | Raise *Work Budget Ms*, or lower *Base Samples* / *Implicit Res* |
| Frame rate drops while graphing | Lower *Work Budget Ms* |
| Can't type | Editing may be locked by another player (status line) |
| Symbols show as boxes | The font lacks the glyph. Use another symbol or add a fallback font in TMP Settings. |
| Graph renders in one eye only | A custom shader is missing the stereo-instancing macros. Copy the pattern from the shaders here. |

---

## Project status

### Verified
- The runtime scripts compile with UdonSharp from the package assembly (`DfgHiatus.GraphingCalculator`), with no errors.
- The C# test suite (about 120 expressions) passes: arithmetic, functions, definitions, user functions and derivatives, sums, products, integrals (including infinite bounds), lists and broadcasting, piecewise, complex mode, degree mode, every graph type, tables, regression, and error messages.
- In ClientSim Play mode the calculator ran with no Udon exceptions, and the default graphs rendered correctly with grid, axes, labels, view buttons, settings bar and keypad.
- The info panels render beside the calculator in the prefab and fit their text. The URL field is read-only.
- The VPM listing generator produces a valid `index.json` (tested against a simulated release built from this package).

### Not yet verified
- **Interactive input** (keys, sliders, panning, the table editor, the keyboard input field) and the expression panel's text in Play mode.
- **Copying from the read-only URL field** inside the VRChat client.
- **Multiplayer sync** with two or more players.
- **Real-world performance** on PC VR and Quest. The sampling settings above are estimates.
- **The GitHub workflows** haven't run on GitHub yet. Pages must be enabled first (see *Releasing a new version*).
- **Installing through VCC** from the published listing.
