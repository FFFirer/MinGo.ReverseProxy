## ADDED Requirements

### Requirement: Icons display as SVG components
The system SHALL render all UI icons as SolidJS SVG components from the `solid-icons` library, replacing the previous CSS font-based approach.

#### Scenario: Icon renders as inline SVG
- **WHEN** a page renders an icon component (e.g., `<FaServer />`)
- **THEN** the output SHALL be an `<svg>` element in the DOM with appropriate `viewBox` attribute

#### Scenario: Icon is visible
- **WHEN** a page containing icon components is rendered
- **THEN** each icon SHALL be visually displayed (not empty or invisible)
- **AND** the icon SHALL not be a placeholder, broken image, or missing glyph

### Requirement: Icon component accepts Tailwind CSS class prop
All icon components SHALL accept a `class` prop for Tailwind CSS styling, matching the existing usage pattern of `class="w-6 text-center"`.

#### Scenario: Class prop applied to SVG
- **WHEN** an icon component is used with `class="w-6 h-6 text-primary"`
- **THEN** the rendered `<svg>` element SHALL have those CSS classes applied

#### Scenario: Icon inherits text color
- **WHEN** an icon has a `class` prop containing `text-primary`, `text-danger`, etc.
- **THEN** the icon's fill/stroke SHALL render in the corresponding color

### Requirement: Icons are tree-shaken
The build process SHALL only include icon components that are actually imported and used in the application bundle.

#### Scenario: Unused icons excluded from bundle
- **WHEN** the application is built with `pnpm build`
- **THEN** the production bundle SHALL NOT contain icon components that are not imported anywhere in the source code

### Requirement: All existing icons are covered
Every existing `<i class="fa fa-xxx">` reference across the frontend codebase SHALL be replaced with the corresponding `solid-icons/fa` component.

#### Scenario: Sidebar navigation icons display correctly
- **WHEN** the sidebar renders navigation items
- **THEN** all 9 nav icons SHALL be visible and correctly styled

#### Scenario: Header icons display correctly
- **WHEN** the header renders
- **THEN** all header icons (menu, search, theme toggle, notifications, user) SHALL be visible

#### Scenario: Toast notification icons display correctly
- **WHEN** a toast notification appears
- **THEN** the toast icon (success/error/warning) SHALL be visible
- **AND** the close button icon SHALL be visible

#### Scenario: Dashboard metric icons display correctly
- **WHEN** the dashboard renders metric cards
- **THEN** all metric icons SHALL be visible

#### Scenario: CRUD action icons display correctly
- **WHEN** a page with action buttons renders
- **THEN** all action icons (add, edit, delete, save, upload, search, refresh) SHALL be visible

#### Scenario: Collapsible section icons display correctly
- **WHEN** a collapsible section renders
- **THEN** the chevron icon SHALL indicate the expanded/collapsed state

### Requirement: Icons remain clear at different sizes
The SVG-based icons SHALL render clearly at any size or screen DPI without pixelation or blurriness.

#### Scenario: Icon at small size
- **WHEN** an icon is styled with `class="w-4 h-4"`
- **THEN** the icon SHALL appear sharp without visible pixelation

#### Scenario: Icon at large size
- **WHEN** an icon is styled with `class="text-4xl"`
- **THEN** the icon SHALL appear sharp without visible pixelation or blurriness

#### Scenario: Icon on high-DPI display
- **WHEN** the page is viewed on a Retina/HiDPI display
- **THEN** the icon SHALL appear crisp and not blurry
