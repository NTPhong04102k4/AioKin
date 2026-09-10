<!--
Tieu de PR dung dung dinh dang commit: <type>(<scope>): <mo ta ngan>
Vi du: feat(auth): add Facebook SSO callback handling
-->

## Thay doi gi

<!-- Mot doan ngan: lam gi va TAI SAO. Phan "lam gi" nguoi doc tu diff cung thay,
     phan "tai sao" thi khong. -->

## Vi sao can thay doi

<!-- Van de dang giai quyet, hoac issue lien quan: Refs #123 / Closes #123 -->

## Kiem thu the nao

<!-- Cach nguoi review tu kiem chung. Vi du:
     - `dotnet test`
     - POST /auth/login voi mat khau sai 5 lan -> tai khoan bi khoa 5 phut
-->

## Checklist

- [ ] `dotnet build AioKin.sln -c Release` chay sach, khong warning moi
- [ ] Da tu kiem thu duong di chinh (happy path) va it nhat mot duong loi
- [ ] Doi schema -> da them EF migration (`dotnet ef migrations add <Ten>`)
- [ ] Doi/them cau hinh -> da cap nhat `appsettings.json` (gia tri rong) va `docs/`
- [ ] **Khong co secret trong diff** — API key, connection string production, `Jwt:Key` that
- [ ] Doi hanh vi auth -> da ghi ro trong phan mo ta anh huong toi client dang chay

## Anh huong toi bao mat

<!-- Bat buoc dien neu PR cham vao auth, token, mat khau, phan quyen hoac Redis key.
     Khong lien quan thi ghi "Khong". -->

Khong

## Ghi chu trien khai

<!-- Bien moi truong moi, buoc migration, thu tu deploy, kha nang rollback.
     Khong co thi ghi "Khong". -->

Khong
