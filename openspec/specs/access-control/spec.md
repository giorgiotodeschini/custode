# access-control Specification

## Purpose
Defines who may use which secrets and folders inside a tenant: users are grouped, permissions are granted on folders, folders pass permissions down to their children unless inheritance is broken, and every access decision is made on the effective permissions of the requesting user.

## Requirements

### Requirement: Groups are tenant-scoped
A tenant administrator SHALL be able to create, rename, and delete groups within their tenant. Groups belong to exactly one tenant and MUST NOT be visible to or assignable by users of another tenant.

#### Scenario: Group created by a tenant administrator
- **WHEN** a tenant administrator creates a group with a unique name
- **THEN** the group is stored in that tenant

#### Scenario: Group management by a non-administrator
- **WHEN** a user without the tenant administrator role attempts to create, rename, or delete a group
- **THEN** the request is rejected as forbidden and no group is changed

### Requirement: Users are associated with groups inside their tenant
A tenant administrator SHALL be able to add a user to a group or remove a user from a group. A user MUST only be added to a group of the same tenant.

#### Scenario: User added to a group
- **WHEN** a tenant administrator adds a user of the tenant to a group of the same tenant
- **THEN** the user is a member of that group

#### Scenario: User added to a group of another tenant
- **WHEN** a tenant administrator attempts to add a user to a group belonging to another tenant
- **THEN** the request is rejected and no membership is created

#### Scenario: User removed from a group
- **WHEN** a tenant administrator removes a user from a group
- **THEN** the permissions granted to that group no longer apply to the user on the next request

### Requirement: Permission levels are Read, Write, Create, and Delete
Folder permissions SHALL be one or more of four levels. Read allows viewing secret metadata and reading secret values. Write allows updating a secret's metadata and value. Create allows creating secrets and subfolders in the folder. Delete allows deleting secrets and folders in the folder.

#### Scenario: Read permission reads a secret value
- **WHEN** a user with Read permission on a secret's folder requests the secret value
- **THEN** the value is returned

#### Scenario: Write permission without Read
- **WHEN** a user has Write permission on a folder but not Read permission
- **THEN** the user can update a secret in that folder but cannot read its value

#### Scenario: Create permission required to add a secret
- **WHEN** a user without Create permission on a folder attempts to create a secret in it
- **THEN** the request is rejected as forbidden and nothing is stored

### Requirement: Permissions are granted on folders to users or groups
A tenant administrator SHALL be able to grant or revoke any combination of permission levels on a folder for a user or for a group. A grant applies to the folder and to all of its descendants that inherit from it.

#### Scenario: Group granted Read on a folder
- **WHEN** a tenant administrator grants Read on `/production` to a group
- **THEN** every member of that group can read secrets in `/production` and its subfolders that inherit from it

#### Scenario: Grant to a principal of another tenant
- **WHEN** a tenant administrator attempts to grant permission to a user or group of another tenant
- **THEN** the request is rejected and no grant is stored

### Requirement: Folder permissions are inherited by child folders and secrets
A folder that inherits its permissions SHALL receive the effective permissions of its parent folder in addition to its own grants. Secrets SHALL use the effective permissions of the folder they belong to.

#### Scenario: Child inherits parent grant
- **WHEN** a user has Read on `/production` and the folder `/production/aws` inherits permissions
- **THEN** the user can read secrets in `/production/aws`

#### Scenario: Child adds its own grant
- **WHEN** a folder inheriting from its parent has a grant of Write for a group
- **THEN** members of that group have Write on the folder in addition to the permissions inherited from the parent

### Requirement: A folder can break permission inheritance
A tenant administrator SHALL be able to break inheritance on a folder. A folder with broken inheritance MUST use only its own grants and MUST NOT receive the effective permissions of its parent. The folder's descendants that inherit from it follow its effective permissions.

#### Scenario: Inheritance broken on a folder
- **WHEN** a tenant administrator breaks inheritance on `/production/aws`, which previously inherited Read for a group from `/production`
- **THEN** members of that group no longer have Read on `/production/aws` unless `/production/aws` has its own grant for them

#### Scenario: Descendant of a folder with broken inheritance
- **WHEN** a user has Read only through the inherited grant of an ancestor above a folder with broken inheritance
- **THEN** the user does not have Read on that folder or on its descendants

### Requirement: Inheritance can be restored
A tenant administrator SHALL be able to restore inheritance on a folder whose inheritance is broken. Restoring inheritance MUST take effect for the folder and its descendants on the next request.

#### Scenario: Inheritance restored
- **WHEN** a tenant administrator restores inheritance on `/production/aws`
- **THEN** the folder again receives the effective permissions of `/production`, in addition to its own grants

### Requirement: Effective permissions combine direct and group grants
The effective permissions of a user on a folder SHALL be the union of the permissions granted to the user directly and to every group the user belongs to, as they apply to that folder after inheritance is resolved. Permissions are only granted, never denied, by any entry.

