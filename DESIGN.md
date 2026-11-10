---
name: Velox
description: A fast, safe and reversible Windows optimizer, drawn as a race team's setup sheet.
colors:
  graphite-ground: "#101113"
  shell-black: "#0B0C0D"
  graphite-panel: "#17191C"
  graphite-inset: "#1D1F23"
  graphite-selected: "#24262A"
  hairline-rule: "#26292D"
  graphite-edge: "#2C2F34"
  track-grey: "#2A2D32"
  paper-white: "#F2F1EC"
  text-muted: "#B0B1AB"
  text-dim: "#8E908B"
  data-fill: "#D6D4CC"
  signal-orange: "#FF5A1F"
  action-ink: "#170A04"
  applied-green: "#3FCF86"
  pending-amber: "#F4B740"
  pending-row: "#1E1A11"
  risk-red: "#FF6B66"
  risk-red-soft: "#311819"
typography:
  headline:
    fontFamily: "Bahnschrift, Segoe UI Variable Display, Segoe UI"
    fontSize: "30px"
    fontWeight: 600
    fontVariation: "SemiCondensed"
  title:
    fontFamily: "Bahnschrift, Segoe UI Variable Display, Segoe UI"
    fontSize: "18px"
    fontWeight: 600
    fontVariation: "SemiCondensed"
  figure:
    fontFamily: "Bahnschrift, Segoe UI"
    fontSize: "13.5px"
    fontWeight: 600
    fontFeature: "tnum"
    fontVariation: "SemiCondensed"
  body:
    fontFamily: "Segoe UI Variable Text, Segoe UI"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: "19px"
  label:
    fontFamily: "Bahnschrift, Segoe UI"
    fontSize: "12px"
    fontWeight: 600
rounded:
  badge: "4px"
  control: "6px"
  panel: "10px"
  switch: "12px"
spacing:
  row-y: "14px"
  panel-x: "24px"
  page-x: "32px"
components:
  button-primary:
    backgroundColor: "{colors.signal-orange}"
    textColor: "{colors.action-ink}"
    rounded: "{rounded.control}"
    padding: "10px 20px"
  button-primary-disabled:
    backgroundColor: "{colors.track-grey}"
    textColor: "{colors.text-muted}"
  button-secondary:
    backgroundColor: "{colors.graphite-inset}"
    textColor: "{colors.paper-white}"
    rounded: "{rounded.control}"
    padding: "9px 16px"
  switch-on:
    backgroundColor: "{colors.paper-white}"
    rounded: "{rounded.switch}"
    width: "44px"
    height: "24px"
  switch-off:
    backgroundColor: "{colors.track-grey}"
    rounded: "{rounded.switch}"
  panel:
    backgroundColor: "{colors.graphite-panel}"
    rounded: "{rounded.panel}"
    padding: "20px 22px"
  nav-item-active:
    backgroundColor: "{colors.graphite-selected}"
    textColor: "{colors.paper-white}"
    rounded: "{rounded.control}"
  badge-risk:
    backgroundColor: "{colors.risk-red-soft}"
    textColor: "{colors.risk-red}"
    rounded: "{rounded.badge}"
---

# Design System: Velox

## Overview

**Creative North Star: "The Setup Sheet"**

Velox reads like the setup sheet a race engineer keeps in the pits: the car's baseline on one side, the setup on the other, every change written down so it can be taken back. The PC has a *base* (the user's original values) and a *Velox setup* (what Velox changes), and the interface shows both, side by side, everywhere a setting appears. Character comes from precision, figures and ruled columns, not from decoration.

