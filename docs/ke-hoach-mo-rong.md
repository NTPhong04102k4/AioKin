# Ke hoach mo rong AioKin

Tai lieu nay la ban thiet ke cho vong phat trien tiep theo: ban do (Goong), nhan tin
realtime, kho file noi bo cua gia dinh, quan ly chi tieu, todo gan vao calendar, va bo
tinh nang QR (tao nhanh, thanh toan, quet de dieu huong, cong kiem soat chuyen tien).

Doc theo thu tu. Cac moc phu thuoc nhau — M0 la nen mong cua tat ca phan con lai.

---

## 0. Hien trang (dieu da co, khong lam lai)

| Da co | O dau |
|---|---|
| Auth day du: dang ky OTP, login, refresh xoay vong, blacklist JTI, khoa tai khoan | `Services/Auth/**` |
| SSO Google / Facebook | `Services/Auth/OAuth/` |
| Phan quyen CASL doc tu `security.roles.permissions` | `Services/Auth/Permissions/` |
| Mot khuon loi duy nhat `OperationResult` -> HTTP status | `Common/OperationResultHttpExtensions.cs` |
| Ba tang Redis (StackExchange / Upstash REST / MemoryCache) | `Services/Common/Cache/` |
| Rate limiting hai policy `auth` va `auth-strict` | `Setup/RateLimitingSetup.cs` |
| Content toi thieu: `DiscoveryItem` (`/posts`), `ScheduleItem` (`/todos`) | `Services/Content/` |

**Thieu hoan toan:** khai niem **ho gia dinh**. Moi thu trong tai lieu nay — chat nhom, kho
file dung chung, so chi tieu, todo giao viec — deu can mot pham vi chia se ma hien tai
khong ton tai. Do la ly do M0 phai lam truoc.

---

## 1. Quyet dinh kien truc chot truoc

Chot o day de khong phai tranh luan lai o tung moc.

| Van de | Quyet dinh | Vi sao |
|---|---|---|
| Realtime | **SignalR** + Redis backplane | Redis da bat buoc o Production; backplane gan nhu mien phi. Raw WebSocket phai tu viet lai group, reconnect, fallback. |
| Luu file | Interface `IFileStorage`, ba ban cai (Local dev / S3-compatible / chan o Production neu roi ve Local) | Dung dung khuon `IRedisService` da co — nguoi doc code khong phai hoc mo hinh moi. |
| Phat video | **Khong proxy byte qua API.** S3 tra presigned GET TTL ngan, client goi thang; dev thi `FileStreamResult` voi `enableRangeProcessing: true` | Proxy byte bien API thanh CDN — het thread pool truoc khi het bang thong. |
| Tien | `long`, don vi **dong VND**, khong bao gio `double` | Chia trung binh, chia deu, cong don deu phai chinh xac tuyet doi. |
| Thoi gian | `timestamptz`, luu UTC, phat ra epoch millis | Dung quy uoc `ScheduleItemResponse` da dat. |
| Nen QR thanh toan | Sinh chuoi **EMVCo / VietQR** o server, **client tu ve QR** | Tra chuoi thay vi anh: nhe hon, API van stateless, va app doi mau/logo khung QR duoc. |
| Job nen | `Hangfire` + storage Postgres | Can retry va lich (nhac viec, sinh khoan chi dinh ky, tao thumbnail). Tu viet bang mot bang `jobs` + `BackgroundService` cung duoc nhung se phai viet lai retry/backoff. |
| Migration | Moi moc mot migration rieng, dat ten theo moc | `detect_changes()` va rollback deu de hon. |

### 1.1. Quy uoc bat buoc giu

- **Moi entity moi phai co hang so `SubjectType`** va duoc them vao bo rule seed trong
  `DbSeeder.SeedRolePermissionsAsync`. Thieu buoc nay thi `/ability/rules` phat ra bo rule
  khong nhac gi den entity moi, va app cam tat ca — mot loi im lang.
- **Thu tu rule CASL la ngu nghia.** Rule sau thang rule truoc. Khong tang nao duoc sap xep lai.
- **Danh tinh luon lay tu token.** Khong endpoint nao nhan `userId` tu client.
- **Duong thanh cong cua `/posts`, `/todos`, `/ability/rules` khong boc `OperationResult`.**
  Endpoint moi thi boc binh thuong; ba duong cu giu nguyen vi `ApiService` ben Android khai
  kieu tra ve la `List<...>`.
- Comment va tai lieu viet tieng Viet **khong dau**, dung theo phan con lai cua repo.

### 1.2. Ma loi moi can them vao `OperationResultHttpExtensions`

| ErrorCode | Status | Dung khi |
|---|---|---|
| `PayloadTooLarge` | 413 | File vuot han muc |
| `UnsupportedMediaType` | 415 | Kieu file khong nam trong whitelist |
| `QuotaExceeded` | 507 | Ho gia dinh het dung luong |
| `NotAFamilyMember` | 403 | Thao tac tren pham vi gia dinh khong thuoc ve minh |
| `TransferNotAuthorized` | 403 | Khong co `TransferIntent` hop le (xem M6) |
| `UpstreamUnavailable` | 503 | Goong / provider thanh toan khong tra loi |

---

## 2. M0 — Nen mong: ho gia dinh

Khong co moc nay thi khong moc nao sau chay duoc.

### Entity moi — schema `family`

```
Family              FamilyID(Guid PK) · FamilyUUID(unique) · Name · OwnerUserID · InviteCode(unique)
                    · StorageQuotaBytes · IsActive · CreatedDate
FamilyMember        FamilyMemberID · FamilyID · UserID · MemberRole(Owner|Adult|Child)
                    · DisplayName · JoinedDate · IsActive
                    -> UNIQUE(FamilyID, UserID); INDEX(UserID)
FamilyInvite        FamilyInviteID · FamilyID · Code(unique) · CreatedByUserID
                    · ExpiresAt · MaxUses · UsedCount · RevokedAt
```

`MemberRole` **khong** phai role he thong (`Roles.CUSTOMER` van la role trong token). No la
vai tro trong pham vi mot gia dinh, va la thu ma rule CASL doc qua `conditions`.

### Endpoint

