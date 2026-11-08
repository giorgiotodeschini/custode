# Spec Delta

## Purpose
Defines the technology stack, theming, responsive behavior, and accessibility baseline shared by every screen of the Custode web UI, so that the application behaves consistently on desktop and mobile and never keeps credentials in browser storage.

## ADDED Requirements

### Requirement: The web UI is a TypeScript Angular application
The web UI SHALL be a single-page application written in TypeScript and built with Angular. The production build MUST complete without TypeScript or template type errors.

#### Scenario: Production build
- **WHEN** the web UI is built in production mode
- **THEN** the build completes with zero TypeScript and template type errors

### Requirement: Styling uses Tailwind CSS design tokens
All screens SHALL be styled with Tailwind CSS utilities that reference one shared set of design tokens (colors, spacing, typography, radii). Components MUST NOT define competing hard-coded colors outside those tokens.

#### Scenario: New screen added
- **WHEN** a developer adds a new screen
- **THEN** its colors and spacing are taken from the shared design tokens

### Requirement: Dark mode is supported
The web UI SHALL provide a light and a dark theme. When the user has not chosen a theme, the UI SHALL follow the operating system color scheme. A chosen theme SHALL persist across sessions in this browser.

#### Scenario: Theme follows the operating system
- **WHEN** the user has not chosen a theme and the operating system uses a dark color scheme
- **THEN** the UI is displayed in the dark theme

#### Scenario: User chooses a theme
- **WHEN** the user selects the light theme and reloads the page
- **THEN** the light theme is still applied

#### Scenario: Text contrast in both themes
- **WHEN** any text is displayed in either theme
- **THEN** its contrast ratio against the background is at least 4.5:1 for normal text

### Requirement: Angular CDK provides the standard UI primitives
The web UI SHALL use Angular CDK for clipboard access, the folder tree, overlays and dialogs, accessibility support (focus management, live announcements, and keyboard navigation), and layout breakpoints. Behavior provided by these primitives MUST NOT be reimplemented in custom components.

#### Scenario: Modal dialog opened
- **WHEN** a confirmation or form dialog is opened
- **THEN** keyboard focus is trapped inside the dialog, and focus returns to the triggering control when it closes

#### Scenario: Folder tree keyboard navigation
- **WHEN** a user navigates the folder tree with arrow keys
- **THEN** focus moves between visible nodes and expanding or collapsing follows the tree keyboard pattern

### Requirement: Layout adapts to desktop and mobile viewports
The web UI SHALL present a multi-panel layout on wide viewports and a single-column layout on narrow viewports. Core screens MUST NOT require horizontal scrolling at a viewport width of 360 pixels or more.

#### Scenario: Desktop viewport
- **WHEN** the viewport is at least 1024 pixels wide
- **THEN** the sidebar is visible beside the content and dashboards show multiple panels

#### Scenario: Mobile viewport
- **WHEN** the viewport is narrower than 768 pixels
- **THEN** the sidebar becomes a drawer opened from a menu control and content is shown in a single column

#### Scenario: Narrow phone
- **WHEN** the viewport is 360 pixels wide and the user opens the folder list
- **THEN** the page does not scroll horizontally

### Requirement: The UI meets the WCAG 2.2 AA baseline
Every interactive control SHALL be reachable and operable with the keyboard, show a visible focus indicator, and have an accessible name. Status changes such as copy confirmations and session warnings SHALL be announced to assistive technology. Motion SHALL be reduced when the operating system requests reduced motion.

#### Scenario: Keyboard-only use
- **WHEN** a user operates a screen with the keyboard only
- **THEN** every control is reachable and its focus indicator is visible

#### Scenario: Status announced
- **WHEN** a credential is copied
- **THEN** the confirmation is announced through a live region

#### Scenario: Reduced motion requested
- **WHEN** the operating system requests reduced motion
- **THEN** transitions and animations are disabled or shortened

### Requirement: Browser storage holds only UI preferences
The web UI MUST NOT write secret values, access tokens, or refresh tokens to local storage, session storage, or any other script-readable browser storage. Only non-sensitive UI preferences MAY be stored in the browser.

#### Scenario: Preference stored
- **WHEN** the user changes the theme or the list presentation
- **THEN** only that preference is written to browser storage

#### Scenario: Secret value not stored
- **WHEN** a secret value is read for display or copying
- **THEN** no copy of that value remains in browser storage after the operation completes
