# Multi-Theme Color System Skill

This skill implements a scalable, framework-agnostic theming system using CSS custom properties. It supports any number of themes (light, dark, high-contrast, etc.) with minimal code overhead.

## Architecture Overview

The system uses three layers:
1. **CSS Custom Properties** - Define all color tokens as variables
2. **Theme Class Selector** - A single class on the root element activates a theme
3. **Utility Classes** - Pre-built classes for common color applications

## Core Pattern

### Step 1: Define Color Tokens in CSS

Create a `colors.css` file with all theme variables:

```css
:root {
  /* ===== BASE THEME (Light) ===== */

  /* Primary - Main brand/accent color */
  --clr-primary: #6442d6;
  --clr-on-primary: #ffffff;
  --clr-primary-container: #9f86ff;
  --clr-on-primary-container: #1e0060;

  /* Secondary - Supporting accent */
  --clr-secondary: #5d5d74;
  --clr-on-secondary: #ffffff;
  --clr-secondary-container: #dcdaf5;
  --clr-on-secondary-container: #21182b;

  /* Tertiary - Third accent (optional) */
  --clr-tertiary: #7d5260;
  --clr-on-tertiary: #ffffff;
  --clr-tertiary-container: #f1d3f9;
  --clr-on-tertiary-container: #271430;

  /* Error/Alert */
  --clr-error: #ff6240;
  --clr-on-error: #490909;
  --clr-error-container: #f9dedc;
  --clr-on-error-container: #410e0b;

  /* Surface levels (backgrounds with depth) */
  --clr-surface-1: #f8f1f6;
  --clr-surface-2: #f2ecee;
  --clr-surface-3: #ece7e9;
  --clr-surface-4: #e6e1e3;
  --clr-surface-5: #e0dbdd;

  /* Utility colors */
  --clr-outline: #787579;
  --clr-shadow: rgba(0, 0, 0, 0.15);
}
```

### Step 2: Define Additional Themes

Add theme overrides using a class selector pattern:

```css
/* ===== DARK THEME ===== */
.dark {
  --clr-primary: #d2bafd;
  --clr-on-primary: #3c1871;
  --clr-primary-container: #53338a;
  --clr-on-primary-container: #ebdcfe;

  --clr-secondary: #cdc1dc;
  --clr-on-secondary: #342c41;
  --clr-secondary-container: #4b4358;
  --clr-on-secondary-container: #e9ddf8;

  --clr-tertiary: #edb8c8;
  --clr-on-tertiary: #482532;
  --clr-tertiary-container: #623b48;
  --clr-on-tertiary-container: #fed8e4;

  --clr-error: #efb9b6;
  --clr-on-error: #5e1612;
  --clr-error-container: #89201b;
  --clr-on-error-container: #ffdad6;

  --clr-surface-1: #141218;
  --clr-surface-2: #1d1b20;
  --clr-surface-3: #0f0d13;
  --clr-surface-4: #3b383e;
  --clr-surface-5: #484649;

  --clr-outline: #938f99;
  --clr-shadow: rgba(0, 0, 0, 0.4);
}

/* ===== HIGH CONTRAST THEME (example) ===== */
.high-contrast {
  --clr-primary: #0000ff;
  --clr-on-primary: #ffffff;
  --clr-surface-1: #ffffff;
  --clr-outline: #000000;
  /* ... override other tokens as needed */
}
```

### Step 3: Create Utility Classes

Generate utility classes that reference the variables:

