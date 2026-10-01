CREATE TABLE IF NOT EXISTS greeting (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    name TEXT NOT NULL,
    city TEXT NOT NULL
);

INSERT INTO greeting (id, name, city)
VALUES (1, 'Hello from hob', 'London')
ON CONFLICT(id) DO NOTHING;