| Method | Duong dan | Quyen |
|---|---|---|
| POST | `/families` | Customer — tao gia dinh, nguoi tao thanh Owner |
| GET | `/families/me` | Cac gia dinh minh thuoc |
| POST | `/families/{uuid}/invites` | Owner / Adult |
| POST | `/families/join` | Customer — nhap `code` |
| GET | `/families/{uuid}/members` | Thanh vien |
| PATCH | `/families/{uuid}/members/{memberId}` | Owner — doi vai tro |
| DELETE | `/families/{uuid}/members/{memberId}` | Owner — hoac tu roi nhom |

### `IFamilyContext` — mieng ghep quan trong nhat

Mot service scoped tra loi duy nhat mot cau: *nguoi goi request nay co phai thanh vien
cua `familyUuid` khong, va voi vai tro gi*. Moi service o M2..M6 goi no truoc khi cham du
lieu. Dat kiem tra o mot cho thay vi lap lai o tung controller — bo sot mot cho la ro ri
du lieu ca gia dinh.

```
Task<FamilyMembership?> ResolveAsync(Guid familyUuid, CancellationToken ct);
// null = khong phai thanh vien -> tra OperationResult.Fail("NotAFamilyMember", ...)
```

Cache membership trong Redis theo `family:{uuid}:member:{userUuid}` TTL 5 phut, **xoa key
ngay** khi doi vai tro hoac go thanh vien — neu khong, nguoi vua bi go van doc duoc 5 phut.

### Migration

`AddFamily` — them ba bang, chua dung den bang nao dang co.

---

## 3. M1 — Ban do: diem khoi dau va diem den (Goong)

### 3.1. Nguyen tac: API key khong bao gio roi khoi server

Nhung API key Goong vao APK thi bat ky ai giai nen APK cung doc duoc, va hoa don la cua
ban. **Backend lam proxy** cho moi loi goi Goong. App chi goi AioKin.

Ngoai le duy nhat: **maptiles key** de ve ban do nen trong SDK — key do buoc phai o client.
Dat rieng, gioi han theo package name / SHA-1 ben bang dieu khien Goong, va khong dung
chung voi REST key.

### 3.2. Cau hinh

```
Goong:ApiKey        (bat buoc o Production)
Goong:BaseUrl       mac dinh https://rsapi.goong.io
Goong:CacheMinutes  mac dinh 1440 cho geocode, 60 cho autocomplete
```

Bind qua `GoongOptions` + `ValidateDataAnnotations`, dung khuon `BrevoOptions` da co.
Thieu key **o Development** thi dang ky mot ban cai tra ket qua rong kem canh bao log —
giong duong roi `LoggingEmailService`. **O Production thi dung khoi dong**, giong Redis.

### 3.3. `IGoongClient` — typed HttpClient

| Phuong thuc | Goong endpoint | Dung cho |
|---|---|---|
| `AutocompleteAsync(input, location?, radius?)` | `/Place/AutoComplete` | O tim kiem dia chi |
| `PlaceDetailAsync(placeId)` | `/Place/Detail` | Lay toa do + dia chi day du |
| `GeocodeAsync(address)` | `/geocode` | Doi chuoi -> toa do |
| `ReverseGeocodeAsync(lat, lng)` | `/Geocode?latlng=` | "Vi tri hien tai" -> ten dia chi |
| `DirectionsAsync(origin, destination, vehicle)` | `/Direction` | Duong di, quang duong, thoi gian |
| `DistanceMatrixAsync(origins, destinations)` | `/DistanceMatrix` | Sap xep nhieu diem den theo do gan |

> **Can kiem chung truoc khi code:** ten duong dan, ten tham so va hinh dang JSON tra ve
> phai doi chieu voi tai lieu Goong hien hanh (`docs.goong.io`) — dung tin bang nay la du.
> Viet mot integration test goi that mot lan de chot hop dong, roi mock cho cac test sau.

### 3.4. Cache va chi phi

Goong tinh tien theo luot goi. Ba lop giam:

1. **Debounce o client** — khong goi autocomplete truoc ky tu thu 3, cach nhau it nhat 300ms.
2. **Redis cache o server** — key `goong:ac:{sha256(input|location)}` TTL 60 phut,
   `goong:geo:{sha256(address)}` TTL 24 gio. Toa do mot dia chi khong doi trong ngay.
3. **Rate limit policy `maps`** — 30 luot/phut/nguoi. Them vao `RateLimitingSetup`.

### 3.5. Entity moi — schema `core`

```
SavedPlace   SavedPlaceID · UserID · FamilyID(nullable) · Label(Home|Work|Custom) · Name
             · Address · Latitude(double) · Longitude(double) · GoongPlaceId(nullable)
             · CreatedDate
             -> INDEX(UserID, Label)
```

`FamilyID` khac null = dia diem dung chung ca nha ("nha ong ba", "truong cua be").

### 3.6. Endpoint

| Method | Duong dan | Quyen | Ghi chu |
|---|---|---|---|
| GET | `/places/autocomplete?q=&lat=&lng=` | Da dang nhap | Proxy + cache |
| GET | `/places/{goongPlaceId}` | Da dang nhap | Chi tiet + toa do |
| GET | `/places/reverse?lat=&lng=` | Da dang nhap | Toa do -> dia chi |
| POST | `/routes/preview` | Da dang nhap | Body: origin, destination, vehicle -> quang duong, thoi gian, polyline |
| GET/POST/DELETE | `/places/saved` | Da dang nhap | Dia diem da luu |

`polyline` tra nguyen chuoi encoded cua Goong — de client giai ma, dung tra mang toa do
(nang gap nhieu lan).

### 3.7. Noi vao ScheduleItem

Them vao `ScheduleItem`: `OriginPlaceID`, `DestinationPlaceID` (deu nullable, FK
`SavedPlace`, `OnDelete: SetNull`). Do la phan "diem khoi dau va diem den" cua yeu cau —
mot muc lich trinh biet minh xuat phat tu dau va den dau, va man hinh chi tiet mo duoc
chi duong.

**Khong sua `ScheduleItemResponse`.** DTO ben Android dang doc no theo ten field; them
field vao la Gson bo qua (khong hong), nhung doi field la hong. Field moi phat qua
`ScheduleItemV2Response` o endpoint `/schedule` moi. Xem M5.