```css
/* Text colors */
.clr-primary { color: var(--clr-primary); }
.clr-on-primary { color: var(--clr-on-primary); }
.clr-secondary { color: var(--clr-secondary); }
.clr-on-secondary { color: var(--clr-on-secondary); }
.clr-tertiary { color: var(--clr-tertiary); }
.clr-error { color: var(--clr-error); }
.clr-outline { color: var(--clr-outline); }

/* Background colors */
.bg-primary { background-color: var(--clr-primary); }
.bg-on-primary { background-color: var(--clr-on-primary); }
.bg-primary-container { background-color: var(--clr-primary-container); }
.bg-secondary { background-color: var(--clr-secondary); }
.bg-secondary-container { background-color: var(--clr-secondary-container); }
.bg-surface-1 { background-color: var(--clr-surface-1); }
.bg-surface-2 { background-color: var(--clr-surface-2); }
.bg-surface-3 { background-color: var(--clr-surface-3); }
.bg-surface-4 { background-color: var(--clr-surface-4); }
.bg-surface-5 { background-color: var(--clr-surface-5); }
.bg-error { background-color: var(--clr-error); }
.bg-error-container { background-color: var(--clr-error-container); }

/* Border colors */
.border-primary { border-color: var(--clr-primary); }
.border-outline { border-color: var(--clr-outline); }
.border-error { border-color: var(--clr-error); }
```

### Step 4: Apply Theme via Root Class

Add/remove the theme class on your root element:

```html
<!-- Light theme (default) -->
<body class="light">...</body>

<!-- Dark theme -->
<body class="dark">...</body>

<!-- Any custom theme -->
<body class="high-contrast">...</body>
```

### Step 5: Theme Switching (JavaScript)

```javascript
// Simple theme switcher
function setTheme(themeName) {
  // Remove all theme classes
  document.body.classList.remove('light', 'dark', 'high-contrast');
  // Add the new theme class
  document.body.classList.add(themeName);
  // Persist preference
  localStorage.setItem('theme', themeName);
}

// Load saved theme on page load
function loadTheme() {
  const saved = localStorage.getItem('theme') || 'light';
  setTheme(saved);
}

// Initialize
document.addEventListener('DOMContentLoaded', loadTheme);
```

## Color Naming Convention (Material Design 3)

Follow this semantic naming pattern:

| Token | Purpose |
|-------|---------|
| `--clr-primary` | Main brand/accent color |
| `--clr-on-primary` | Text/icons ON primary backgrounds |
| `--clr-primary-container` | Lighter shade for containers/cards |
| `--clr-on-primary-container` | Text ON primary containers |
| `--clr-secondary` | Supporting accent color |
| `--clr-tertiary` | Third accent (for highlights) |
| `--clr-error` | Error/destructive actions |
| `--clr-surface-N` | Background levels (1=lightest, 5=darkest) |
| `--clr-outline` | Borders and dividers |

The "on-" prefix means "content that sits ON this color" - ensuring proper contrast.

## Adding a New Theme (Minimal Steps)

To add a new theme (e.g., "ocean"):

1. Add a new class block in your CSS:
```css
.ocean {
  --clr-primary: #0077b6;
  --clr-on-primary: #ffffff;
  --clr-primary-container: #90e0ef;
  --clr-on-primary-container: #03045e;
  /* ... define all tokens */
}
```

2. Add it to your theme switcher options - done!

No changes needed to utility classes or component styles.

## Reference: Light/Dark Color Values

### Light Theme Palette
```
Primary:           #6442d6 (Purple)
On Primary:        #ffffff
Primary Container: #9f86ff
Secondary:         #5d5d74 (Gray)
Tertiary:          #7d5260 (Mauve)
Error:             #ff6240 (Orange-Red)
Surface 1:         #f8f1f6 (Near White)
Surface 2:         #f2ecee
Surface 3:         #ece7e9
Surface 4:         #e6e1e3
Surface 5:         #e0dbdd
Outline:           #787579
```

### Dark Theme Palette
```
Primary:           #d2bafd (Light Purple)
On Primary:        #3c1871
Primary Container: #53338a
Secondary:         #cdc1dc (Light Gray)
Tertiary:          #edb8c8 (Light Pink)
Error:             #efb9b6 (Soft Red)
Surface 1:         #141218 (Near Black)
Surface 2:         #1d1b20
Surface 3:         #0f0d13
Surface 4:         #3b383e
Surface 5:         #484649
Outline:           #938f99
```

## Usage in Component Styles

Always use CSS variables, never hardcoded colors:

```css
/* CORRECT */
.card {
  background-color: var(--clr-surface-2);
  color: var(--clr-on-primary-container);
  border: 1px solid var(--clr-outline);
}

.card:hover {
  background-color: var(--clr-primary-container);
}

/* INCORRECT - hardcoded colors */
.card {
  background-color: #f2ecee;
  color: #1e0060;
}
```

