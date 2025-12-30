-- Grant SQL permissions to service account
USE [NMDALDEV]
GO

-- Get the computer name for the login
DECLARE @computerName NVARCHAR(128) = CAST(SERVERPROPERTY('MachineName') AS NVARCHAR(128))
DECLARE @serviceAccount NVARCHAR(256) = @computerName + '\l_lscentral'

PRINT 'Granting permissions to: ' + @serviceAccount

-- Create login if doesn't exist
IF NOT EXISTS (SELECT * FROM sys.server_principals WHERE name = @serviceAccount)
BEGIN
    DECLARE @createLoginSQL NVARCHAR(MAX) = 'CREATE LOGIN [' + @serviceAccount + '] FROM WINDOWS'
    EXEC sp_executesql @createLoginSQL
    PRINT 'âœ“ Created login for ' + @serviceAccount
END
ELSE
BEGIN
    PRINT 'âœ“ Login already exists for ' + @serviceAccount
END
GO

-- Create user in database
USE [NMDALDEV]
GO

DECLARE @computerName NVARCHAR(128) = CAST(SERVERPROPERTY('MachineName') AS NVARCHAR(128))
DECLARE @serviceAccount NVARCHAR(256) = @computerName + '\l_lscentral'
DECLARE @userName NVARCHAR(128) = 'l_lscentral'

IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = @userName)
BEGIN
    DECLARE @createUserSQL NVARCHAR(MAX) = 'CREATE USER [' + @userName + '] FOR LOGIN [' + @serviceAccount + ']'
    EXEC sp_executesql @createUserSQL
    PRINT 'âœ“ Created user ' + @userName
END
ELSE
BEGIN
    PRINT 'âœ“ User already exists: ' + @userName
END
GO

-- Grant read permissions
DECLARE @userName NVARCHAR(128) = 'l_lscentral'
DECLARE @addMemberSQL NVARCHAR(MAX) = 'ALTER ROLE [db_datareader] ADD MEMBER [' + @userName + ']'
EXEC sp_executesql @addMemberSQL
PRINT 'âœ“ Added to db_datareader role'

-- Specific permission for Active Session table
DECLARE @grantSelectSQL NVARCHAR(MAX) = 'GRANT SELECT ON [dbo].[Active Session] TO [' + @userName + ']'
EXEC sp_executesql @grantSelectSQL
PRINT 'âœ“ Granted SELECT on [Active Session] table'

PRINT ''
PRINT '=== Permissions granted successfully ==='
PRINT 'Next step: Restart BCHealthMonitor service'
GO
