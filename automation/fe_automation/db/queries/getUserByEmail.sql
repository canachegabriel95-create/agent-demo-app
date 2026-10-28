SELECT id, email, full_name, role, status
FROM users
WHERE normalized_email = lower(trim($1));
