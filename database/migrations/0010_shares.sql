-- Sharing one notebook with another person, read only (milestone 3, slice 3c). The share lives here and not in the rows of the owner: the sync never carries it, and the
-- iPad of the recipient does not get the notebook (that is a later step). Revoking sets revoked_at (the row stays for the log); a person who shares again gets a new row.
-- No foreign keys to the people of the base, as with owner_id; what happens to the shares of a deleted person is decided with the deleting of people (slice 3b).
create table shares (
    id uuid primary key,
    notebook_id uuid not null,
    owner_id uuid not null,
    recipient_id uuid not null,
    mode text not null default 'read' check (mode = 'read'),
    created_at timestamptz not null default now(),
    revoked_at timestamptz
);
-- At most one live share of a notebook with the same person.
create unique index shares_live on shares (notebook_id, recipient_id) where revoked_at is null;
create index shares_recipient on shares (recipient_id) where revoked_at is null;
create index shares_owner on shares (owner_id) where revoked_at is null;
