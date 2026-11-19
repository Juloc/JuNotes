-- The admin's settings for the learning helpers: one profile per task. The server fills in the defaults on its first start.
create table ai_profiles (
    key text primary key,
    name text not null,
    system_prompt text not null,
    difficulty text not null default 'mittel',
    tone text not null default 'freundlich',
    enabled boolean not null default true,
    updated_at timestamptz not null default now()
);

-- How many requests each day, for the daily limit and the admin's overview. No content is kept.
create table ai_usage (
    day date primary key,
    requests integer not null default 0,
    failures integer not null default 0
);
