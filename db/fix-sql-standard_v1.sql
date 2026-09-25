-- =========================================================================
-- PROMPTVAULT MERGE — SCHEMA CHUAN (v1), DA SUA THEO SPEC
-- Xem: docs/superpowers/specs/2026-09-25-promptvault-merge-design.md
--      docs/superpowers/plans/2026-09-25-promptvault-space-and-prompt-domain.md
--      docs/superpowers/plans/2026-09-25-promptvault-sync-engine.md
--
-- File nay la TAI LIEU THAM CHIEU cho schema cuoi cung, KHONG phai script chay
-- truc tiep de tao database tu dau. Nguon su that thuc su la EF Core migration
-- (dotnet ef migrations add ...) sinh theo 2 plan noi tren — xem
-- docs/database.md va spec muc 8. File nay gia dinh cac bang sau DA TON TAI
-- (tao boi migration EF cua AioKin, KHONG duoc tao lai o day):
--   - security.users, security.roles, security.staff   (AioKin that, EF)
--   - family.families, family.family_members, family.family_invites (M0, EF)
--
-- BA CHO DA SUA SO VOI db/init-postgres.sql ban dau (spec muc 3):
--   1. KHONG con tao security.users rieng — bang do la mot the gioi khac voi
--      security.users that cua AioKin (thieu UserCode/UserUUID/OAuth/lockout...).
--      Moi FK ve "security.users(user_id)" trong file nay tro thang vao bang
--      that cua AioKin.
--   2. BO HOAN TOAN ROW LEVEL SECURITY (RLS). Ban goc dung auth.uid() — ham cua
--      Supabase Auth. AioKin KHONG dung Supabase Auth (chi dung Supabase de
--      host Postgres/Storage — xem docs/overview.md, docs/auth-opaque-tokens-
--      biometric.md), backend dung 1 connection pool chung nen auth.uid() luon
--      NULL. Phan quyen nam o tang API (.NET thuan): [Authorize], CASL/
--      IPermissionService, ISpaceContext/IFamilyContext.
--   3. Trigger tang "version" chi chay khi cot NOI DUNG thay doi (WHEN clause),
--      khong chay tren moi UPDATE — tranh conflict gia khi chi doi has_conflict/
--      is_favorite/usage_count.
--
-- CAC THAY DOI KHAC DA CHOT TRONG QUA TRINH THIET KE:
--   - vault.spaces: Space tong quat hoa Family — space_type='family' chi TRO
--     TOI family.families qua family_id, KHONG tu tao lai co che thanh vien.
--     space_members CHI dung cho space_type='team' (role rut gon con 3:
--     owner/admin/member, thay vi 5 role o ban goc).
--   - vault.prompts/categories/tags/prompt_variables: id DO CLIENT TU SINH
--     (khong con DEFAULT gen_random_uuid(), khong con cot local_id) — tranh
--     phai anh xa local_id -> id server sau khi dong bo offline.
--   - vault.prompts: them content_size_bytes/is_externalized/content_storage_
--     path (tiered storage) va has_conflict (co dua vao sync.sync_conflicts).
--   - sync.sync_conflicts: KHONG dung Last-Write-Wins — moi lech version deu
--     la conflict, giu ca 2 ban, user tu chon (giong Git tu choi push khong
--     fast-forward).
--   - sync.backup_snapshots: them snapshot_type='sync_catchup', tai dung cho
--     pull-fallback khi cursor qua cu (da bi cron xoa) hoac so dong tra ve
--     vuot nguong (mac dinh 500).
--   - vault.prompt_versions: GIU LAI trong schema (chua bi xoa) nhung CHUA
--     duoc wire vao ung dung — hoan lai theo spec muc 2, khong phai loi sot.
-- =========================================================================

START TRANSACTION;

-- =========================================================================
-- 1. SCHEMAS — security/family da co san (AioKin/EF). vault, sync la moi.
-- =========================================================================
DO $EF$ BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'vault') THEN CREATE SCHEMA vault; END IF;
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'sync') THEN CREATE SCHEMA sync; END IF;
END $EF$;

