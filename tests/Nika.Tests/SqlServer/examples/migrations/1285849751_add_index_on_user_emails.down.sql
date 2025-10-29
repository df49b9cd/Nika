IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'users_email_index')
BEGIN
  DROP INDEX users_email_index ON users;
END;
