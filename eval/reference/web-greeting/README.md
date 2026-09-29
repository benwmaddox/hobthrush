# Greeting Service

Run the managed web package with `lang run eval/reference/web-greeting --
--urls http://127.0.0.1:5000`. Set `LANG_SQLITE_PATH` to use an isolated
database file.

| Method | Path | Result |
| --- | --- | --- |
| GET | `/health` | 200, no body |
| GET | `/api/greeting` | 200 JSON when saved; otherwise 404 |
| POST | `/api/greeting` | 201 JSON when saved; 400 when name or city is blank after trimming |
| GET | `/` | 200 HTML showing the saved greeting or an empty-state message |

Posting a new valid greeting replaces the existing one. The record is stored
in SQLite and survives a server restart. Names and cities are trimmed before
storage. The page uses safe HTML builders, which escape user text.

The package requires `net.listen`, `db.read`, and `db.write`. Its compiler
reports describe checked facts and trust claims; they do not prove adapter
behavior or provide an operating-system sandbox.
