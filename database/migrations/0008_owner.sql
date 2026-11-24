-- Whose is a row. Empty means "belongs to the first administrator": the account of the owner does not exist yet when this runs (it is made in the setup of the base),
-- and until it exists everything behaves as before. When the first account exists, the server gives all empty rows to it in one statement (the claim).
-- Nothing is rewritten: the column is new, the rows stay as they are.
-- There is no foreign key to the people of the base on purpose: deleting a person must not silently delete or block notes; what happens to a person's notes is decided
-- when people can share them (milestone 3).
alter table entities add column owner_id uuid;
create index entities_owner_seq on entities (owner_id, seq);
create index entities_unowned on entities (seq) where owner_id is null;

-- Who owns a file (the scans and PDFs; the files themselves are kept on disk by their id). A file without a row here was made before this table existed and is
-- given to the first administrator by the claim.
create table assets (
    id uuid primary key,
    owner_id uuid not null,
    created_at timestamptz not null default now()
);
create index assets_owner on assets (owner_id);