CREATE EXTENSION IF NOT EXISTS "pgcrypto";  -- gen_random_uuid()


-- =========================================================================
-- 2. SCHEMA: SECURITY — CHI THEM MOI, KHONG DUNG LAI security.users
-- =========================================================================

-- user_identities: ho tro multi-login (Google/Facebook/Email-OTP/Password).
-- AioKin da co OAuth rieng qua Services/Auth/OAuth — bang nay la lop du lieu
-- bo sung neu can hop nhat nhieu provider cho cung 1 user_id, KHONG thay the
-- co che OAuth hien co.
CREATE TABLE security.user_identities (
    identity_id uuid NOT NULL DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL,
    provider character varying(20) NOT NULL,         -- 'google' | 'facebook' | 'email_password' | 'email_otp'
    provider_user_id character varying(100),
    provider_email character varying(100),
    is_primary boolean NOT NULL DEFAULT false,
    linked_at timestamp with time zone NOT NULL DEFAULT now(),
    last_used_at timestamp with time zone,
    CONSTRAINT pk_user_identities PRIMARY KEY (identity_id),
    CONSTRAINT fk_user_identities_users FOREIGN KEY (user_id) REFERENCES security.users (user_id) ON DELETE CASCADE,
    CONSTRAINT uq_user_identities_provider UNIQUE (provider, provider_user_id)
);
CREATE INDEX ix_user_identities_user_id ON security.user_identities (user_id);

-- user_profiles: PERSIST giao dien sang/toi + tuy chon ca nhan, tach khoi bang auth that.
CREATE TABLE security.user_profiles (
    profile_id uuid NOT NULL DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL,
    display_name character varying(120),
    avatar_url character varying(500),
    theme_mode character varying(10) NOT NULL DEFAULT 'system',  -- 'light' | 'dark' | 'system'
    accent_color character varying(20) DEFAULT 'default',
    locale character varying(10) NOT NULL DEFAULT 'vi-VN',
    font_scale numeric(3,2) NOT NULL DEFAULT 1.00,
    editor_settings jsonb NOT NULL DEFAULT '{}'::jsonb,
    last_active_space_id uuid,                            -- ghi nho space dang mo lan cuoi (vault.spaces)
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_device_id character varying(100),
    CONSTRAINT pk_user_profiles PRIMARY KEY (profile_id),
    CONSTRAINT fk_user_profiles_users FOREIGN KEY (user_id) REFERENCES security.users (user_id) ON DELETE CASCADE,
    CONSTRAINT uq_user_profiles_user_id UNIQUE (user_id),
    CONSTRAINT ck_user_profiles_theme_mode CHECK (theme_mode IN ('light', 'dark', 'system'))
);
CREATE INDEX ix_user_profiles_user_id ON security.user_profiles (user_id);


-- =========================================================================
-- 3. SCHEMA: VAULT — Space (tong quat hoa Family), Prompt domain
-- =========================================================================

