-- Flashcards belong to a subject and carry their own review schedule, so they sync like grades do.
alter table entities drop constraint entities_kind_check;
alter table entities add constraint entities_kind_check check (kind in ('subject', 'notebook', 'page', 'stroke', 'grade', 'card'));
