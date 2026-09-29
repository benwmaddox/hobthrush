# Task

Build a managed `lang` web package for one saved greeting. `GET /health`
returns 200. `GET /api/greeting` returns 404 before a greeting exists and
returns the saved greeting as JSON afterward. `POST /api/greeting` accepts a
JSON object with `name` and `city` text fields. Trim both values; return 400
when either is blank after trimming, without changing the saved greeting.
Otherwise save or replace the greeting and return 201 JSON. The saved greeting
must survive a server restart.

`GET /` shows the saved name and city in an HTML page. User-provided text must
be rendered as text, never interpreted as markup. Include a README table that
documents the routes and their status codes.