-- spaces: Personal | Family | Team. Family KHONG tu tao lai thanh vien —
-- chi tro toi family.families qua family_id, nguon that ve thanh vien van
-- la family.family_members (ISpaceContext uy quyen cho IFamilyContext).
CREATE TABLE vault.spaces (
    space_id uuid NOT NULL DEFAULT gen_random_uuid(),
    space_uuid uuid NOT NULL DEFAULT gen_random_uuid(),   -- id cong khai, moi route/DTO dung no
    space_type character varying(10) NOT NULL,             -- 'personal' | 'family' | 'team'
    name character varying(120) NOT NULL,
    owner_user_id uuid NOT NULL,
    family_id uuid,                                          -- CHI co gia tri khi space_type = 'family'
    is_active boolean NOT NULL DEFAULT true,
    created_date timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_spaces PRIMARY KEY (space_id),
    CONSTRAINT fk_spaces_users_owner FOREIGN KEY (owner_user_id) REFERENCES security.users (user_id) ON DELETE RESTRICT,
    CONSTRAINT fk_spaces_families FOREIGN KEY (family_id) REFERENCES family.families (family_id) ON DELETE CASCADE,
    CONSTRAINT ck_spaces_type CHECK (space_type IN ('personal', 'family', 'team'))
);
CREATE UNIQUE INDEX ix_spaces_uuid ON vault.spaces (space_uuid);
-- 1 family = 1 space.
CREATE UNIQUE INDEX ix_spaces_family_id ON vault.spaces (family_id) WHERE family_id IS NOT NULL;
-- 1 personal space moi user — chan race luc tu tao (EnsureMyPersonalSpaceAsync) o tang DB.
CREATE UNIQUE INDEX ix_spaces_owner_personal_unique ON vault.spaces (owner_user_id) WHERE space_type = 'personal';
CREATE INDEX ix_spaces_type ON vault.spaces (space_type);

-- space_members: CHI dung cho space_type = 'team'. Family dung family_members,
-- Personal khong co bang thanh vien (owner la thanh vien duy nhat, ngam dinh).
-- Role rut gon con 3 (owner/admin/member) — vai tro Family (Owner/Adult/Child)
-- khong hop voi ngu canh nhom lam viec nen KHONG dung chung.
CREATE TABLE vault.space_members (
    space_member_id uuid NOT NULL DEFAULT gen_random_uuid(),
    space_id uuid NOT NULL,
    user_id uuid NOT NULL,
    member_role character varying(20) NOT NULL,   -- 'owner' | 'admin' | 'member'
    joined_date timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_space_members PRIMARY KEY (space_member_id),
    CONSTRAINT fk_space_members_spaces FOREIGN KEY (space_id) REFERENCES vault.spaces (space_id) ON DELETE CASCADE,
    CONSTRAINT fk_space_members_users FOREIGN KEY (user_id) REFERENCES security.users (user_id) ON DELETE CASCADE,
    CONSTRAINT uq_space_members_space_user UNIQUE (space_id, user_id),
    CONSTRAINT ck_space_members_role CHECK (member_role IN ('owner', 'admin', 'member'))
);
CREATE INDEX ix_space_members_user_id ON vault.space_members (user_id);
CREATE INDEX ix_space_members_space_role ON vault.space_members (space_id, member_role);

-- categories: id DO CLIENT TU SINH (khong DEFAULT) — xem ghi chu dau file.
CREATE TABLE vault.categories (
    category_id uuid NOT NULL,
    space_id uuid NOT NULL,
    name character varying(80) NOT NULL,
    icon character varying(50),
    color character varying(20),
    sort_order integer NOT NULL DEFAULT 0,
    created_date timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_categories PRIMARY KEY (category_id),
    CONSTRAINT fk_categories_spaces FOREIGN KEY (space_id) REFERENCES vault.spaces (space_id) ON DELETE CASCADE,
    CONSTRAINT uq_categories_space_name UNIQUE (space_id, name)
);
CREATE INDEX ix_categories_space_id ON vault.categories (space_id);

-- tags: id DO CLIENT TU SINH.
CREATE TABLE vault.tags (
    tag_id uuid NOT NULL,
    space_id uuid NOT NULL,
    name character varying(50) NOT NULL,
    created_date timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_tags PRIMARY KEY (tag_id),
    CONSTRAINT fk_tags_spaces FOREIGN KEY (space_id) REFERENCES vault.spaces (space_id) ON DELETE CASCADE,
    CONSTRAINT uq_tags_space_name UNIQUE (space_id, name)
);
CREATE INDEX ix_tags_space_id ON vault.tags (space_id);