---

## 4. M2 — Nhan tin realtime va nhom chat

### 4.1. Entity — schema `chat`

```
Conversation        ConversationID(Guid) · ConversationUUID(unique) · FamilyID(nullable)
                    · Kind(Direct|Group) · Title(nullable) · AvatarMediaID(nullable)
                    · CreatedByUserID · LastMessageAt · CreatedDate
ConversationMember  ConversationMemberID · ConversationID · UserID · MemberRole(Owner|Member)
                    · JoinedAt · LastReadMessageID · MutedUntil · LeftAt(nullable)
                    -> UNIQUE(ConversationID, UserID); INDEX(UserID, LeftAt)
Message             MessageID(bigint identity) · ConversationID · SenderUserID
                    · Kind(Text|Media|System) · Body(nullable) · ReplyToMessageID(nullable)
                    · ClientMessageID(Guid) · EditedAt · DeletedAt · CreatedDate
                    -> INDEX(ConversationID, MessageID DESC)
                    -> UNIQUE(ConversationID, SenderUserID, ClientMessageID)
MessageAttachment   MessageAttachmentID · MessageID · MediaAssetID
MessageReceipt      ConversationID · UserID · LastDeliveredMessageID · LastReadMessageID
                    -> PK(ConversationID, UserID)
```

`MessageID` la `bigint identity` chu khong phai Guid: phan trang chat la "lay 50 tin truoc
tin X", ma cursor tren khoa tang dan la mot lan seek index, con tren Guid thi phai sort
theo `CreatedDate` va xu ly trung thoi diem.

Da doc thi ghi o `MessageReceipt` (mot dong moi nguoi moi phong) chu khong phai mot dong
moi tin nhan — nhom 8 nguoi, 10k tin thi cach kia la 80k dong chi de biet ai doc den dau.

### 4.2. `ChatHub`

```
// Server -> Client
ReceiveMessage(MessageDto)      MessageEdited(...)      MessageDeleted(...)
TypingStarted(convUuid, userUuid)                       ReadReceiptUpdated(...)
MemberJoined / MemberLeft

// Client -> Server
SendMessage(convUuid, body, attachmentIds[], clientMessageId)
MarkRead(convUuid, messageId)
Typing(convUuid)
```

`clientMessageId` (Guid do client sinh) cho phep **idempotent**: mang chap chon, client gui
lai, server thay id trung thi tra ve tin da luu thay vi tao ban sao. Do la ly do co rang
buoc UNIQUE `(ConversationID, SenderUserID, ClientMessageID)` o tren.

Group cua SignalR dat ten `conv:{ConversationUUID}`. Vao group luc connect (moi phong dang
mo) hoac lazily khi client goi `Subscribe`.

### 4.3. Xac thuc SignalR — hai diem phai xu ly

**(a) Token qua query string.** WebSocket khong dat duoc header `Authorization`. Phai them
vao cau hinh JwtBearer:

```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = ctx =>
    {
        var token = ctx.Request.Query["access_token"];
        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
            ctx.Token = token;
        return Task.CompletedTask;
    }
};
```

Gioi han theo `/hubs` — khong thi moi endpoint REST cung nhan token qua URL, va URL thi
nam trong log truy cap, lich su trinh duyet, header `Referer`.

**(b) Lo hong thu hoi token — phai xu ly, khong duoc bo qua.**
`JwtBlacklistMiddleware` chay trong pipeline HTTP. SignalR chi xac thuc **mot lan luc bat
tay**; sau do ket noi song hang gio. Nguoi dung bam "dang xuat tat ca thiet bi" hoac bi
khoa tai khoan thi ket noi hub dang mo **van tiep tuc nhan tin nhan**.

Cach xu ly, lam ca hai:

1. `ChatHub` ghi ban do `userUuid -> connectionIds` vao Redis luc `OnConnectedAsync`.
   Khi thu hoi token / khoa tai khoan, publish mot message Redis; mot `IHostedService`
   subscribe va goi `IHubContext.Clients.Client(id).Abort()`.
2. Trong `ChatHub`, moi ~2 phut kiem tra lai `jti` con hop le khong; het han thi `Abort()`.
   Lop nay bat truong hop message Redis bi mat.

### 4.4. REST di kem (lich su, khong realtime)

| Method | Duong dan | Ghi chu |
|---|---|---|
| GET | `/conversations` | Danh sach phong + tin cuoi + so chua doc |
| POST | `/conversations` | Tao Direct (2 nguoi) hoac Group |
| GET | `/conversations/{uuid}/messages?before=&limit=` | Cursor tren `MessageID`, mac dinh 50, toi da 100 |
| POST | `/conversations/{uuid}/messages` | Gui tin qua REST (duong du phong) |
| POST | `/conversations/{uuid}/members` | Them thanh vien (Group) |
| DELETE | `/conversations/{uuid}/members/{userUuid}` | Go / roi nhom |
| PATCH | `/conversations/{uuid}` | Doi ten, doi anh |

Gui tin **cung mo qua REST** chu khong chi qua hub: mang yeu thi hub rot, va gui duoc tin
quan trong hon gui nhanh. Ca hai duong deu di qua cung mot `IChatService.SendAsync` — mot
cho kiem tra quyen, mot cho ghi, roi broadcast.

### 4.5. Thong bao khi nguoi nhan offline

Can FCM. **Ngoai pham vi moc nay** — thiet ke chua cho no bang cach cho `IChatService`
phat mot su kien `MessageDelivered` qua Hangfire; ban dau handler chi log.

---

## 5. M3 — Kho file va media noi bo

Muc tieu: ca nha luu tai lieu (docx, pdf, txt), anh/gif, nhac (mp3), video (mp4) o mot cho,
xem/nghe truc tiep trong app.

### 5.1. `IFileStorage` — ba ban cai

| Ban cai | Dung khi | Ghi chu |
|---|---|---|
| `LocalDiskFileStorage` | Development | Ghi vao thu muc ngoai wwwroot |
| `S3FileStorage` | Production (Cloudflare R2 / MinIO / S3) | Presigned PUT/GET |
| — | Production ma khong cau hinh S3 | **Dung khoi dong**, giong Redis |

