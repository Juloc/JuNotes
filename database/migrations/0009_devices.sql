-- Devices of a person (the iPad app) and the one-time codes that pair them (milestone 3, slice 3a).
-- A device token is 256 random bits; only its SHA-256 is stored, so this table cannot be used to sign in. Removing a device sets revoked_at (the row stays for the log).
-- There is no foreign key to the people of the base on purpose, as with owner_id: deleting a person is decided in one place (milestone 3, slice 3b), not by a cascade.
create table device_tokens (
    id uuid primary key,
    person_id uuid not null,
    name text not null,
    hash text not null unique,
    created_at timestamptz not null default now(),
    last_seen timestamptz,
    last_address text,
    revoked_at timestamptz
);
create index device_tokens_person on device_tokens (person_id) where revoked_at is null;

-- A pairing code lives ten minutes or until its first use, and is then deleted (not marked). Only its SHA-256 is stored. A person has at most one live code.
create table pairing_codes (
    id uuid primary key,
    person_id uuid not null,
    hash text not null,
    expires_at timestamptz not null,
    created_at timestamptz not null default now()
);
create unique index pairing_codes_person on pairing_codes (person_id);
create index pairing_codes_expires on pairing_codes (expires_at);