-- prompts: id DO CLIENT TU SINH va dung nguyen lam id vinh vien — khong con
-- local_id, khong con anh xa local->server sau dong bo (xem spec muc 6.1).
CREATE TABLE vault.prompts (
    prompt_id uuid NOT NULL,
    space_id uuid NOT NULL,
    category_id uuid,
    author_user_id uuid NOT NULL,
    title character varying(200) NOT NULL,
    content text NOT NULL,                       -- prompt goc, chua placeholder {var}
    description character varying(500),
    is_favorite boolean NOT NULL DEFAULT false,
    is_archived boolean NOT NULL DEFAULT false,
    usage_count integer NOT NULL DEFAULT 0,
    -- --- Tiered storage (tam luu trong DB, > 8KB day ra Storage) ---
    content_size_bytes integer,
    is_externalized boolean NOT NULL DEFAULT false,
    content_storage_path character varying(500),         -- vd: 'prompts/{space_id}/{prompt_id}.md.gz'
    -- --- Sync metadata: KHONG Last-Write-Wins ---
    version integer NOT NULL DEFAULT 1,            -- base_version cho /sync/push; lech = conflict
    is_deleted boolean NOT NULL DEFAULT false,     -- soft-delete de dong bo xoa giua cac thiet bi
    has_conflict boolean NOT NULL DEFAULT false,   -- true khi co dong chua resolve trong sync_conflicts
    created_date timestamp with time zone NOT NULL DEFAULT now(),
    updated_date timestamp with time zone NOT NULL DEFAULT now(),
    updated_device_id character varying(100),
    CONSTRAINT pk_prompts PRIMARY KEY (prompt_id),
    CONSTRAINT fk_prompts_spaces FOREIGN KEY (space_id) REFERENCES vault.spaces (space_id) ON DELETE CASCADE,
    CONSTRAINT fk_prompts_categories FOREIGN KEY (category_id) REFERENCES vault.categories (category_id) ON DELETE SET NULL,
    CONSTRAINT fk_prompts_users_author FOREIGN KEY (author_user_id) REFERENCES security.users (user_id) ON DELETE RESTRICT
);
CREATE INDEX ix_prompts_space_id ON vault.prompts (space_id);
CREATE INDEX ix_prompts_category_id ON vault.prompts (category_id);
CREATE INDEX ix_prompts_author_user_id ON vault.prompts (author_user_id);
CREATE INDEX ix_prompts_space_favorite ON vault.prompts (space_id, is_favorite) WHERE is_deleted = false;
CREATE INDEX ix_prompts_space_updated ON vault.prompts (space_id, updated_date) WHERE is_deleted = false;
-- Full-Text Search. LUU Y: khi is_externalized = true, content chi con preview 200 ky tu —
-- prompt da externalize chi tim duoc theo title + preview, khong theo toan van tren Storage.
CREATE INDEX ix_prompts_fts ON vault.prompts USING GIN (to_tsvector('simple', title || ' ' || content));

-- prompt_variables: id DO CLIENT TU SINH.
CREATE TABLE vault.prompt_variables (
    variable_id uuid NOT NULL,
    prompt_id uuid NOT NULL,
    var_key character varying(50) NOT NULL,       -- 'product_name'
    label character varying(100),
    default_value character varying(500),
    var_type character varying(20) NOT NULL DEFAULT 'text', -- 'text' | 'number' | 'select'
    options jsonb,                                   -- cho var_type = 'select': ["A","B","C"]
    sort_order integer NOT NULL DEFAULT 0,
    CONSTRAINT pk_prompt_variables PRIMARY KEY (variable_id),
    CONSTRAINT fk_prompt_variables_prompts FOREIGN KEY (prompt_id) REFERENCES vault.prompts (prompt_id) ON DELETE CASCADE,
    CONSTRAINT uq_prompt_variables_prompt_key UNIQUE (prompt_id, var_key)
);
CREATE INDEX ix_prompt_variables_prompt_id ON vault.prompt_variables (prompt_id);

