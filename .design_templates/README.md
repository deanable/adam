# Design Templates

This directory contains reference documentation for adam's design system and component library.

## What's Here

| File | Purpose |
|------|---------|
| `design_token_reference.md` | Complete list of all `{DynamicResource}` tokens mapped to light/dark values |
| `component_style_guide.md` | Visual and behavioral reference for all custom controls |
| `layout_patterns.md` | Responsive layout patterns: default 3-column, loupe mode, compare mode |
| `accent_palettes.md` | 5 preset accent color palettes with hex values and usage guidance |

## How to Create a Theme

1. Copy an existing `.design/*.md` file as a template
2. Add YAML frontmatter with `colors:`, `rounded:`, and `spacing:` tokens
3. Drop the file into `.design/`
4. Restart the app — the theme appears in the title-bar dropdown automatically

See `design_token_reference.md` for the full token schema.
