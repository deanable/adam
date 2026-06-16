# Design Token Reference

All design tokens are exposed as `{DynamicResource}` keys in Avalonia XAML. Themes are loaded from `.design/*.md` files at startup via `DesignThemeService`.

## Color Tokens

| Token Name | Light Value | Dark Value | Usage |
|-----------|-------------|------------|-------|
| `PrimaryBrush` | `#1976D2` | `#90CAF9` | Buttons, links, selection highlights |
| `PrimaryHoverBrush` | `#1565C0` | `#64B5F6` | Primary hover state |
| `PrimaryPressedBrush` | `#0D47A1` | `#42A5F5` | Primary pressed state |

### Surface

| Token | Light | Dark | Usage |
|-------|-------|------|-------|
| `SurfaceBrush` | `#FFFFFF` | `#2D2D2D` | Main panels, card backgrounds |
| `SurfaceLightBrush` | `#F8F9FA` | `#363636` | Toolbars, elevated surfaces |
| `SurfaceSubtleBrush` | `#F0F1F3` | `#252525` | Sidebar, secondary panels |
| `SurfaceDisabledBrush` | `#E9ECEF` | `#1E1E1E` | Disabled state backgrounds |

### Text

| Token | Light | Dark | Usage |
|-------|-------|------|-------|
| `TextPrimaryBrush` | `#212121` | `#E0E0E0` | Body text, headings |
| `TextSecondaryBrush` | `#616161` | `#BDBDBD` | Secondary information |
| `TextMutedBrush` | `#9E9E9E` | `#9E9E9E` | Subtle hints |
| `TextTertiaryBrush` | `#BDBDBD` | `#757575` | Captions, metadata labels |
| `TextPlaceholderBrush` | `#CACACA` | `#616161` | Placeholder text |
| `TextDisabledBrush` | `#E0E0E0` | `#424242` | Disabled labels |

### Semantic

| Token | Light | Dark | Usage |
|-------|-------|------|-------|
| `SuccessBrush` | `#4CAF50` | `#81C784` | Success indicators, confirm buttons |
| `SuccessHoverBrush` | `#43A047` | `#66BB6A` | Success hover state |
| `DangerBrush` | `#E53935` | `#E57373` | Delete/destructive actions |
| `DangerHoverBrush` | `#C62828` | `#EF5350` | Danger hover state |
| `WarningBrush` | `#FB8C00` | `#FFB74D` | Warning badges, caution buttons |
| `WarningHoverBrush` | `#F57C00` | `#FFA726` | Warning hover state |

### Borders

| Token | Light | Dark | Usage |
|-------|-------|------|-------|
| `BorderBrush` | `#E0E0E0` | `#404040` | Control borders, dividers |
| `BorderLightBrush` | `#EEEEEE` | `#484848` | Subtle separators |
| `InputBorderBrush` | `#BDBDBD` | `#505050` | TextBox, ComboBox borders |

### Interactive

| Token | Light | Dark | Usage |
|-------|-------|------|-------|
| `SelectionHoverBrush` | `#F5F5F5` | `#383838` | Hover highlight on items |
| `SelectionBrush` | `#E3F2FD` | `#1E3A5F` | Selected item background |
| `SelectionActiveBrush` | `#BBDEFB` | `#2A4F7F` | Active/focused selection |

### Accent Palettes

Five accent presets are defined in `accent_palettes.md`. The primary accent color drives:
- `PrimaryBrush` / `PrimaryHoverBrush` / `PrimaryPressedBrush`
- `TitleBarBrush` / `TitleBarTextBrush`
- `SelectionBrush` variants
- `AiTagBrush` variants

## Typography

| Class | Family | Size | Weight | Usage |
|-------|--------|------|--------|-------|
| `.Title` | Default | 16px | Bold | Window titles, section headers |
| `.Subheading` | Default | 13px | SemiBold | Panel headers, dialog titles |
| `.Body` | Default | 12px | Normal | Body text, control labels |
| `.Caption` | Default | 11px | Normal | Metadata, timestamps, secondary info |

## Rounded Corners

| Token | Value | Usage |
|-------|-------|-------|
| Button corner radius | 4px | Buttons, input fields |
| Panel corner radius | 6px | Toast notifications, dialogs |
| Tile corner radius | 8px | Asset tiles in gallery |

## Spacing

| Token | Value | Usage |
|-------|-------|-------|
| Tight | 4px | Between icon and label |
| Compact | 8px | Between related controls |
| Normal | 12px | Between unrelated sections |
| Relaxed | 16px | Panel padding |
| Wide | 24px | Section spacing |

## Fallback Chains

When a design file doesn't define a specific token, the system falls back through a chain:
- `TextPrimaryBrush` → `ink` → `body` → `charcoal` → `primary`
- `SurfaceBrush` → `canvas` → `surface-soft` → `on-primary`
- `BorderBrush` → `hairline` → `canvas` → `surface-soft`

Derived resources (hover, pressed, disabled states) are generated programmatically using darken/lighten logic applied to the base tokens.