-- prompt_tags: nhieu-nhieu Prompt-Tag. KHONG phai entity_type rieng trong
-- sync_log — dong bo nhu mot phan cua payload Prompt (xem sync-engine plan).
CREATE TABLE vault.prompt_tags (
    prompt_id uuid NOT NULL,
    tag_id uuid NOT NULL,
    CONSTRAINT pk_prompt_tags PRIMARY KEY (prompt_id, tag_id),
    CONSTRAINT fk_prompt_tags_prompts FOREIGN KEY (prompt_id) REFERENCES vault.prompts (prompt_id) ON DELETE CASCADE,
    CONSTRAINT fk_prompt_tags_tags FOREIGN KEY (tag_id) REFERENCES vault.tags (tag_id) ON DELETE CASCADE
);
CREATE INDEX ix_prompt_tags_tag_id ON vault.prompt_tags (tag_id);

-- prompt_versions: GIU trong schema, CHUA wire vao ung dung (hoan lai — spec
-- muc 2). Khong nam trong migration EF cua 2 plan hien tai.
CREATE TABLE vault.prompt_versions (
    version_id uuid NOT NULL DEFAULT gen_random_uuid(),
    prompt_id uuid NOT NULL,
    version_number integer NOT NULL,
    title character varying(200) NOT NULL,
    content text,                                     -- optional: NULL khi da externalize hoan toan
    content_storage_path character varying(500),
    edited_by_user_id uuid NOT NULL,
    created_date timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_prompt_versions PRIMARY KEY (version_id),
    CONSTRAINT fk_prompt_versions_prompts FOREIGN KEY (prompt_id) REFERENCES vault.prompts (prompt_id) ON DELETE CASCADE,
    CONSTRAINT fk_prompt_versions_users FOREIGN KEY (edited_by_user_id) REFERENCES security.users (user_id) ON DELETE RESTRICT,
    CONSTRAINT uq_prompt_versions_prompt_version UNIQUE (prompt_id, version_number)
);
CREATE INDEX ix_prompt_versions_prompt_id ON vault.prompt_versions (prompt_id, version_number DESC);


-- =========================================================================
-- 4. SCHEMA: SYNC — Device registry, change-feed, conflict, snapshot
-- =========================================================================

-- devices: so dang ky thiet bi duy nhat. Neu 3 plan auth (opaque token/
-- session/biometric) da trien khai, security.device_credentials nen tro ve
-- day thay vi tu luu ten/nen tang rieng (spec muc 6.5) — khong bat buoc cho
-- 2 plan PromptVault tu chay duoc mot minh.
CREATE TABLE sync.devices (
    device_id character varying(100) NOT NULL,   -- id client tu sinh, giu on dinh
    user_id uuid NOT NULL,
    device_name character varying(120),
    platform character varying(20),                -- 'android' | 'ios' | 'web' ...
    app_version character varying(20),
    last_synced_at timestamp with time zone,
    is_stale boolean NOT NULL DEFAULT false,        -- true khi offline > 90 ngay — loai khoi retention
    created_date timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_devices PRIMARY KEY (device_id),
    CONSTRAINT fk_devices_users FOREIGN KEY (user_id) REFERENCES security.users (user_id) ON DELETE CASCADE
);
CREATE INDEX ix_devices_user_id ON sync.devices (user_id);
CREATE INDEX ix_devices_last_synced ON sync.devices (last_synced_at) WHERE is_stale = false;

