-- JuNotes baseline schema. Frozen at release; changes go into database/migrations/NNNN_name.sql.
-- Phase 0 keeps this to the instance table; notebooks, pages and strokes arrive with Phase 1.

-- Called instance_settings until the base arrived: the base has a table of that name with other columns, so ours (still unused) is renamed by the server at start.
create table app_instance_settings (
    key text primary key,
    value text not null,
    updated_at timestamptz not null default now()
);