#### Scenario: Permissions from two groups
- **WHEN** a user belongs to group A with Read on a folder and group B with Create on the same folder
- **THEN** the user has both Read and Create on that folder

#### Scenario: No explicit deny
- **WHEN** a group grants Read on a folder and the user has no grant on that folder
- **THEN** the user has Read on that folder through the group, regardless of any other grant the user has

### Requirement: Inaccessible folders and secrets appear as non-existent
A user with no effective permission on a folder or secret SHALL receive the same response as for a folder or secret that does not exist. A user with some permission on a folder but without the permission required for an operation SHALL receive a forbidden response.

#### Scenario: Secret with no permission for the user
- **WHEN** a user with no effective permission on the folder of a secret requests the secret
- **THEN** the response is the same as for a secret that does not exist

#### Scenario: Secret visible without Read
- **WHEN** a user with Write but not Read on the folder of a secret requests the secret value
- **THEN** the request is rejected as forbidden

### Requirement: Access control changes are restricted to tenant administrators
Only a tenant administrator SHALL be able to create or change groups, memberships, folder grants, or inheritance settings. Each of these changes MUST be recorded in the security audit log.

#### Scenario: Grant changed by an administrator
- **WHEN** a tenant administrator grants or revokes a permission on a folder
- **THEN** the change takes effect for the next request and an audit entry is recorded

#### Scenario: Grant changed by a non-administrator
- **WHEN** a user without the tenant administrator role attempts to grant or revoke a permission on a folder
- **THEN** the request is rejected as forbidden and no grant changes

### Requirement: Tenant administration does not imply secret value access
Holding the tenant administrator role SHALL NOT by itself grant Read on any secret value. Administrators who need to read a secret value MUST have a Read grant on its folder.

#### Scenario: Administrator without a Read grant
- **WHEN** a tenant administrator with no effective permission on a folder requests a secret value in that folder
- **THEN** the response is the same as for a secret that does not exist and the attempt is recorded in the security audit log

### Requirement: Administrators can read groups and their members
A tenant administrator SHALL be able to list the groups of the tenant with their member and grant counts, and the members of each group, through the access read endpoints. These reads are restricted to tenant administrators and are not audited.

#### Scenario: Groups listed with counts
- **WHEN** a tenant administrator lists the groups of the tenant
- **THEN** each group is returned with its name, member count, and grant count

#### Scenario: Members of a group listed
- **WHEN** a tenant administrator lists the members of a group of the tenant
- **THEN** each member is returned with its identifier and login

#### Scenario: Group of another tenant
- **WHEN** a tenant administrator lists the members of a group that belongs to another tenant
- **THEN** the response is not found

#### Scenario: Group read by a non-administrator
- **WHEN** a user without the tenant administrator role lists groups
- **THEN** the request is rejected as forbidden

### Requirement: Administrators can read folder grants with their effective permissions
A tenant administrator SHALL be able to read the grants of a folder through `GET /api/access/grants` with the query parameter `folderPath`, with each principal's sources and effective permission levels after inheritance is resolved.

#### Scenario: Inherited grant shown with its source
- **WHEN** a group holds Read on `/production` and `/production/aws` inherits permissions
- **THEN** the entry for `/production/aws` lists the group with a source of `/production` with Read, and the effective level Read

#### Scenario: Direct grant and inherited grant
- **WHEN** a group holds Read on `/production` and Write on `/production/aws`
- **THEN** the group has two sources and its effective levels are Read and Write on `/production/aws`

#### Scenario: Inheritance broken
- **WHEN** `/production/aws` has broken inheritance
- **THEN** the grants of `/production` do not appear as sources for `/production/aws`

#### Scenario: Entry contents
- **WHEN** a tenant administrator reads the grants of a folder
- **THEN** each entry contains the principal kind, identifier, and name, its sources with the holding folder path and levels, and its effective levels

#### Scenario: Unknown folder
- **WHEN** a tenant administrator requests the grants of a folder that does not exist
- **THEN** the response is not found

### Requirement: Administrators can read the inheritance status of folders
A tenant administrator SHALL be able to list every folder of the tenant through `GET /api/access/folders`, with each folder's path, its parent path, and whether it inherits permissions.

#### Scenario: Inheritance status listed
- **WHEN** a tenant administrator lists the folders of the tenant
- **THEN** each folder is returned with its path, parent path, and inheritance flag

### Requirement: Callers can read their own effective permissions on a folder
Every authenticated user SHALL be able to read their own effective permission levels on a folder through `GET /api/access/me/permissions` with the query parameter `folderPath`, computed from direct and group grants after inheritance is resolved.

#### Scenario: Caller reads permissions on a folder
- **WHEN** a user with Read and Create through a group reads their permissions on the folder
- **THEN** the response lists Read and Create

#### Scenario: Other principals not revealed
- **WHEN** a user reads their own permissions on a folder where another user holds grants
- **THEN** the response contains only the caller's levels

#### Scenario: No permission on the folder
- **WHEN** a user with no effective permission on a folder reads their permissions on it
- **THEN** the response is not found, the same as for a folder that does not exist
