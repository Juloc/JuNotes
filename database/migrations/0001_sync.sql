-- One table for everything that syncs: subjects, notebooks, pages and strokes.
-- seq orders the changes; a client pulls everything with a seq above its cursor.
create sequence change_seq;

create table entities (
    kind text not null check (kind in ('subject', 'notebook', 'page', 'stroke')),
    id uuid not null,
    parent_id uuid,
    payload jsonb,
    deleted boolean not null default false,
    seq bigint not null,
    primary key (kind, id),
    check (deleted or payload is not null)
);

create unique index entities_seq on entities (seq);
create index entities_parent on entities (parent_id) where parent_id is not null;
