-- JuNotes baseline schema. Frozen at release; changes go into database/migrations/NNNN_name.sql.
-- Phase 0 keeps this to the instance table; notebooks, pages and strokes arrive with Phase 1.

create table instance_settings (
    key text primary key,
    value text not null,
    updated_at timestamptz not null default now()
);
