-- Grades sync like everything else she writes: they belong to a subject.
alter table entities drop constraint entities_kind_check;
alter table entities add constraint entities_kind_check check (kind in ('subject', 'notebook', 'page', 'stroke', 'grade'));
