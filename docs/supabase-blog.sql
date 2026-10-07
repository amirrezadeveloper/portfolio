-- Already applied remotely; reference only.

create schema if not exists private;
revoke all on schema private from public, anon, authenticated;
grant usage on schema private to authenticated, service_role;

create table private.blog_admins (
  user_id uuid primary key references auth.users(id) on delete cascade,
  created_at timestamptz not null default now()
);
alter table private.blog_admins enable row level security;
revoke all on table private.blog_admins from public, anon, authenticated;
grant select on table private.blog_admins to authenticated;
grant all on table private.blog_admins to service_role;
create policy blog_admins_read_self on private.blog_admins
  for select to authenticated
  using (user_id = (select auth.uid()));

create table public.blog_posts (
  id uuid primary key default gen_random_uuid(),
  author_id uuid not null default auth.uid() references auth.users(id),
  title text not null check (length(trim(title)) between 1 and 250),
  slug text not null check (length(slug) between 1 and 160 and slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'),
  language text not null default 'fa' check (language in ('fa', 'en')),
  excerpt text not null default '',
  content_markdown text not null default '',
  tags text[] not null default '{}',
  cover_path text,
  status text not null default 'draft' check (status in ('draft', 'published', 'archived')),
  published_at timestamptz,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  constraint blog_posts_language_slug_unique unique (language, slug),
  constraint blog_posts_published_date_required check (status <> 'published' or published_at is not null)
);
create index blog_posts_published_listing on public.blog_posts (language, published_at desc, id)
  where status = 'published';
create index blog_posts_author_id_idx on public.blog_posts (author_id);

alter table public.blog_posts enable row level security;
revoke all on table public.blog_posts from public, anon, authenticated;
grant select on table public.blog_posts to anon;
grant select, insert, update, delete on table public.blog_posts to authenticated;
grant all on table public.blog_posts to service_role;

create policy blog_posts_read_published on public.blog_posts
  for select to anon, authenticated
  using (status = 'published' and published_at <= now());

create policy blog_posts_admin_manage on public.blog_posts
  for all to authenticated
  using (exists (
    select 1 from private.blog_admins where user_id = (select auth.uid())
  ))
  with check (exists (
    select 1 from private.blog_admins where user_id = (select auth.uid())
  ));

create function private.touch_blog_post_updated_at()
returns trigger language plpgsql security invoker set search_path = ''
as $fn$
begin
  new.updated_at := pg_catalog.now();
  return new;
end;
$fn$;
revoke all on function private.touch_blog_post_updated_at() from public;
grant execute on function private.touch_blog_post_updated_at() to authenticated, service_role;
create trigger blog_posts_touch_updated_at
  before update on public.blog_posts
  for each row execute function private.touch_blog_post_updated_at();

comment on table public.blog_posts is 'Portfolio blog posts: public reads published articles, allowlisted admins manage content.';
comment on table private.blog_admins is 'Managed only by privileged operators. Authentication alone does not grant blog administration.';


drop policy blog_posts_read_published on public.blog_posts;
drop policy blog_posts_admin_manage on public.blog_posts;
create policy blog_posts_anon_read_published on public.blog_posts
  for select to anon
  using (status = 'published' and published_at <= now());
create policy blog_posts_authenticated_read on public.blog_posts
  for select to authenticated
  using (
    (status = 'published' and published_at <= now())
    or exists (select 1 from private.blog_admins where user_id = (select auth.uid()))
  );
create policy blog_posts_admin_insert on public.blog_posts
  for insert to authenticated
  with check (exists (select 1 from private.blog_admins where user_id = (select auth.uid())));
create policy blog_posts_admin_update on public.blog_posts
  for update to authenticated
  using (exists (select 1 from private.blog_admins where user_id = (select auth.uid())))
  with check (exists (select 1 from private.blog_admins where user_id = (select auth.uid())));
create policy blog_posts_admin_delete on public.blog_posts
  for delete to authenticated
  using (exists (select 1 from private.blog_admins where user_id = (select auth.uid())));



-- Applied as add_blog_admin_check.
create or replace function public.is_blog_admin()
returns boolean language sql stable security invoker set search_path = ''
as $$ select exists (select 1 from private.blog_admins where user_id = (select auth.uid())); $$;
revoke all on function public.is_blog_admin() from public, anon;
grant execute on function public.is_blog_admin() to authenticated;
