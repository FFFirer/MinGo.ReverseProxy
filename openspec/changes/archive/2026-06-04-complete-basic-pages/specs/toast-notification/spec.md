## ADDED Requirements

### Requirement: Global toast notification system
The system SHALL provide a global toast notification component for displaying operation feedback messages across all pages.

#### Scenario: Show success toast
- **WHEN** a save/create/update/delete API call returns successfully
- **THEN** a green success toast SHALL appear with the operation description message
- **AND** the toast SHALL auto-dismiss after 3 seconds

#### Scenario: Show error toast
- **WHEN** an API call throws an exception or returns a non-ok status
- **THEN** a red error toast SHALL appear with the error message
- **AND** the toast SHALL auto-dismiss after 5 seconds

#### Scenario: Show warning toast
- **WHEN** an operation completes with a warning condition (e.g., partial success)
- **THEN** a yellow warning toast SHALL appear with the warning message
- **AND** the toast SHALL auto-dismiss after 4 seconds

#### Scenario: Manual toast dismissal
- **WHEN** a toast is visible
- **THEN** the user SHALL be able to click the close button to dismiss it immediately

#### Scenario: Multiple toasts stacking
- **WHEN** multiple toasts are triggered in succession
- **THEN** they SHALL stack vertically at the top-right corner of the viewport
- **AND** each toast SHALL have independent timer and dismissal

### Requirement: Toast accessible from any component
The toast system SHALL expose `addToast` and `removeToast` functions via a global store, importable from any component.

#### Scenario: Import and trigger toast
- **WHEN** any page component imports `addToast` from the toast store
- **AND** calls `addToast({ type: 'success', message: '操作成功' })`
- **THEN** a toast SHALL appear in the viewport