Bucket **khong bao gio public**. Moi truy cap qua presigned URL TTL ngan (5 phut cho GET,
15 phut cho PUT).

```csharp
Task<UploadTicket> CreateUploadTicketAsync(string key, string contentType, long size, CancellationToken ct);
Task<Uri> CreateReadUrlAsync(string key, TimeSpan ttl, CancellationToken ct);
Task<Stream> OpenReadAsync(string key, CancellationToken ct);   // chi dung o duong dev
Task DeleteAsync(string key, CancellationToken ct);
```

### 5.2. Entity — schema `media`

```
MediaAsset   MediaAssetID(Guid) · MediaAssetUUID(unique) · FamilyID · OwnerUserID
             · FolderID(nullable) · FileName · ContentType · SizeBytes(long)
             · Sha256(char 64) · StorageKey · ThumbnailKey(nullable)
             · Kind(Image|Video|Audio|Document|Other)
             · Status(Pending|Ready|Rejected) · RejectReason(nullable)
             · DurationSeconds(nullable) · Width/Height(nullable)
             · CreatedDate · DeletedAt(nullable)
             -> INDEX(FamilyID, DeletedAt, CreatedDate DESC); INDEX(FamilyID, Sha256)
MediaFolder  MediaFolderID · FamilyID · ParentFolderID(nullable) · Name · CreatedByUserID
```

`Sha256` de **khu trung**: ca nha cung luu mot file PDF thi chi ton mot ban tren storage,
hai `MediaAsset` cung tro toi mot `StorageKey`. Xoa thi chi go object khi khong con
`MediaAsset` nao (chua `DeletedAt`) tro toi no.

### 5.3. Luong upload

```
1. POST /media/upload-ticket   { fileName, contentType, sizeBytes, sha256 }
   -> server kiem tra whitelist, han muc, quota gia dinh
   -> neu sha256 da co trong gia dinh: tra ve luon MediaAsset cu (khong upload lai)
   -> nguoc lai: tao MediaAsset Status=Pending + tra presigned PUT
2. Client PUT thang len storage
3. POST /media/{uuid}/complete
   -> server doc lai header object (size, content-type) de doi chieu voi thu client khai
   -> doc 16 byte dau -> kiem tra magic bytes
   -> Status=Ready, xep job sinh thumbnail
```

**Buoc 3 khong duoc bo.** Client khai `contentType: application/pdf` roi PUT mot file
khac hoan toan — chi co server doc lai object moi biet. Sai lech thi `Status=Rejected` va
xoa object.

### 5.4. Kiem tra kieu file — magic bytes, khong phai duoi file

Whitelist va chu ky dau file:

| Kind | Content type | Magic bytes |
|---|---|---|
| Document | `application/pdf` | `25 50 44 46` (`%PDF`) |
| Document | `...wordprocessingml.document` (docx) | `50 4B 03 04` (zip) + phai co entry `word/` |
| Document | `text/plain` | — kiem tra la UTF-8/ASCII hop le, khong chua byte NUL |
| Image | `image/gif` | `47 49 46 38` (`GIF8`) |
| Image | `image/png` `image/jpeg` `image/webp` | `89 50 4E 47` / `FF D8 FF` / `RIFF....WEBP` |
| Video | `video/mp4` | `....66 74 79 70` (`ftyp` o offset 4) |
| Audio | `audio/mpeg` | `49 44 33` (`ID3`) hoac `FF FB`/`FF F3`/`FF F2` |

Ngoai whitelist -> `UnsupportedMediaType` (415).

**SVG khong nam trong whitelist.** SVG la XML co the chua `<script>`; phuc vu no tu mot
origin ma nguoi dung dang co phien dang nhap la XSS. Ai can icon thi dung PNG/WebP.

Moi phan hoi tra file deu dat `X-Content-Type-Options: nosniff`, va
`Content-Disposition: attachment` cho moi thu **tru** anh, video, audio (nhung kieu can
hien inline).

### 5.5. Han muc

| Kind | Toi da / file | Ghi chu |
|---|---|---|
| Image / GIF | 20 MB | |
| Document | 50 MB | |
| Audio | 100 MB | |
| Video | 500 MB | Presigned PUT bat buoc — khong di qua API |
| Quota gia dinh | 10 GB mac dinh | `Family.StorageQuotaBytes`, sua duoc |

Tinh quota bang `SUM(SizeBytes) WHERE FamilyID = ? AND DeletedAt IS NULL`, cache Redis 60
giay. Cong truoc khi cap ticket, khong phai sau khi upload xong.

### 5.6. Phat lai (streaming)

| Method | Duong dan | Hanh vi |
|---|---|---|
| GET | `/media/{uuid}/url` | Tra presigned GET TTL 5 phut. **Duong chinh** cho video/audio: ExoPlayer nhan URL nay va tu lo Range request. |
| GET | `/media/{uuid}/thumb` | Presigned GET cho thumbnail |
| GET | `/media/{uuid}/content` | Chi bat o Development (LocalDisk): `FileStreamResult` voi `enableRangeProcessing: true` |
| GET | `/media?folderId=&kind=&cursor=` | Duyet kho |

Mot presigned URL da phat ra thi khong thu hoi duoc trong TTL — do la ly do TTL la 5 phut
chu khong phai 24 gio.

### 5.7. Thumbnail va metadata

Job Hangfire sau khi `Status=Ready`:

- Anh/GIF: thu nho canh dai ve 512px. Dung `SkiaSharp` (cross-platform, khong can native
  ngoai). GIF thi lay frame dau.
- Video: **can `ffmpeg`** tren may chu — lay frame o giay thu 1 + doc `DurationSeconds`.
- Audio: doc tag ID3 (`TagLibSharp`) lay ten bai, thoi luong.

`ffmpeg` la mot phu thuoc ha tang that (them vao Dockerfile). **Thumbnail thieu khong duoc
lam hong asset**: khong co `ThumbnailKey` thi app hien icon theo `Kind`.

### 5.8. Quet virus

Kho file dung chung cua gia dinh, ai cung upload duoc. Toi thieu: chay ClamAV
(`clamav-daemon` sidecar) trong cung job, `Status=Rejected` neu duong tinh. Neu chua lam
ngay thi ghi thang vao backlog — dung de no bien mat.

