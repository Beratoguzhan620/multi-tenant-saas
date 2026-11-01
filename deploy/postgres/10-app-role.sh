#!/bin/sh
# Runs once, when the database volume is created. The API connects as this role: it is not the owner of any
# table and not a superuser, so PostgreSQL's row-level security applies to every query it makes.
set -eu
psql -v ON_ERROR_STOP=1 -v app_password="$APP_DB_PASSWORD" --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<'SQL'
CREATE ROLE helpdesk_app LOGIN PASSWORD :'app_password';
SQL
