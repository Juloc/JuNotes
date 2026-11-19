-- What the server fetches from the school: the timetable as calendar events and the school's news.
-- Each source is replaced as a whole on every successful fetch, so nothing stale is left behind.
create table calendar_events (
    source text not null,
    uid text not null,
    starts_at timestamptz not null,
    ends_at timestamptz not null,
    summary text not null,
    location text,
    description text,
    cancelled boolean not null default false,
    all_day boolean not null default false,
    primary key (source, uid, starts_at)
);

create index calendar_events_time on calendar_events (starts_at);

create table school_news (
    source text not null,
    guid text not null,
    title text not null,
    link text,
    published_at timestamptz,
    summary text,
    primary key (source, guid)
);

create index school_news_published on school_news (published_at desc);