---

## 6. M4 — Chi tieu va thong ke

### 6.1. Entity — schema `finance`

```
ExpenseCategory   ExpenseCategoryID · FamilyID(nullable = danh muc he thong) · Code · Name
                  · Icon · ColorHex · SortOrder · IsActive
Expense           ExpenseID(Guid) · ExpenseUUID(unique) · FamilyID · PaidByUserID
                  · CategoryID · AmountVnd(long) · Note · OccurredAt(date)
                  · ReceiptMediaID(nullable) · CreatedByUserID · CreatedDate · DeletedAt
                  -> INDEX(FamilyID, OccurredAt DESC); INDEX(FamilyID, CategoryID, OccurredAt)
ExpenseShare      ExpenseShareID · ExpenseID · UserID · AmountVnd(long)
                  -- chia mot khoan cho nhieu nguoi; tong phai bang Expense.AmountVnd
Budget            BudgetID · FamilyID · CategoryID(nullable = tong) · PeriodMonth(date)
                  · LimitVnd(long)
                  -> UNIQUE(FamilyID, CategoryID, PeriodMonth)
RecurringExpense  RecurringExpenseID · FamilyID · CategoryID · AmountVnd · Note
                  · Rrule · NextRunAt · PayeeAccountID(nullable) · IsActive
```

`AmountVnd` la `long`, don vi dong. Khong `decimal`, khong `double`. Rang buoc CHECK
`amount_vnd > 0`.

`ExpenseShare` la thu bien "so chi tieu" thanh "ai no ai": tong `ExpenseShare` phai bang
`Expense.AmountVnd`. Kiem tra trong service **va** bang mot CHECK/trigger neu lam duoc —
lech o day la sai so tich luy khong ai phat hien ra.

### 6.2. Endpoint

| Method | Duong dan | Ghi chu |
|---|---|---|
| POST/GET/PATCH/DELETE | `/families/{uuid}/expenses` | Xoa la soft delete |
| GET | `/families/{uuid}/expenses?from=&to=&categoryId=&paidBy=&cursor=` | |
| GET | `/families/{uuid}/stats/summary?from=&to=` | Tong, trung binh/ngay, so giao dich |
| GET | `/families/{uuid}/stats/by-category?from=&to=` | Cho bieu do tron |
| GET | `/families/{uuid}/stats/by-member?from=&to=` | Ai chi bao nhieu |
| GET | `/families/{uuid}/stats/trend?months=12` | Cho bieu do duong |
| GET/PUT | `/families/{uuid}/budgets?month=` | Han muc + phan tram da dung |
| CRUD | `/families/{uuid}/recurring-expenses` | "Cac khoan thuong dung" |
| GET | `/expense-categories` | Danh muc he thong + cua gia dinh |

### 6.3. Thong ke lam o SQL, khong lam trong bo nho

```csharp
// Dung: mot lan tra database, Postgres gom
var byCategory = await _db.Expenses
    .Where(e => e.FamilyID == familyId && e.DeletedAt == null
             && e.OccurredAt >= from && e.OccurredAt < to)
    .GroupBy(e => e.CategoryID)
    .Select(g => new { CategoryId = g.Key, Total = g.Sum(e => e.AmountVnd), Count = g.Count() })
    .ToListAsync(ct);
```

`ToListAsync()` roi `GroupBy` trong LINQ-to-Objects se keo ca nam giao dich ve app. Voi
mot gia dinh thi khong sao; voi mot nghin gia dinh thi do la su co.

Cache ket qua thong ke cua **thang da dong** (khong con thay doi) trong Redis TTL 24 gio;
thang hien tai thi khong cache.

### 6.4. Danh muc he thong seed san

`AN_UONG`, `DI_LAI`, `NHA_CUA`, `HOC_HANH`, `Y_TE`, `GIAI_TRI`, `MUA_SAM`, `HOA_DON`,
`TIET_KIEM`, `KHAC`. Them vao `DbSeeder`.

---

## 7. M5 — Todo va calendar

### 7.1. Mo rong `ScheduleItem` — them cot, khong doi cot cu

```
+ FamilyID(nullable)          -- null = viec rieng, khac null = viec chung ca nha
+ AssigneeUserID(nullable)    -- giao cho ai
+ EndAt(nullable)
+ IsAllDay(bool)
+ Priority(0..3)
+ Rrule(nullable)             -- tap con RFC 5545: FREQ, INTERVAL, BYDAY, COUNT, UNTIL
+ RemindMinutesBefore(nullable)
+ OriginPlaceID(nullable)     -- tu M1
+ DestinationPlaceID(nullable)
+ Notes(nullable)
+ CompletedAt(nullable)

ScheduleException  ScheduleExceptionID · ScheduleItemID · OriginalStartAt
                   · Action(Skipped|Moved|Edited) · NewStartAt · NewTitle
```

### 7.2. Lich lap: khai trien luc doc, khong luu san

Mot viec lap hang tuan khong sinh 520 dong cho 10 nam. Luu **mot dong + `Rrule`**, khai
trien trong khoang `[from, to]` khi client hoi, ap `ScheduleException` len ket qua.

Chi ho tro tap con RRULE: `FREQ=DAILY|WEEKLY|MONTHLY|YEARLY`, `INTERVAL`, `BYDAY`,
`COUNT`, `UNTIL`. Vao chuoi RRULE ngoai tap nay -> `ValidationError`, khong "co gang hieu".

Chan cua so: `to - from` toi da 366 ngay. Khong chan thi mot request `from=1970&to=2100`
tren mot viec lap hang ngay se sinh 47 nghin muc.

### 7.3. Endpoint

| Method | Duong dan | Ghi chu |
|---|---|---|
| GET | `/todos` | **Giu nguyen y het.** Hop dong voi app Android hien tai. |
| GET | `/schedule?from=&to=&familyId=&assignee=` | v2 — da khai trien lap, du field moi |
| POST/PATCH/DELETE | `/schedule/{id}` | |
| POST | `/schedule/{id}/complete` | |
| POST | `/schedule/{id}/occurrences/{startAt}/skip` | Bo mot lan lap |
| GET | `/calendar?from=&to=` | Gop lich trinh + han khoan chi dinh ky + nhac tra hoa don |
| GET | `/calendar/feed.ics?token=` | Xuat iCalendar cho Google/Apple Calendar |

