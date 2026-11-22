-- The admin can choose the model and how hard it thinks, for everything or for one task, and change the daily limit.
-- Empty means: the account's own default (model, effort) or the value from the environment (limit).
alter table ai_profiles add column model text;
alter table ai_profiles add column effort text;

create table ai_settings (
    key text primary key,
    value text not null
);