-- sync_log: nhat ky thay doi (change feed) — CHI entity_type = 'prompt' duoc
-- ghi boi 2 plan hien tai (category/tag di kem trong payload prompt, khong
-- tach rieng — xem sync-engine plan, muc "Scope narrowing").
CREATE TABLE sync.sync_log (
    sync_log_id bigint GENERATED ALWAYS AS IDENTITY,
    space_id uuid NOT NULL,
    entity_type character varying(30) NOT NULL,   -- 'prompt' | 'category' | 'tag' | 'prompt_variable'
    entity_id uuid NOT NULL,
    operation character varying(10) NOT NULL,       -- 'insert' | 'update' | 'delete'
    payload jsonb,                                   -- snapshot du lieu tai thoi diem thay doi
    origin_device_id character varying(100),
    version integer NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_sync_log PRIMARY KEY (sync_log_id),
    CONSTRAINT ck_sync_log_operation CHECK (operation IN ('insert', 'update', 'delete'))
);
CREATE INDEX ix_sync_log_space_created ON sync.sync_log (space_id, created_at);
CREATE INDEX ix_sync_log_entity ON sync.sync_log (entity_type, entity_id);

-- sync_conflicts: KHONG dung Last-Write-Wins. Moi lan push lech version deu
-- la conflict — giong Git tu choi push khong fast-forward. Live row giu
-- nguyen ban remote; ca 2 ban luu o day de user tu chon 'keep_local' |
-- 'keep_remote' | 'merged'. vault.prompts.has_conflict = true trong luc
-- dong nay resolved = false.
CREATE TABLE sync.sync_conflicts (
    conflict_id uuid NOT NULL DEFAULT gen_random_uuid(),
    entity_type character varying(30) NOT NULL,
    entity_id uuid NOT NULL,
    local_payload jsonb NOT NULL,
    remote_payload jsonb NOT NULL,
    local_version integer NOT NULL,
    remote_version integer NOT NULL,
    resolved boolean NOT NULL DEFAULT false,
    resolution_strategy character varying(20),       -- 'keep_local' | 'keep_remote' | 'merged'
    resolved_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_sync_conflicts PRIMARY KEY (conflict_id)
);
CREATE INDEX ix_sync_conflicts_entity ON sync.sync_conflicts (entity_type, entity_id) WHERE resolved = false;

-- backup_snapshots: backup dinh ky/theo yeu cau, VA pull-fallback cua sync
-- (snapshot_type = 'sync_catchup'): khi cursor cua client da bi cron xoa mat
-- (retention) hoac so dong phai tra vuot nguong (mac dinh 500), server tra
-- snapshot moi nhat thay vi replay sync_log — khong can bang rieng.
CREATE TABLE sync.backup_snapshots (
    snapshot_id uuid NOT NULL DEFAULT gen_random_uuid(),
    space_id uuid NOT NULL,
    triggered_by_user_id uuid NOT NULL,
    snapshot_type character varying(20) NOT NULL,   -- 'manual' | 'scheduled' | 'pre_sync' | 'sync_catchup'
    storage_path character varying(500) NOT NULL,     -- path trong Supabase Storage
    file_size_bytes bigint,
    prompt_count integer,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_backup_snapshots PRIMARY KEY (snapshot_id),
    CONSTRAINT fk_backup_snapshots_spaces FOREIGN KEY (space_id) REFERENCES vault.spaces (space_id) ON DELETE CASCADE,
    CONSTRAINT fk_backup_snapshots_users FOREIGN KEY (triggered_by_user_id) REFERENCES security.users (user_id) ON DELETE RESTRICT
);
CREATE INDEX ix_backup_snapshots_space_id ON sync.backup_snapshots (space_id, created_at DESC);


-- =========================================================================
-- 5. TRIGGER: tang version (CHI khi noi dung doi) + ghi sync_log
-- =========================================================================

-- Tu tinh size moi khi content thay doi — quyet dinh externalize o tang service.
CREATE OR REPLACE FUNCTION vault.fn_prompts_calc_size()
RETURNS TRIGGER AS $$
BEGIN
    NEW.content_size_bytes := octet_length(NEW.content);
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_prompts_calc_size
    BEFORE INSERT OR UPDATE OF content ON vault.prompts
    FOR EACH ROW
    EXECUTE FUNCTION vault.fn_prompts_calc_size();