`feed.ics` dung mot **feed token rieng** (chuoi ngau nhien luu tren `User`, thu hoi duoc),
khong dung JWT: URL nay se bi dan vao Google Calendar va nam do vinh vien.

### 7.4. Nhac viec

Hangfire recurring job chay moi 5 phut: tim `ScheduleItem` co
`StartAt - RemindMinutesBefore` roi vao cua so vua qua va chua gui, phat su kien nhac.
Kenh gui ban dau la SignalR (`ReminderDue`) + luu mot `Notification`; FCM sau.

---

## 8. M6 — QR: tao nhanh, thanh toan, quet dieu huong, cong chuyen tien

Moc phuc tap nhat, va la cho co mot han che ngoai tam kiem soat. Doc ky muc 8.5.

### 8.1. Doc lai yeu cau

> "cho phep create QR nhanh, cac khoan thanh toan thuong dung, tu dong navigate voi qr nhan
> vao, gan tinh nang ai can chuyen qr thi moi chuyen dc con k quet k ra va an button chuyen
> tien duoi qr frame"

Chia thanh bon phan:

| # | Yeu cau | Trang thai |
|---|---|---|
| A | Tao QR nhanh (text, URL, wifi, danh thiep, ma moi gia dinh) | Lam duoc ngay |
| B | QR thanh toan VietQR + cac khoan thuong dung | Sinh QR: lam duoc ngay. Doi soat: **bi chan**, xem 8.5 |
| C | Quet QR -> tu dong dieu huong dung man hinh | Lam duoc ngay |
| D | Chi nguoi **can** chuyen moi chuyen duoc; nguoi khac quet khong ra ket qua; an nut chuyen tien duoi khung quet | Lam duoc ngay — thiet ke o 8.4 |