The surface is a working Windows tool first. Navigation, switches, buttons and lists are standard desktop controls; the world lends only type (Bahnschrift's DIN figures), palette (warm graphite and paper white), density (ruled rows over boxed cards) and one signature move (the BASE → VELOX delta). It is dark because it is used at a desk in the evening, in low light. It refuses the category's habits: no neon or RGB glow, no aggressive angular shapes, no "BOOST" claims, nothing that looks like a game HUD.

**Key Characteristics:**
- Warm graphite ground with paper-white text; no gradients anywhere.
- Signal orange appears only where the user acts.
- Every figure and column head is set in Bahnschrift with tabular numerals.
- Settings are rows separated by hairline rules, each carrying its BASE → VELOX delta.
- Undo is always in sight: "N changes saved · Back to base".

## Colors

A restrained graphite field with one action colour and three state colours that each mean one thing.

### Primary
- **Signal Orange** (signal-orange): the action colour. The primary button of a page, the 3px tick on the active section in the sidebar, the activity bar while Velox works, and the speed lines of the logo. Text on it is Action Ink (6.2:1).

### Neutral
- **Graphite Ground** (graphite-ground): the content field behind every page.
- **Shell Black** (shell-black): the sidebar, one step darker than the content so the shell reads as frame.
- **Graphite Panel** (graphite-panel): panels and the setup sheet itself.
- **Graphite Inset** (graphite-inset): secondary buttons, icon tiles, neutral chips, insets inside panels.
- **Graphite Selected** (graphite-selected): the active nav item and selected list rows.
- **Hairline Rule** (hairline-rule): the 1px rules between sheet rows and around column heads.
- **Paper White** (paper-white): primary text, the "on" switch track, the selected segment, checked boxes, the focus ring.
- **Text Muted / Text Dim** (text-muted, text-dim): descriptions and labels; Text Dim is the floor for any readable text (5.5:1 on panels).
- **Data Fill** (data-fill): every neutral data bar (live monitor, category progress, memory use) — data is never painted in a state colour.

### Tertiary (state)
- **Applied Green** (applied-green): a setting or result that is in effect.
- **Pending Amber** (pending-amber, with pending-row as the row tint): a change waiting for "Apply", a restart notice, a monitor value above 85 %.
- **Risk Red** (risk-red on risk-red-soft): the *Risk* badge and destructive actions.

### Named Rules
**The One Action Rule.** Signal orange marks where the user acts, nothing else. One orange button per screen; secondary actions stay graphite.

**The One Meaning Rule.** Green is applied, amber is pending, red is risk. Impact, recommendation and data are never coloured with them.

**The No Gradient Rule.** Every surface, button and bar is a flat colour.

## Typography

**Display Font:** Bahnschrift SemiCondensed (with Segoe UI Variable Display)
**Body Font:** Segoe UI Variable Text (with Segoe UI)
**Label Font:** Bahnschrift

**Character:** Bahnschrift is DIN, the lettering of road signs and engineering drawings, set semi-condensed for headings and every number; Segoe UI Variable carries the prose so long descriptions stay native and easy to read. Both ship with Windows, which keeps Velox a single executable.

### Hierarchy
- **Headline** (600, 30px): the page title.
- **Title** (600, 18px; 22px for the sheet headline): panel and category titles.
- **Figure** (600 or 400, 12.5–26px, tabular): hardware specs, live monitor values, BASE → VELOX labels, counts, sizes.
- **Body** (400, 13px, 19px line height; 12.5px for row descriptions): descriptions, capped around 720px wide.
- **Label** (600, 12px, uppercase as written): column heads, sidebar group names, spec labels.

### Named Rules
**The Figures Are DIN Rule.** Any number or value a user compares is set in Bahnschrift with tabular numerals, so columns line up.

## Layout

A fixed 248px sidebar (logo, grouped navigation, live monitor docked at the bottom) and a scrolling content column with 32px page margins and a 44px integrated title bar. The minimum window is 1060×680, and the sidebar is sized so every section fits at that height without scrolling. Pages are a vertical stack: headline and one-line explanation, then panels. Inside panels, content is a ruled list: rows with 10–14px vertical padding separated by hairline rules, the action aligned right. Spec headers use a four-column grid of label / figure / caption.

## Elevation & Depth

Flat by default. Depth comes from tonal layering (shell black → graphite ground → graphite panel → graphite inset) and 1px edges, not shadows. The one shadow in the system belongs to the modal dialog, which lifts off a dimmed overlay.

### Shadow Vocabulary
- **Dialog lift** (40px blur, 12px downward offset, 50 % black): only the confirmation dialog.

### Named Rules
**The Flat Sheet Rule.** Panels never cast shadows; a panel is separated by tone and a hairline edge.

## Shapes

Small, engineered radii: 4px on badges, 6px on buttons, inputs, nav items and rows, 10px on panels and the setup sheet, full-round only on switches. Borders are 1px hairlines. No clipped corners or diagonal cuts.

## Components

### Buttons
- **Shape:** gently squared (6px).
- **Primary:** signal orange with action-ink text, 10px × 20px padding; at most one per screen.
- **Hover / Focus:** a 6 % white wash on hover, 12 % when pressed; keyboard focus draws a 2px paper-white ring 3px outside the button.
- **Secondary:** graphite inset with a graphite edge. **Ghost:** transparent, muted text that brightens on hover. **Danger:** risk-red-soft with light red text.
- **Disabled:** primary turns track grey with muted text; all buttons drop to 45 % opacity.

### Chips
- **Recommended:** outlined (1px #6E706B) with paper-white text and a star.
- **Impact, restart, "your call":** graphite inset with muted text; high impact only brightens the text.
- **Risk:** risk-red on risk-red-soft — the only coloured chip.
- **Pending count:** pending amber on its soft tone.

### Cards / Containers
- **Corner Style:** 10px.
- **Background:** graphite panel.
- **Shadow Strategy:** none (The Flat Sheet Rule).
- **Border:** 1px soft edge; 1px stronger edge when a category is expanded.
- **Internal Padding:** 20–24px; the setup sheet runs its rules edge to edge inside a 24px inset.

### Inputs / Fields
- **Style:** graphite panel field, 1px graphite edge, 6px radius, a search glyph and placeholder in Text Dim.
- **Focus:** paper-white caret; selection in signal orange at 45 %.

### Navigation
- Sidebar items with a Fluent icon and label in Text Muted; hover tints the row; the active item turns graphite selected with paper-white text and a 3px signal-orange tick on the left (the Windows 11 navigation convention). Group names are Bahnschrift labels. The first nine sections answer to Ctrl+1…9, shown in their tooltips.

### Switches
- 44×24 pill. Off: track grey with a grey knob. On: paper-white track with an ink knob. The knob travels in 160ms with an exponential ease-out.

### The Setup Sheet (signature component)
- A panel whose header is the PC in figures (CPU, GPU, RAM, system drive), followed by a ruled table: SETTING | BASE → VELOX (or NOW → RECOMMENDED when changes are recommended). The base value sits right-aligned against the arrow, the target left-aligned after it in semi-bold. The foot carries the primary "Apply N" button and, on the right, "N changes saved · Back to base".
- In Optimizations every row repeats the delta under its name: the side in effect is paper white and semi-bold, the other dim; a pending change turns the arrow and its destination amber and tints the row.

## Do's and Don'ts

### Do:
- **Do** show the BASE → VELOX delta wherever a setting appears, using the setting's own labels.
- **Do** keep "N changes saved · Back to base" visible on the Overview.
- **Do** set every number, spec and column head in Bahnschrift with tabular numerals.
- **Do** separate list items with 1px hairline rules inside one panel instead of a grid of identical cards.
- **Do** give every interactive control a visible 2px paper-white keyboard focus ring and an accessible name.

### Don't:
- **Don't** use neon or RGB glow, aggressive angular shapes, "BOOST"/"+FPS" claims, or anything that looks like a game HUD.
- **Don't** use gradients on any surface, button, bar or text.
- **Don't** put signal orange on anything that isn't an action, or show more than one orange button per screen.
- **Don't** colour impact, recommendation or data with green, amber or red.
- **Don't** bring in fonts or assets that don't ship with Windows; Velox stays a single executable.
