-- $1 author email
DELETE FROM articles
WHERE author_id IN (SELECT id FROM users WHERE normalized_email = lower(trim($1::text)));
