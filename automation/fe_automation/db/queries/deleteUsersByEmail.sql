-- $1 array of emails
DELETE FROM users
WHERE normalized_email = ANY (SELECT lower(trim(e)) FROM unnest($1::text[]) AS e);
