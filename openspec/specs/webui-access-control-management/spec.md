# webui-access-control-management Specification

## Purpose

Defines how tenant administrators view and change folder permissions for users and groups, see whether each folder inherits its permissions, and break or restore inheritance, so that every access decision is visible before it is applied.

## Requirements

### Requirement: The folder ACL view is limited to tenant administrators
The folder ACL view SHALL be available only to users with the tenant administrator role. Other users MUST NOT see the ACL of any folder.

#### Scenario: Administrator opens the ACL of a folder
- **WHEN** a tenant administrator opens the ACL view of a folder
- **THEN** the principals and their grants for that folder are listed

#### Scenario: Non-administrator
- **WHEN** a user without the tenant administrator role requests the ACL view
- **THEN** the page is not available

### Requirement: The ACL view lists effective and direct grants with their source
For the selected folder, the ACL view SHALL list every user and group that has a grant on the folder or an inherited grant. Each row SHALL show the principal, its four permission levels (Read, Write, Create, Delete), and whether each level is granted directly on this folder or inherited from an ancestor, including the ancestor's path.

#### Scenario: Inherited grant shown
- **WHEN** a group holds Read on `/production` and `/production/aws` inherits permissions
- **THEN** the `/production/aws` ACL view lists the group with Read marked as inherited from `/production`

#### Scenario: Direct grant shown
- **WHEN** a group holds Write on `/production/aws` directly
- **THEN** the group's Write is marked as set on this folder

### Requirement: Inheritance status is shown for every folder
Each folder in the ACL view SHALL show whether it inherits permissions from its parent or whether inheritance is broken. The folder tree SHALL show the same status for each node.

#### Scenario: Inherited folder
- **WHEN** a folder inherits permissions
- **THEN** its status is shown as "Inheriting from parent"

#### Scenario: Broken folder
- **WHEN** a folder has broken inheritance
- **THEN** its status is shown as "Inheritance broken"

### Requirement: Breaking inheritance shows the impact before it is applied
The "Break Inheritance" toggle SHALL open a confirmation that lists the principals and permission levels that would no longer apply to the folder and its descendants that inherit from it. The change MUST NOT be sent until the administrator confirms.

#### Scenario: Preview of lost access
- **WHEN** the administrator switches "Break Inheritance" on for a folder whose group has inherited Read
- **THEN** the confirmation lists the group and Read as no longer inherited

#### Scenario: Break confirmed
- **WHEN** the administrator confirms the break
- **THEN** the folder shows "Inheritance broken" and its own direct grants remain

#### Scenario: Break cancelled
- **WHEN** the administrator cancels the confirmation
- **THEN** the toggle returns to its previous state and nothing is changed

### Requirement: Restoring inheritance shows the grants that will return
The toggle that restores inheritance SHALL open a confirmation listing the inherited grants that will apply again to the folder and its descendants. The change MUST NOT be sent until the administrator confirms.

#### Scenario: Restore confirmed
- **WHEN** the administrator confirms the restore for a folder whose parent grants Read to a group
- **THEN** the folder shows "Inheriting from parent" and the group's Read is shown as inherited

### Requirement: Access levels are set for users and groups
An administrator SHALL be able to set any combination of Read, Write, Create, and Delete for a user or group on a folder. The editor SHALL offer the presets Viewer (Read), Editor (Read, Write, Create), and Full (Read, Write, Create, Delete), and SHALL allow a custom combination. Changes SHALL be shown only after the server confirms them.

#### Scenario: Preset applied
- **WHEN** the administrator applies the Editor preset to a group on a folder
- **THEN** the group holds Read, Write, and Create on that folder after the server confirms the change

#### Scenario: Custom combination
- **WHEN** the administrator sets only Delete for a user on a folder
- **THEN** the user holds only Delete as a direct grant on that folder

### Requirement: Revoking a grant requires confirmation
Removing a principal's direct grant on a folder SHALL require confirmation that names the principal and the folder. The grant is removed on the next request after the server confirms.

#### Scenario: Grant revoked
- **WHEN** the administrator confirms the revocation of a group's grant on a folder
- **THEN** the grant no longer appears in the folder's direct grants

### Requirement: Refused ACL changes do not change the displayed state
If the server refuses an ACL change, the view SHALL show the server's reason and keep the displayed state equal to the server's current state. The interface MUST NOT show a permission change that the server has not confirmed.

#### Scenario: Change refused
- **WHEN** the server refuses a grant change with forbidden
- **THEN** the view shows the refusal and the grant table is unchanged

### Requirement: Group and member selection is limited to the tenant
The principal selector in the ACL view SHALL offer only users and groups of the signed-in tenant.

#### Scenario: Principal selector
- **WHEN** the administrator opens the principal selector
- **THEN** only users and groups of the current tenant are listed
