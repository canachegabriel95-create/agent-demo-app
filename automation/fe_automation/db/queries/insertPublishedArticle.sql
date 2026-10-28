-- $1 author email, $2 title, $3 summary, $4 body, $5 category name
INSERT INTO articles (id, author_id, category_id, title, summary, body, status,
                      submission_version, submitted_at, published_at, current_version_published_at,
                      published_version, created_at, updated_at)
SELECT gen_random_uuid(), u.id, c.id, $2::text, $3::text, $4::text, 'Published',
       1, now(), now(), now(),
       1, now(), now()
FROM users u, categories c
WHERE u.normalized_email = lower(trim($1::text))
  AND c.name = $5::text
RETURNING id;