## Benefits of This Pattern

1. **Single source of truth** - All colors defined in one place
2. **Zero JS overhead** - Theme switch is just a class change
3. **Unlimited themes** - Add themes by adding CSS class blocks
4. **Framework agnostic** - Works with any tech stack
5. **No component changes** - Components reference variables, not values
6. **Browser native** - CSS variables have excellent performance
7. **DevTools friendly** - Easy to inspect and debug

## Complete Starter Template

```css
/* ========== colors.css ========== */

:root {
  /* Light theme (default) */
  --clr-primary: #6442d6;
  --clr-on-primary: #ffffff;
  --clr-primary-container: #9f86ff;
  --clr-on-primary-container: #1e0060;
  --clr-secondary: #5d5d74;
  --clr-on-secondary: #ffffff;
  --clr-secondary-container: #dcdaf5;
  --clr-on-secondary-container: #21182b;
  --clr-tertiary: #7d5260;
  --clr-on-tertiary: #ffffff;
  --clr-tertiary-container: #f1d3f9;
  --clr-on-tertiary-container: #271430;
  --clr-error: #ff6240;
  --clr-on-error: #490909;
  --clr-error-container: #f9dedc;
  --clr-on-error-container: #410e0b;
  --clr-surface-1: #f8f1f6;
  --clr-surface-2: #f2ecee;
  --clr-surface-3: #ece7e9;
  --clr-surface-4: #e6e1e3;
  --clr-surface-5: #e0dbdd;
  --clr-outline: #787579;
  --clr-shadow: rgba(0, 0, 0, 0.15);
}

.dark {
  --clr-primary: #d2bafd;
  --clr-on-primary: #3c1871;
  --clr-primary-container: #53338a;
  --clr-on-primary-container: #ebdcfe;
  --clr-secondary: #cdc1dc;
  --clr-on-secondary: #342c41;
  --clr-secondary-container: #4b4358;
  --clr-on-secondary-container: #e9ddf8;
  --clr-tertiary: #edb8c8;
  --clr-on-tertiary: #482532;
  --clr-tertiary-container: #623b48;
  --clr-on-tertiary-container: #fed8e4;
  --clr-error: #efb9b6;
  --clr-on-error: #5e1612;
  --clr-error-container: #89201b;
  --clr-on-error-container: #ffdad6;
  --clr-surface-1: #141218;
  --clr-surface-2: #1d1b20;
  --clr-surface-3: #0f0d13;
  --clr-surface-4: #3b383e;
  --clr-surface-5: #484649;
  --clr-outline: #938f99;
  --clr-shadow: rgba(0, 0, 0, 0.4);
}

/* Utility classes */
.clr-primary { color: var(--clr-primary); }
.clr-on-primary { color: var(--clr-on-primary); }
.clr-secondary { color: var(--clr-secondary); }
.clr-error { color: var(--clr-error); }

.bg-primary { background-color: var(--clr-primary); }
.bg-primary-container { background-color: var(--clr-primary-container); }
.bg-secondary-container { background-color: var(--clr-secondary-container); }
.bg-surface-1 { background-color: var(--clr-surface-1); }
.bg-surface-2 { background-color: var(--clr-surface-2); }
.bg-surface-3 { background-color: var(--clr-surface-3); }
.bg-error-container { background-color: var(--clr-error-container); }

.border-outline { border-color: var(--clr-outline); }
.border-primary { border-color: var(--clr-primary); }
```

```javascript
/* ========== theme.js ========== */

const THEMES = ['light', 'dark']; // Add new theme names here

function setTheme(name) {
  document.body.classList.remove(...THEMES);
  document.body.classList.add(name);
  localStorage.setItem('theme', name);
}

function getTheme() {
  return localStorage.getItem('theme') || 'light';
}

function toggleTheme() {
  const current = getTheme();
  const next = current === 'dark' ? 'light' : 'dark';
  setTheme(next);
}

// Initialize on load
document.addEventListener('DOMContentLoaded', () => setTheme(getTheme()));
```
