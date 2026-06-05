## ADDED Requirements

### Requirement: Compact icon button with tooltip
In compact form areas (e.g., target address list rows in cluster form), operation buttons SHALL display as icon-only with a native HTML `title` tooltip.

#### Scenario: Icon button renders in compact area
- **WHEN** rendering a delete button in a compact form row
- **THEN** the button SHALL use `.btn-icon-danger` class and display a `<i class="fa fa-times"></i>` icon
- **AND** the button SHALL have a `title="删除"` attribute for tooltip

#### Scenario: Icon button hover interaction
- **WHEN** user hovers over an icon button
- **THEN** the button SHALL show a semi-transparent background (`hover:bg-{color}/10`)
- **AND** the browser SHALL display the native tooltip with the button's description

#### Scenario: Add target icon button in cluster form
- **WHEN** rendering the "添加目标" button in cluster form modal
- **THEN** the button SHALL use `.btn-icon-primary` class and display a `<i class="fa fa-plus"></i>` icon
- **AND** the button SHALL have a `title="添加目标"` attribute

### Requirement: Text-only button for table operation columns
In spacious but constrained areas (e.g., table operation columns in routes and certificates pages), operation buttons SHALL display as text-only with hover background effect.

#### Scenario: Text button renders in table operation column
- **WHEN** rendering an edit operation in a table row
- **THEN** the button SHALL use `.btn-text-primary` class and display the text "编辑"
- **AND** the button SHALL NOT display an icon

#### Scenario: Delete text button in table
- **WHEN** rendering a delete operation in a table row
- **THEN** the button SHALL use `.btn-text-danger` class and display the text "删除"
- **AND** the button SHALL NOT display an icon

#### Scenario: Text button hover interaction
- **WHEN** user hovers over a text button
- **THEN** the button SHALL show a semi-transparent background (`hover:bg-{color}/10`)

### Requirement: Icon+text button for spacious areas
In spacious card areas (e.g., cluster card bottom action area), operation buttons SHALL display as icon + text with hover background effect.

#### Scenario: Icon+text button renders in card
- **WHEN** rendering an edit operation in a cluster card bottom
- **THEN** the button SHALL use `.btn-text-primary` class with `<i class="fa fa-edit mr-1"></i>编辑`
- **AND** the button SHALL have consistent padding with other `.btn-text` buttons

#### Scenario: Icon+text delete button in card
- **WHEN** rendering a delete operation in a cluster card bottom
- **THEN** the button SHALL use `.btn-text-danger` class with `<i class="fa fa-trash mr-1"></i>删除`
