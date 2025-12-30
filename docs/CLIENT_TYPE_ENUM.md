# Business Central / Dynamics NAV Client Type Enum

## Standard BC/NAV Schema

The `[Active Session]` table in Business Central and Dynamics NAV uses **integer values** for the `Client Type` column, not strings.

### Verified Schema (NMDALDEV):
```
Client Type : int(N/A) [NO]
```

### Client Type Enum Values

Based on NAV/BC documentation and the Microsoft.Dynamics.Nav.Types.ClientType enum:

| Value | Name | Description |
|-------|------|-------------|
| 0 | Windows | Windows Client (classic NAV client) |
| 1 | SharePoint | SharePoint Client |
| 2 | Web | Web Client (browser-based) |
| 3 | SOAP | SOAP Web Services |
| 4 | OData | OData Web Services |
| 5 | ODataV4 | OData V4 Web Services |
| 6 | Background | Background/Scheduled Tasks |
| 7 | NAS | NAS Services (NAV Application Server) |
| 8 | Tablet | Tablet Client |
| 9 | Phone | Phone Client |
| 10 | Desktop | Desktop Client |
| 11 | Management | Management Client |
| 12 | API | API Web Services |

## Session Type Mapping for Health Monitor

For load balancing purposes, we group these into three categories:

### Web Client Sessions (interactive users):
- `2` - Web
- `0` - Windows (legacy)
- `8` - Tablet
- `9` - Phone
- `10` - Desktop

### Web Service Sessions (API/integration):
- `3` - SOAP
- `4` - OData
- `5` - ODataV4
- `12` - API

### Background Sessions (scheduled tasks):
- `6` - Background
- `7` - NAS
- `11` - Management (administrative)

## Current Implementation Status

Our code correctly handles integer values. The switch statements need to be updated to cover all possible values, not just 2, 3, and 7.

## Sources
- Microsoft Dynamics NAV/BC system tables documentation
- Actual NMDALDEV database schema verification
- Microsoft.Dynamics.Nav.Types.ClientType enumeration
