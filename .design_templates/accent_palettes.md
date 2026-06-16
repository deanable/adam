# Accent Palettes

## Blue (Default)

| Step | Hex | Usage |
|------|-----|-------|
| Base | `#1976D2` | Primary buttons, active links |
| Hover | `#1565C0` | Button hover state |
| Pressed | `#0D47A1` | Button pressed state |
| Light | `#E3F2FD` | Selection backgrounds |
| Dark | `#0D47A1` | Title bar |

## Green

| Step | Hex | Usage |
|------|-----|-------|
| Base | `#2E7D32` | Primary buttons |
| Hover | `#1B5E20` | Button hover state |
| Pressed | `#004D40` | Button pressed state |
| Light | `#E8F5E9` | Selection backgrounds |
| Dark | `#1B5E20` | Title bar |

## Purple

| Step | Hex | Usage |
|------|-----|-------|
| Base | `#7B1FA2` | Primary buttons |
| Hover | `#6A1B9A` | Button hover state |
| Pressed | `#4A148C` | Button pressed state |
| Light | `#F3E5F5` | Selection backgrounds |
| Dark | `#4A148C` | Title bar |

## Orange

| Step | Hex | Usage |
|------|-----|-------|
| Base | `#E65100` | Primary buttons |
| Hover | `#D84315` | Button hover state |
| Pressed | `#BF360C` | Button pressed state |
| Light | `#FBE9E7` | Selection backgrounds |
| Dark | `#BF360C` | Title bar |

## Teal

| Step | Hex | Usage |
|------|-----|-------|
| Base | `#00796B` | Primary buttons |
| Hover | `#00695C` | Button hover state |
| Pressed | `#004D40` | Button pressed state |
| Light | `#E0F2F1` | Selection backgrounds |
| Dark | `#004D40` | Title bar |

## Using Accents in Design Files

Add an accent color to your `.design/*.md` theme file's YAML frontmatter:

```yaml
colors:
  accent: "#7B1FA2"    # Purple
  primary: "#7B1FA2"    # Same as accent for cohesive look
  ink: "#212121"        # Near-black for text
  canvas: "#FAFAFA"     # Warm off-white for background
```

The `DesignThemeService` generates hover/pressed states automatically (darken 10%/20%) from the base accent color.