CREATE OR REPLACE FUNCTION vault.fn_prompts_before_update()
RETURNS TRIGGER AS $$
BEGIN
    NEW.version := OLD.version + 1;
    NEW.updated_date := now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- DA SUA (spec muc 3.3): chi chay khi cot NOI DUNG doi that su. Truoc day
-- chay tren MOI update (ke ca chi doi has_conflict/is_favorite/usage_count),
-- gay conflict gia cho cac thay doi khong lien quan.
CREATE TRIGGER trg_prompts_before_update
    BEFORE UPDATE ON vault.prompts
    FOR EACH ROW
    WHEN (
        OLD.title IS DISTINCT FROM NEW.title OR
        OLD.content IS DISTINCT FROM NEW.content OR
        OLD.description IS DISTINCT FROM NEW.description OR
        OLD.category_id IS DISTINCT FROM NEW.category_id
    )
    EXECUTE FUNCTION vault.fn_prompts_before_update();

CREATE OR REPLACE FUNCTION sync.fn_prompts_write_log()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO sync.sync_log (space_id, entity_type, entity_id, operation, payload, origin_device_id, version)
    VALUES (
        COALESCE(NEW.space_id, OLD.space_id),
        'prompt',
        COALESCE(NEW.prompt_id, OLD.prompt_id),
        LOWER(TG_OP),
        CASE WHEN TG_OP = 'DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END,
        COALESCE(NEW.updated_device_id, OLD.updated_device_id),
        COALESCE(NEW.version, OLD.version)
    );
    RETURN COALESCE(NEW, OLD);
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_prompts_write_sync_log
    AFTER INSERT OR UPDATE OR DELETE ON vault.prompts
    FOR EACH ROW
    EXECUTE FUNCTION sync.fn_prompts_write_log();


-- =========================================================================
-- 6. PHAN QUYEN — KHONG dung RLS (da bo, spec muc 3.2)
-- =========================================================================
-- Ban goc co RLS dua tren auth.uid() (Supabase Auth). Da bo hoan toan: AioKin
-- khong dung Supabase Auth, backend dung 1 connection pool chung nen
-- auth.uid() luon NULL — RLS kieu nay khong bao gio hoat dong dung trong kien
-- truc nay. Phan quyen thuc su nam o tang API .NET:
--   - [Authorize(Roles = ...)] — xac thuc vai tro he thong (Customer/Staff...)
--   - ISpaceContext.ResolveAsync — thanh vien + CanManage cho tung Space
--     (uy quyen cho IFamilyContext khi Space la 'family')
--   - CASL/IPermissionService — subject-level rule (Prompt/Category/Tag/Space)
-- Xem docs/superpowers/plans/2026-09-25-promptvault-space-and-prompt-domain.md.


-- =========================================================================
-- 7. DON DEP sync_log DINH KY — tranh phinh bang change-log vo han
-- =========================================================================
CREATE EXTENSION IF NOT EXISTS pg_cron;

CREATE OR REPLACE FUNCTION sync.fn_mark_stale_devices()
RETURNS void AS $$
BEGIN
    UPDATE sync.devices
    SET is_stale = true
    WHERE is_stale = false
      AND last_synced_at IS NOT NULL
      AND last_synced_at < now() - INTERVAL '90 days';
END;
$$ LANGUAGE plpgsql;

-- Xoa log cu hon 30 ngay VA da duoc moi device con "song" sync qua moc do;
-- ngoai ra xoa cung log cu hon 180 ngay bat ke device nao.
--
-- LUU Y (GET /sync/pull, sync-engine plan): mot cursor cu hon dong sync_log
-- cu nhat con lai la dau hieu retention da xoa mat 1 phan lich su — server
-- phai tra snapshot_type='sync_catchup' thay vi incremental trong truong hop
-- do, khong duoc am tham thieu du lieu.
CREATE OR REPLACE FUNCTION sync.fn_cleanup_old_sync_log()
RETURNS void AS $$
DECLARE
    min_last_synced timestamptz;
