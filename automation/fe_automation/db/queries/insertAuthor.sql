-- $1 full name, $2 email, $3 status ('Active' | 'Suspended')
INSERT INTO users (id, email, normalized_email, password_hash, full_name, role, status,
                   suspension_reason, suspended_at, created_at, updated_at)
VALUES (gen_random_uuid(), $2::text, lower(trim($2::text)), 'not-a-real-hash', $1::text, 'Author', $3::text,
        CASE WHEN $3::text = 'Suspended' THEN 'Suspended by automated test' END,
        CASE WHEN $3::text = 'Suspended' THEN now() END,
        now(), now())
RETURNING id;