**Ve chu "ai" trong yeu cau D:** doc la **"ai" = "nguoi nao"** ("ai can chuyen thi moi
chuyen duoc"), tuc la mot **cong phan quyen**, khong phai machine learning. Toan bo 8.4
thiet ke theo cach hieu do. Neu that su muon them mot lop AI cham diem rui ro thi do la
8.6 — mot lop **cong them**, dat sau cong phan quyen chu khong thay the no.

### 8.2. Sinh QR (phan A)

Server chi tra **chuoi payload**; client ve QR. Nhe hon, va app doi kich thuoc/mau/logo
duoc ma khong goi lai server.

| Method | Duong dan | Body -> tra ve |
|---|---|---|
| POST | `/qr/build` | `{ kind, payload }` -> `{ raw, kind }` |

`kind` ho tro: `Text`, `Url`, `Wifi` (`WIFI:T:WPA;S:..;P:..;;`), `Contact` (vCard),
`FamilyInvite` (deeplink `aiokin://join?code=...`), `Payment` (xem 8.3).

Neu van muon anh PNG: them `GET /qr/build.png?...` dung `QRCoder`. De sau, khong phai
duong chinh.

### 8.3. QR thanh toan VietQR (phan B — phan sinh ra)

Chuan **EMVCo QR Code** ma NAPAS 247 dung: chuoi TLV (`ID` 2 ky tu + `Length` 2 ky tu +
`Value`), ket thuc bang truong `63` la **CRC-16/CCITT-FALSE** cua toan bo chuoi tinh den va
bao gom ca `6304`.

Sinh duoc **hoan toan offline** tu: BIN ngan hang + so tai khoan + so tien + noi dung.
**Khong can hop dong, khong can API key, khong can xin phep ai** de sinh va hien thi.

```
PayeeAccount   PayeeAccountID · FamilyID · Label · BankBin(6) · AccountNumber
               · AccountName · IsActive · CreatedByUserID
               -> day la "cac khoan thanh toan thuong dung": tien dien, tien nuoc,
                  hoc phi, chuyen cho ong ba...
```

| Method | Duong dan | Ghi chu |
|---|---|---|
| CRUD | `/families/{uuid}/payees` | Nguoi nhan thuong dung |
| POST | `/qr/payment` | `{ payeeId, amountVnd?, note? }` -> `{ raw, payeeLabel, amountVnd }` |

Viet **`EmvcoQrBuilder`** trong `Common/Qr/` cung voi **`Crc16Ccitt`**. Test bang cac
payload mau da biet dap an — CRC sai mot bit thi moi app ngan hang deu bao "ma khong hop
le", va debug tu con so do rat mat thoi gian.

### 8.4. Quet, dieu huong, va cong chuyen tien (phan C + D)

**Nguyen tac chot: quyet dinh nam o server, khong nam o UI.**

An mot cai nut la trang tri. Neu API van tra ve du lieu thanh toan da phan tich cho moi
nguoi quet, thi bat ky ai sua APK hoac goi thang API deu vuot qua duoc. **Cai bao ve that
la server tu choi tra du lieu do.** Nut bi an chi la he qua nhin thay duoc.

#### `TransferIntent` — "ai can chuyen"

```
TransferIntent  TransferIntentID(Guid) · TransferIntentUUID(unique) · FamilyID
                · GrantedToUserID          -- nguoi duoc phep chuyen
                · GrantedByUserID          -- nguoi cap quyen (Owner/Adult)
                · PayeeAccountID(nullable) -- rang buoc dung mot nguoi nhan cu the
                · AllowedBankBin(nullable) · AllowedAccountNumber(nullable)
                · MaxAmountVnd(long)
                · Purpose                  -- "tra tien dien thang 9"
                · ExpiresAt · UsedAt(nullable) · RevokedAt(nullable)
                · LinkedExpenseID(nullable) · LinkedRecurringExpenseID(nullable)
                -> INDEX(GrantedToUserID, ExpiresAt, UsedAt)
```

Mot intent = **mot lan chuyen, mot nguoi nhan, mot tran so tien, mot cua so thoi gian**.
Dung xong (`UsedAt`) la het — khong tai su dung.

Nguon sinh intent, ca hai deu co:
- Nguoi trong nha co quyen cap: `POST /families/{uuid}/transfer-intents` (Owner/Adult).
  Vi du bo me giao cho con di dong tien dien.
- Tu dong tu `RecurringExpense` den han: Hangfire sinh intent cho nguoi duoc gan.

#### `POST /qr/resolve` — mot cong duy nhat

```
Request:  { raw: "<chuoi doc tu QR>" }
```

Server:

1. Phan loai `raw`: EMVCo payment / URL / deeplink AioKin / text thuong.
2. Neu **khong phai** payment -> tra intent dieu huong tuong ung (`OpenUrl`,
   `JoinFamily`, `ShowText`). Day la phan C, khong cong nao chan.
3. Neu **la** payment -> phan tich lay BIN, so tai khoan, so tien, noi dung. Roi hoi:
   *nguoi goi co `TransferIntent` con hieu luc khop voi nguoi nhan nay va so tien nay khong?*

```
Co  -> 200 { kind: "Payment", allowed: true,
             payee: {...}, amountVnd, note,
             intentUuid, deeplinks: [...] }

Khong -> 200 { kind: "Unknown", allowed: false }
         // KHONG kem payee, KHONG kem so tien, KHONG kem gi ca.
```

Do la "khong quet khong ra" duoc thuc thi o dung cho: server khong noi. App nhan
`kind: "Unknown"` thi hien "Khong nhan dang duoc ma nay" va **khong render nut chuyen tien**
duoi khung quet.

Khop la khop **tat ca** dieu kien:

```
intent.RevokedAt == null
&& intent.UsedAt == null
&& intent.ExpiresAt > now
&& intent.GrantedToUserID == caller
&& (intent.PayeeAccountID == null || khop BIN + so tai khoan cua payee do)
&& (intent.AllowedAccountNumber == null || khop so tai khoan trong QR)
&& (amountVnd trong QR) <= intent.MaxAmountVnd
```

QR khong ghi san so tien (VietQR dong mo) -> lay `MaxAmountVnd` lam tran, bat client nhap
so tien, va buoc xac nhan sau kiem tra lai tran.

#### `POST /qr/transfer-intents/{uuid}/confirm`

Client mo app ngan hang bang deeplink; app ngan hang moi la noi tien thuc su di. AioKin
khong cam tien. Endpoint nay:

- danh dau `UsedAt`
- ghi `Expense` tuong ung (neu `LinkedExpenseID` chua co thi tao) — de khoan chi vao so
- ghi `AuditLog`

**Ghi audit day du va khong the tat**: cap intent, thu hoi intent, moi lan `resolve` tra
`allowed: true`, moi lan confirm. Mot co che cho phep nguoi nay chuyen tien danh cho nguoi
khac ma khong de lai vet la mot co che khong nen ton tai. Chu tai khoan (Owner) phai xem
duoc toan bo lich su nay o `GET /families/{uuid}/transfer-intents`.

#### Chong do

`/qr/resolve` la mot oracle: quet nhieu QR de biet QR nao "duoc phep". Rate limit **10
luot/phut/nguoi** (policy `qr` moi), va log moi lan `allowed: false` de phat hien do.

### 8.5. Diem bi chan — doi soat thanh toan

**Sinh va hien thi QR VietQR: lam duoc ngay, khong can xin phep.**

**Biet duoc tien da chuyen thanh cong hay chua: khong lam duoc neu khong co ben thu ba.**
Ngan hang khong phat webhook bien dong so du cho ung dung ca nhan. Duong hop phap:

| Duong | Can gi |
|---|---|
| Cong thanh toan (PayOS, SePay, Casso...) | Tai khoan doanh nghiep + ky hop dong. Ho phat webhook khi co tien vao. |
| API ngan hang truc tiep (VCB, MB, ACB...) | Hop dong doanh nghiep, thuong kem yeu cau ve quy mo. |
| Nguoi dung tu xac nhan | Khong can gi. Do chinh xac phu thuoc nguoi dung. |

**Khuyen nghi:** ship duong 3 truoc (sau khi chuyen xong, nguoi dung bam "da chuyen" ->
ghi `Expense`, dinh anh chup man hinh bien lai vao `ReceiptMediaID`). Duong 1 la mot moc
rieng, mo khi co phap nhan. Dung de M6 phu thuoc vao no.

### 8.6. (Tuy chon) Lop AI cham diem rui ro

Neu muon them lop AI that — **dat sau cong `TransferIntent`, khong thay the no**:

- Dau vao: so tien so voi lich su, nguoi nhan da tung chuyen chua, thoi diem trong ngay,
  QR den tu anh chup man hinh hay tu camera, tan suat quet gan day.
- Dau ra: `Low | Medium | High`. `High` -> bat xac nhan them tu Owner truoc khi
  `resolve` tra `allowed: true`.
- Ban dau lam bang **rule co dinh** (mot bang if/else doc va sua duoc). Dung goi mo hinh
  cho viec nay truoc khi co du lieu that: mot mo hinh khong giai thich duoc ma chan giao
  dich hop le se bi tat trong tuan dau.

---

## 9. M7 — Van hanh va bao mat

### 9.1. Package moi

| Package | Cho |
|---|---|
| `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | M2 backplane |
| `AWSSDK.S3` (hoac `Minio`) | M3 storage |
| `SkiaSharp` + `SkiaSharp.NativeAssets.Linux` | M3 thumbnail anh |
| `TagLibSharp` | M3 metadata audio |
| `Hangfire.AspNetCore` + `Hangfire.PostgreSql` | Job nen |
| `QRCoder` | (tuy chon) M6 render PNG |

Khong them thu vien QR/EMVCo — `EmvcoQrBuilder` tu viet, khoang 150 dong, va test duoc.

### 9.2. Cau hinh moi

```
Goong:ApiKey                   Prod bat buoc
Storage:Provider               Local | S3
Storage:S3:Endpoint / Bucket / AccessKey / SecretKey / Region
Storage:LocalPath              chi Development
Media:MaxVideoBytes            ...
Family:DefaultQuotaBytes
Hangfire:Enabled
```

Bind qua `IOptions` + `ValidateDataAnnotations`, dung khuon `BrevoOptions`. Cap nhat bang
cau hinh trong README.

### 9.3. Rate limit policy moi

| Policy | Han muc | Cho |
|---|---|---|
| `maps` | 30/phut/nguoi | Proxy Goong |
| `upload` | 20/phut/nguoi | Cap ticket upload |
| `qr` | 10/phut/nguoi | `/qr/resolve` |
| `chat` | 120/phut/nguoi | Gui tin qua REST |

### 9.4. Bao mat — nhung diem rieng cua vong nay

Sau vong nay, mot database chua: tin nhan gia dinh, giay to (pdf/docx), anh va video ca
nha, toan bo chi tieu, va so tai khoan ngan hang. Muc rui ro cao hon han hien tai.

1. **Bucket khong public.** Chi presigned URL TTL ngan. Kiem tra bang mot test that.
2. **Moi truy van co pham vi gia dinh deu phai qua `IFamilyContext`.** Viet mot test quet
   het controller, bat cai nao cham entity co `FamilyID` ma khong goi kiem tra.
3. **Magic bytes, `nosniff`, `Content-Disposition: attachment`** cho moi file khong phai
   media. Khong ho tro SVG.
4. **Khoang trong thu hoi token o SignalR** (muc 4.3b) — phai xu ly, khong duoc de lai.
5. **Audit log cho `TransferIntent`** — cap, thu hoi, resolve thanh cong, confirm.
6. **Log khong duoc chua**: chuoi QR tho, so tai khoan day du, presigned URL, noi dung tin nhan.
7. `/calendar/feed.ics` dung feed token rieng, thu hoi duoc — khong dung JWT.
8. Ma moi gia dinh (`InviteCode`) phai het han va co so lan dung toi da.

### 9.5. Test

- **Integration test tren Postgres + Redis that** (CI da co ca hai service). Migration EF va
  truy van `ILike` chi lo loi tren Postgres that.
- `EmvcoQrBuilder` / `Crc16Ccitt`: unit test voi payload mau co dap an biet truoc.
- Khai trien RRULE: unit test cac bien (thang 2, nam nhuan, `UNTIL` roi dung ngay bien).
- `/qr/resolve`: test rang **khong** co intent thi phan hoi khong chua bat ky truong nao
  cua payment. Do la test quan trong nhat cua M6.
- Kiem tra magic bytes: upload file doi duoi -> phai bi tu choi.

### 9.6. Migration — mot per moc

```
AddFamily                 M0
AddSavedPlaces            M1
AddChat                   M2
AddMedia                  M3
AddFinance                M4
ExtendScheduleItems       M5   -- chi ADD COLUMN, khong doi cot cu
AddPaymentQrAndIntents    M6
```

`ExtendScheduleItems` chi duoc them cot. Doi hoac xoa cot dang co la hong app Android dang
chay ngoai thuc te.

---

## 10. Thu tu lam va nhanh

Theo `docs/git-flow.md`: `feat/<mo-ta-ngan>`, nham duoi mot tuan mot nhanh.

| Moc | Nhanh | Chan boi | Uoc luong |
|---|---|---|---|
| M0 Family | `feat/family-core` | — | 3-4 ngay |
| M1 Goong | `feat/goong-places` | M0 (SavedPlace dung chung) | 3-4 ngay |
| M2 Chat | `feat/realtime-chat` | M0 | 6-8 ngay |
| M3 Media | `feat/media-storage` | M0 | 6-8 ngay |
| M4 Chi tieu | `feat/family-expenses` | M0, M3 (anh bien lai) | 5-6 ngay |
| M5 Todo/Calendar | `feat/schedule-v2` | M0, M1 | 4-5 ngay |
| M6 QR | `feat/qr-payment` | M0, M4, M5 | 6-8 ngay |
| M7 Hardening | `chore/hardening` | tat ca | 3-4 ngay |

M1, M2, M3 doc lap nhau — lam song song duoc sau khi M0 merge.

**Khong bat dau M6 truoc khi M4 xong**: `TransferIntent` phai gan duoc vao mot khoan chi
that, neu khong no chi la mot co che cap quyen lo lung khong ai kiem tra lai duoc.

---

## 11. Rui ro va diem chan

| # | Rui ro | Muc | Xu ly |
|---|---|---|---|
| 1 | Doi soat thanh toan can hop dong voi cong thanh toan | **Chan** | Ship duong "tu xac nhan" truoc; tach cong thanh toan thanh moc rieng |
| 2 | Thu hoi token khong co hieu luc voi ket noi SignalR dang mo | **Cao** | Muc 4.3b — bat buoc, khong duoc bo qua |
| 3 | App Android o repo khac; doi hop dong la hong app dang chay | **Cao** | Chi them field, khong doi. `/posts` `/todos` giu nguyen. Endpoint moi dung duong dan moi |
| 4 | `ffmpeg` la phu thuoc ha tang moi | Trung binh | Vao Dockerfile; thumbnail thieu khong lam hong asset |
| 5 | Chi phi Goong tang theo luot goi | Trung binh | Debounce + cache Redis + rate limit `maps` |
| 6 | Chi phi va quota storage | Trung binh | Quota theo gia dinh, khu trung theo sha256, canh bao o 80% |
| 7 | Gia tri du lieu tang manh -> muc tieu hap dan | **Cao** | Muc 9.4 |
| 8 | Hop dong Goong API co the khac bang o muc 3.3 | Thap | Doi chieu docs truoc khi code; mot integration test that de chot |
| 9 | Rang buoc `SUM(ExpenseShare) == Expense.AmountVnd` de lech | Trung binh | Kiem tra o service + test; xet CHECK o database |

---

## 12. Ba dieu can quyet dinh truoc khi go dong code dau tien

1. **Nha cung cap storage**: Cloudflare R2 (re, khong tinh phi egress) / AWS S3 / MinIO tu
   host. Anh huong den `S3FileStorage` va chi phi van hanh.
2. **Co theo duoi doi soat thanh toan tu dong khong**, va neu co thi qua cong nao. Quyet
   dinh nay doi hinh dang cua M6 nhung **khong chan** phan con lai cua M6.
3. **Mot nguoi mot gia dinh, hay mot nguoi nhieu gia dinh.** Thiet ke tren dang cho **nhieu**
   (`FamilyMember` la bang noi). Neu chac chan chi can mot thi don gian hoa duoc dang ke —
   nhung doi chieu lai ve sau thi rat dat.