BEGIN
    PERFORM sync.fn_mark_stale_devices();

    SELECT MIN(last_synced_at) INTO min_last_synced
    FROM sync.devices
    WHERE is_stale = false AND last_synced_at IS NOT NULL;

    DELETE FROM sync.sync_log
    WHERE created_at < now() - INTERVAL '30 days'
      AND created_at < COALESCE(min_last_synced, now());

    DELETE FROM sync.sync_log
    WHERE created_at < now() - INTERVAL '180 days';
END;
$$ LANGUAGE plpgsql;

SELECT cron.schedule(
    'cleanup-sync-log-daily',
    '0 3 * * *',   -- 3h sang moi ngay (gio UTC)
    $$ SELECT sync.fn_cleanup_old_sync_log(); $$
);


-- =========================================================================
-- 8. CAC CAU TRUY VAN MAU
-- =========================================================================

-- 8.1. Lay toan bo prompt trong 1 category, sap moi nhat truoc
-- SELECT p.*, c.name AS category_name
-- FROM vault.prompts p
-- LEFT JOIN vault.categories c ON c.category_id = p.category_id
-- WHERE p.space_id = :space_id AND p.category_id = :category_id AND p.is_deleted = false
-- ORDER BY p.updated_date DESC;

-- 8.2. Full-Text Search prompt theo tu khoa
-- SELECT prompt_id, title, content
-- FROM vault.prompts
-- WHERE space_id = :space_id
--   AND is_deleted = false
--   AND to_tsvector('simple', title || ' ' || content) @@ to_tsquery('simple', :keyword);

-- 8.3. Nhom so luong prompt theo category (dashboard theo chu de)
-- SELECT c.name, COUNT(p.prompt_id) AS total_prompts
-- FROM vault.categories c
-- LEFT JOIN vault.prompts p ON p.category_id = c.category_id AND p.is_deleted = false
-- WHERE c.space_id = :space_id
-- GROUP BY c.name
-- ORDER BY total_prompts DESC;

-- 8.4. Lay prompt kem tags + variables (man hinh chi tiet)
-- SELECT p.*,
--        (SELECT jsonb_agg(t.name) FROM vault.prompt_tags pt JOIN vault.tags t ON t.tag_id = pt.tag_id WHERE pt.prompt_id = p.prompt_id) AS tags,
--        (SELECT jsonb_agg(jsonb_build_object('key', v.var_key, 'label', v.label, 'default', v.default_value))
--           FROM vault.prompt_variables v WHERE v.prompt_id = p.prompt_id) AS variables
-- FROM vault.prompts p
-- WHERE p.prompt_id = :prompt_id;

-- 8.5. Pull-sync incremental: thay doi moi hon cursor cho 1 space
-- SELECT * FROM sync.sync_log
-- WHERE space_id = :space_id AND sync_log_id > :since_cursor
-- ORDER BY sync_log_id ASC;

-- 8.6. Kiem tra retention truoc khi quyet dinh incremental hay snapshot
-- SELECT MIN(sync_log_id) AS oldest_log_id
-- FROM sync.sync_log
-- WHERE space_id = :space_id;
-- -- Neu :since_cursor < oldest_log_id - 1 => phai tra snapshot_type='sync_catchup'.

-- 8.7. Backup/snapshot toan bo prompt cua 1 space ra JSON
-- SELECT jsonb_agg(to_jsonb(p)) AS backup_data
-- FROM vault.prompts p
-- WHERE p.space_id = :space_id AND p.is_deleted = false;

-- 8.8. Doc / cap nhat theme sang-toi persist cho user (UPSERT)
-- INSERT INTO security.user_profiles (user_id, theme_mode, updated_device_id)
-- VALUES (:user_id, :theme_mode, :device_id)
-- ON CONFLICT (user_id)
-- DO UPDATE SET theme_mode = EXCLUDED.theme_mode,
--               updated_device_id = EXCLUDED.updated_device_id,
--               updated_at = now();


COMMIT;
