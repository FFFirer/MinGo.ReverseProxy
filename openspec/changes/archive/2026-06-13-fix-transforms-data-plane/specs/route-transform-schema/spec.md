# Route Transform Schema

## Purpose

Define how route request/response transforms are modeled, stored, served to frontend, and applied to YARP proxy configuration.

## ADDED Requirements

### Requirement: Transform data model uses YARP-native list format

Route transforms SHALL be stored as a list of flat dictionaries (`List<Dictionary<string, string>>`), directly matching YARP's native transform pipeline format. Each dictionary represents one transform operation. The transform type key (e.g. `"PathPrefix"`, `"RequestHeader"`) SHALL be the first key in the dictionary, and additional keys SHALL be the parameters for that transform type.

The `RouteConfig.Transforms` property SHALL use this type. The `ApiRouteEntity.TransformsJson` SHALL serialize this list as a JSON array. The default value SHALL be an empty array `"[]"`.

The `TransformsJson` default in the entity SHALL be `"[]"`. Older records with `"{}"` SHALL be treated equivalently to `null` (no transforms configured).

#### Scenario: Transform list is directly serializable to YARP

- **WHEN** the data plane receives a `ConfigSnapshot` with route transforms
- **THEN** it SHALL pass the raw list to `YarpRouteConfig.Transforms` without any format conversion

#### Scenario: Backward compatibility with empty object

- **WHEN** an existing `ApiRouteEntity` has `TransformsJson = "{}"`
- **THEN** `GetTransforms()` SHALL return `null`
- **AND** the route SHALL behave as if no transforms are configured

### Requirement: Transform Schema Registry API

The control plane SHALL expose a `GET /api/transforms/schemas` endpoint that returns all known transform type definitions. Each transform schema SHALL include:

- `type`: The YARP transform type key (e.g. `"PathPrefix"`, `"RequestHeader"`, `"XForwarded"`)
- `displayName`: Human-readable name in Chinese
- `category`: Logical grouping — one of `"path"`, `"requestHeader"`, `"responseHeader"`, `"xForwarded"`
- `description`: Tooltip/helper text
- `order`: Rendering order within the category
- `isList`: Whether the transform supports multiple entries
- `fields`: Array of field schemas, each with `key`, `label`, `type` (`"text"` or `"select"`), `required`, `options` (for select), `placeholder`, `defaultValue`
- `defaultEntries`: Optional list of preset entry dictionaries shown by default (for XForwarded)

The schemas SHALL be defined statically in `TransformSchemaRegistry` and returned without database access.

#### Scenario: Frontend fetches transform schemas

- **WHEN** the route configuration page loads
- **THEN** it SHALL fetch `GET /api/transforms/schemas` to know what transform types are available and how to render their fields

#### Scenario: Adding a new transform type requires only a schema entry

- **WHEN** a developer adds a new transform type to the registry
- **THEN** no data plane code, frontend code, or gRPC proto changes SHALL be needed for the new transform type to be configurable and functional

### Requirement: Frontend renders transforms form via three-step state machine with Modal-in-Modal

The route edit form SHALL include a collapsible "请求变换" (Request Transforms) panel. The transforms editing SHALL follow a three-step state machine where PICK and CONFIGURE are rendered as **inner dialogs** (absolute overlay over the entire outer modal card), and LIST is the in-flow content:

1. **LIST** (in-flow): All configured transforms SHALL be displayed as compact summary cards. Each card SHALL show the transform type name and a brief summary of its values. Cards SHALL be clickable to edit (opens CONFIGURE inner dialog). A delete button SHALL be on each card. An "+ 添加变换" button SHALL open the PICK inner dialog.
2. **PICK** (inner dialog): All available transform types from `TransformSchemaRegistry` SHALL be displayed grouped by `category` in a grid button layout. The dialog SHALL be absolutely positioned over the outer modal card with a semi-transparent backdrop, and SHALL have its own scroll container. Clicking a type SHALL close PICK and open CONFIGURE. Clicking cancel or the backdrop SHALL close the dialog and return to LIST.
3. **CONFIGURE** (inner dialog): The selected transform's fields SHALL be rendered as a form according to its `TransformSchema.Fields`. The dialog SHALL use the same absolute positioning pattern as PICK. Confirming SHALL add/update the entry. Cancelling SHALL discard changes. Both actions close the dialog and return to LIST.

Both inner dialogs SHALL be positioned within the outer modal card using `absolute inset-0`, keeping the LIST visible in the background. The inner dialogs SHALL NOT be full-screen overlays — they are scoped within the outer modal's visual boundaries. The user can dismiss either dialog by clicking the backdrop or the cancel button.

The form SHALL NOT hardcode any transform-specific rendering logic. All field types, labels, validation rules, and options SHALL come from the schema API response. Adding a new transform type to the registry SHALL automatically make it available in the pick step without any frontend code changes.

#### Scenario: Pick step opens as inner dialog over the list

- **WHEN** a user clicks "+ 添加变换" in the list view
- **THEN** a PICK inner dialog SHALL appear as an absolute overlay covering the entire outer modal card, with the list view still visible behind a semi-transparent backdrop
- **AND** the inner dialog SHALL have its own scrollbar if content overflows
- **AND** clicking "取消" or the backdrop SHALL close the dialog and return to the list view

#### Scenario: User configures path prefix transform via three-step flow

- **WHEN** a user clicks "+ 添加变换" in the list view
- **THEN** a PICK inner dialog SHALL appear with available transform types grouped by category
- **WHEN** the user selects "路径前缀" from the path category
- **THEN** the PICK dialog SHALL close and a CONFIGURE inner dialog SHALL open with fields "添加前缀" and "移除前缀"
- **WHEN** the user fills `PathPrefix = "/v1"` and `Prefix = "/api"` and clicks "确认"
- **THEN** the CONFIGURE dialog SHALL close and the transforms list SHALL contain `{ "PathPrefix": "/v1", "Prefix": "/api" }`
- **AND** the list view SHALL show a card with "路径前缀: 添加 /v1  移除 /api"

#### Scenario: User edits an existing transform via inner dialog

- **WHEN** a user clicks a transform card in the list view
- **THEN** a CONFIGURE inner dialog SHALL open with the entry's current values pre-filled, and the list SHALL remain visible behind the backdrop
- **WHEN** the user modifies a field and clicks "确认"
- **THEN** the dialog SHALL close and the entry SHALL be updated in the transforms list
- **WHEN** the user clicks "取消" or the backdrop instead
- **THEN** the dialog SHALL close without changes

#### Scenario: User deletes a transform

- **WHEN** a user clicks the delete button on a transform card
- **THEN** the entry SHALL be removed from the transforms list without opening any dialog
