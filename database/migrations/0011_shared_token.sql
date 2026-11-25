-- Who still uses the shared instance token of the iPad app (milestone 3, slice 3d). The token has no device behind it, so what is kept is the address it came from (the
-- address as the server sees it, an IPv6 address cut to its network), when it was first and last seen, and nothing else: no token, no path, no content. A row is written at
-- most once a minute for an address, and rows not seen for 30 days are deleted whenever a row is written. The administration shows how many addresses there were in the last 30
-- days before the shared token is switched off, so nobody shuts a device out without knowing it.
create table shared_token_seen (
    address text primary key,
    first_seen timestamptz not null,
    last_seen timestamptz not null
);
create index shared_token_seen_last on shared_token_seen (last_seen);
