-- Pictures, text boxes, stickers and recordings on a page sync like strokes do: they belong to a page.
alter table entities drop constraint entities_kind_check;
alter table entities add constraint entities_kind_check check (kind in ('subject', 'notebook', 'page', 'stroke', 'grade', 'card', 'object'));
