-- Migration 002: promote first user to ADMIN
--
-- Before this migration, the seeded 'Cashier' user was the only staff member
-- and had role = CASHIER. That meant nobody could reach the Settings page,
-- Staff management, or Audit log. This migration fixes the chicken-and-egg
-- by promoting the oldest active user to ADMIN.
--
-- Idempotent: safe to run multiple times. If any ADMIN already exists, no-op.

UPDATE users
   SET role = 'ADMIN'
 WHERE id = (
    SELECT MIN(id)
      FROM users
     WHERE is_active = 1
 )
   AND NOT EXISTS (
    SELECT 1 FROM users WHERE role = 'ADMIN' AND is_active = 1
 );

-- Update the schema version so migrations stay in order.
UPDATE app_meta SET value = '1.2' WHERE key = 'schema_version';
