# Cat Pipe – prototype progress

Unity 2022.3.62f3 project: `D:\luyentayunityxgame\prototype\CatPipe`
Asset source (rip): `D:\luyentayunityxgame\asset\catdom\Assets`

## Design (v2 – kiểu sắp xếp như Catdom: Color Hole)
- Bàn lưới. Các KHỐI nhiều ô (23 hình gốc). Kéo khối trượt qua ô trống (không xoay, bị chặn bởi khối khác/tường).
- (v4) HÌNH KHỐI = HƯỚNG ĐI: dãy ô thẳng → đi thẳng và ra ở 2 đầu; góc chữ L → rẽ; giao chữ T/+ → ngã tư (đi thẳng 2 trục); khối 1 ô → mở 4 phía; khối 2x2 → vòng kín (vật cản).
- Mèo ngồi ở giỏ ngoài mép lưới, ổ = hố 1 ô màu ngoài mép. Đường nối từ giỏ tới ổ CÙNG MÀU → mèo tự chạy về ổ.
- Thắng: tất cả mèo về ổ. Thua: hết giờ, hoặc bom nổ.
- Cơ chế: khối cố định (fixed), khối màu (color – chỉ mèo cùng màu đi qua), tường (walls), băng (ice – tan 1 mỗi lần thả khối; còn băng thì không kéo/không đi qua),
  chìa (key) + khóa (lock – không kéo/không đi qua tới khi mèo đi qua khối có chìa), cổng (gate – đóng/mở mỗi 2 giây), bom (bomb – giây đếm lùi, mèo đi qua là gỡ).
- Booster: CHƯA làm (theo yêu cầu).

## ĐANG LÀM (2026-11-10): Luật đường mới + 100 màn
- User chốt: mèo TỰ TÌM ĐƯỜNG (BFS) về hố cùng màu; giữ hình khối = đường + KHỐI ĐẶC BIỆT có đường riêng (PieceJson.paths: ô 1 chỉ ngang/dọc, góc 1 ô, ngã ba 3 lối = junction rẽ tự do, ô cụt "-"); mèo đóng băng tan khi đủ N mèo về hố (khối băng vẫn tan theo lượt thả).
- 100 màn sinh bằng Tools/gen_levels.py (+ Tools/catpipe_logic.py: BFS + mô phỏng giao mèo để kiểm chứng). 50 màn đầu onboarding: mỗi mechanic mới có màn giới thiệu (thẻ Mới! icon+chữ ngắn, tay chỉ nước, xáo ít) rồi 2-3 màn luyện.
- ĐÃ XONG phần code + 100 màn (level_001..100.json, check 100/100). Lộ trình giới thiệu: 5 ngã tư, 8 ô một hướng, 11 ngã ba, 14 ô cụt, 17 màu, 20 cố định, 23 khối băng, 26 chìa/khóa, 29 mèo băng, 32 một chiều, 35 khóa trục, 38 bí ẩn, 41 cổng, 44 bom; 47-50 ôn tập; 51-100 trộn, cứ màn 10 là bàn lớn 7x8/6 mèo.
- Khối đặc biệt giới hạn ≤2/màn (≤50), ≤4 (51-100) để giữ luật hình khối = đường. Đã gỡ make_levels_09_20.py, make_levels_21_30.py, level_meta.py.
- Đã biên dịch OK. Autotest lần 1: 99/100 (màn 86 chỉ do autotest chờ 20s quá ngắn khi mèo đi đường vòng 44 ô) → mèo nhảy nhanh hơn trên đường dài, autotest chờ 60s. Khối cụt nhuộm gỗ sẫm.
- SỬA LỖI (user báo: mèo tới gần hố rồi nhảy loạn mãi mới vào): tìm đường cũ cho đi lại ô cũ → vòng vèo. Giờ BFS mang theo tập ô đã đi (ulong bitmask), cấm đi lại ô → mèo chỉ đi đường gọn, không có thì đứng chờ. Python catpipe_logic.route cùng luật (100/100 OK). Đo: tải màn ~20-30ms.
- Autotest sau sửa lỗi vòng: 100/100 WIN. Visual dễ nhìn: số mèo băng to trên huy hiệu băng, khối cụt gỗ sẫm vừa. (2 chỉnh này chỉ đổi hình, đã xem trong Editor, chưa chạy lại autotest đủ 100.)
- LEVEL DESIGN v2 (user): giảm ô đơn (TB 6.4 → 1.8/màn, tối đa 3) bằng DP lát khối lớn + giới hạn ô giao khi vẽ đường; bỏ decoy 1 ô.
  Lộ trình: 1-20 chỉ onboarding (intro 5 ngã tư, 7 ngã ba, 9 ô một hướng, 11 ô cụt, 13 màu, 15 cố định, 17 khối băng, 19 chìa/khóa).
  Từ 21: khối 5 màn = giới thiệu (21 mèo băng, 26 một chiều, 31 khóa trục, 36 bí ẩn, 41 cổng, 46 bom) / 3 màn luyện / màn KHÓ (x5) ; x10 = SIÊU KHÓ (7x8, 5 mèo, xáo x7).
  LevelJson.difficulty (1 khó, 2 siêu khó) → nhãn trên map + băng rôn "Màn N • Khó".
- LEVEL DESIGN v3 (user: số khối vừa đủ, nhiều mèo cùng màu, siêu khó chưa đủ khó): Tools/solver.py = bộ giải BFS chính xác số nước kéo tối thiểu (luật giống game; có lọc chỉ kiểm mèo bị ảnh hưởng, đã đối chiếu 0 sai khác). Đo màn cũ: siêu khó chỉ 2-5 nước.
  gen_levels.pick_start: xáo nhiều cách + leo đồi, giữ thế có số nước tối ưu trong dải: intro 1-2, onboarding 2-4, thường 3-7 tăng dần, khó 6-8/7-9, siêu khó 9-13/10-14. Bàn chật theo độ khó (ô trống 35%→18%).
  Mèo cùng màu: giới thiệu màn 20; thường 35%, khó 1 cặp, siêu khó 2 cặp (4 mèo). JSON có start + solution (lời giải tối ưu, tay hướng dẫn + autotest dùng); game: ApplyStart().
- MAP đã xong (Scripts/MapScreen.cs) và đã chụp kiểm tra: nút gỗ+sao / nút vàng+mèo / khóa nhỏ + số mờ, bong bóng icon màn giới thiệu.
- TIẾP THEO (user yêu cầu): tối ưu visual cho dễ nhìn + thiết kế MAP màn chơi (kiểu Journey Catdom). KHÔNG đẩy git cho tới khi user bảo.
- MAP: Scripts/MapScreen.cs; 5 chặng x 20 màn (map_bg1..5 = journey1, journey4, BG_1, BG_0, BG_8), nút map_node/map_node_cur, khóa, sao, bong bóng icon màn giới thiệu, nhãn Khó; lưu PlayerPrefs CatPipe_Unlocked; thắng → về map.

## WEB DEMO (GitHub Pages, công khai theo yêu cầu user)
- Link: https://dylananatole.github.io/democatpipe/  — repo D:/luyentayunityxgame/prototype/democatpipe (chỉ chứa bản build HTML).
- Cập nhật: Unity menu CatPipe/Build WebGL → xóa Build/ + index.html trong democatpipe → copy Build/WebGL/* vào → git add -A, commit, push. Pages tự deploy ~1 phút.
- Template Assets/WebGLTemplates/CatPipe (co vừa 9:16), tên file theo hash (tránh cache trộn bản cũ), layout tự tính lại khi canvas đổi kích thước.

## XONG: Avatar + motes (autotest 30/30)
- Mèo avatar góc trên trái = Spine IP_ingame (skin no_bg, material ip_ingame_IP_ingame bật straight alpha): idle/idle2, đứng yên 6s → ip_thinking, <=10s → ip_worried, mèo vào hố → ip_like, thắng → ip_cheer, thua → worried, chạm avatar → cheer.
- Đốm sáng mờ trôi trên nền (Glow). Đã gỡ ảnh tĩnh ui_ava.

## XONG: Feeling v11 (user: khối giật + mèo đi xấu) – autotest 30/30, test dao động bằng code: 0
- Kéo: trợ giúp góc không còn đánh nhau với trục kia; cộc chỉ 1 lần khi mới chạm; thả tay thì khối lướt vào ô (GlideFrom).
- Mèo: nhảy từng ô theo vòng cung; dáng theo hướng (jump3 ngang + lật mặt, jump2 lao xuống, back_jump đi lên); cú cuối cao hơn, đáp có nén, rồi select_to_sit → front_idle1.

## XONG: Feeling v10 (autotest 30/30; kéo va chạm đã test bằng code trong Play mode; chưa chơi tay) (tham khảo Catdom: khối va chạm thật)
- Kéo khối có va chạm liên tục: trượt dọc vật cản, trợ giúp vào góc, "cộc" khi bị chặn (nảy + rung khối chặn + âm Fence), viền sáng khi cầm, nảy khi thả.
- Chạm khối không kéo được: rung + nháy đỏ + âm Lock. Âm thanh gốc: Resources/Audio (pick/drop/bump/locked/meow1-7/hole/key/ice/unfreeze/mystery/bomb/tick/win/confetti/lose/click/banner).

## XONG: Spine + onboarding + màn 21-30 (autotest 30/30 WIN, ảnh mèo Spine OK) (user cho phép dùng/cài Spine)
- spine-unity 4.2 (git UPM, dữ liệu gốc 4.2.43). Mèo: cat_ingame.json skins do/vang/xanhduong/xanhla/hong/timxanh/cam/xanhngoc/trang; anim front_idle1-4, jump1-3, ice1-3(+_idle), unfreeze, touch_front.
- Onboarding: bàn tay hand_tut (anim click) chỉ thao tác kéo ở màn 1-2 (JSON "tutorial":true; tay chạy theo nước xáo đảo ngược, dừng khi người chơi tự đi khác).
- Chữ tối thiểu: thanh đáy = icon (hintIcon) + 1-3 chữ (hint); băng rôn chỉ "Màn N". Hố đập nhịp khi đường tới nó đã nối.
- Mèo Spine: SkeletonAnimation, skin theo màu, idle ngẫu nhiên front_idle1-4, đi = jump1, tới hố = select→select_idle, băng = iceN_idle / iceN / unfreeze.
- QUAN TRỌNG: atlas gốc là straight alpha → material Spine (cat_ingame_Material, hand_tut_Material) phải bật Straight Alpha Texture (_STRAIGHT_ALPHA_INPUT), nếu không mèo bị khối chữ nhật quanh từng bộ phận. Nếu Spine tạo lại material thì bật lại.
- Đã gỡ CatSpriteBaker + Sprites/cats (mèo tĩnh) vì đã dùng Spine.
- Màn 21-30: Tools/make_levels_21_30.py. Gợi ý/icon/tutorial của mọi màn: Tools/level_meta.py (chạy sau khi sinh màn).

## XONG: Visual v9 – autotest 20/20 WIN
- Thanh gỗ đáy (bg_bot_bar) + gợi ý từng màn (LevelJson.hint), băng rôn mở màn (title_violet) hiện Màn N + tên, bóng dưới chân mèo, dấu chân mèo khi đi.

## XONG: Visual v8 (asset gốc thêm) – autotest 20/20 WIN + ảnh popup thua (shot_lose.png)
- Nền bg_2 toàn màn (thay ván), popup thắng: ruy băng xanh + chữ well_done + vầng effbg; thua: ruy băng đỏ + chữ + mèo cat_3; đồng hồ đập khi <=10s.

## XONG: Mechanic mới + màn 9-20 (autotest 20/20 WIN; độ khó chưa playtest bằng tay)
- axis "H"/"V" (khóa trục – chỉ trượt ngang/dọc; icon 2 mũi tên arrow_0), mystery true (che đường bằng block_hoi_cham + "?", lộ khi kéo lần đầu hoặc mèo đi qua),
  oneway "N/E/S/W" (chỉ đi theo hướng; chỉ dùng cho khối thẳng; icon arrow 1), cats[].ice N (mèo đóng băng, tan 1 mỗi lần thả khối).
- Màn 9 khóa trục, 10 bí ẩn, 11 mèo băng, 12 một chiều, 13-20 kết hợp. Kiểm tra bằng Tools/check_levels.py (đã thêm oneway). Màn 9-20 sinh từ Tools/make_levels_09_20.py (sửa ở đó rồi chạy lại để ghi JSON).

## XONG: Visual v7 (juice) – autotest 8/8 WIN; bóng mờ khi kéo chưa kiểm chứng bằng tay
- Đệm catbed 2 thay giỏ; dấu chân leg_cat trắng trong hố; cổng = thanh xa hong; lấp lánh Flare01 khi mèo vào hố.
- Kéo: bóng mờ chỗ thả + bóng đổ sâu khi nhấc; mở màn: khối pop-in lần lượt, mèo rơi xuống nảy; thắng: popup phóng to + pháo giấy (UI).

## XONG: Visual v6 (autotest 8/8 WIN) (tham khảo Catdom 2 No Timer + Catdom Color Hole)
- Đường đi: BỎ rãnh nâu/tô màu/dây. Thay bằng chuỗi chấm trắng mềm (circle_soft02) theo hình khối; khi mèo nối tới → chấm màu mèo + quầng Glow + nhấp nháy chạy theo đường.
- Nền ván gỗ (bg_go xếp chồng), viền bàn gỗ dày nổi khối, HUD: ava mèo trái, viên Level tím (Bg1) giữa + đồng hồ (time + Icon_Time_Big), nút cam chơi lại (btn_orange + retry) phải.
- Chữ "Nice!" bay lên khi mèo vào hố; popup thắng có tia sáng (backlight_circle) quay.

## Visual (v5)
- Khối: sprite gốc; trung tính = viền trắng gốc nhuộm caramel, khối màu = sprite màu gốc, khối "chỉ mèo trắng" (color 9) = viền trắng gốc, cố định = xám. Có bóng đổ.
- Đường đi: rãnh nâu đặc (sprite groove = trang_1 9-slice) luôn hiện theo hình khối; khi đường mèo chạm tới → rãnh tô màu mèo + dây gốc cùng màu (mèo trắng = dây trang).
- Mèo: 9 màu nướng từ skin gốc (thêm White = 9), mèo đứng chờ có nhịp thở. Ổ = hố 1 ô gốc (trắng dùng b0_13).
- Autotest chụp thêm shot_XX_m.png (giữa lúc tự giải) để xem nét đường khi có mèo nối.

## Level JSON (Assets/CatPipe/Resources/Levels)
- Chỉ cần thiết kế trạng thái ĐÃ GIẢI. Game tự xáo bằng các nước kéo hợp lệ (seed theo level) → luôn giải được. Phím S = chạy ngược nước xáo.
- `pieces[].at` = "x,y" anchor; `cells` = "dx,dy dx,dy ..." (y hướng xuống, phải là 1 trong 23 hình gốc). Khối ice/lock/fixed/gate không bị xáo.
- Kiểm tra màn trước khi vào Unity: `python Tools/check_levels.py Assets/CatPipe/Resources/Levels` (cùng logic hướng đi với game).
- `time` giây, `scramble` số nước xáo (0 = 3 × số khối kéo được), `walls` = "x,y x,y".
- cats/nests: side L/R/T/B, index = hàng (L/R) hoặc cột (T/B). Màu 1-based: 1 đỏ, 2 vàng, 3 xanh dương, 4 xanh lá, 5 hồng, 6 tím, 7 cam, 8 xanh ngọc, 9 trắng. Khối: color 0 = trung tính.

## Trạng thái
- [x] Khảo sát asset, chọn sprite (cat_1 → xám để nhuộm màu, catbed 2 → ổ, gio_sinh_meo → giỏ, Trang_to14 → ống 9-slice, key/lock/ice/bomb)
- [x] Scripts: LevelData.cs, Piece.cs, GameController.cs, Editor/CatPipeSetup.cs
- [x] 8 level (01 kéo ống, 02 góc rẽ, 03 ống màu+cố định, 04 băng, 05 chìa/khóa, 06 cổng+bom, 07 tổng hợp+tường, 08 bốn mèo)
- [x] Scene Assets/CatPipe/Scenes/CatPipe.unity, build Windows ở Build/ (menu CatPipe/Build Windows)
- [x] Autotest: `Build/CatPipe.exe -autotest` → 8/8 màn WIN (v2), chụp ảnh shot_XX_a/b.png

## Asset gốc trong project
- `Assets/CatdomAssets/` = copy từ rip (Texture2D, Sprite json, AudioClip, Font, Mesh, PrefabHierarchyObject, TextAsset, _Project, _Game, SpriteAtlas).
- `Resources` gốc đổi tên thành `OriginalResources` (để không bị đóng gói vào build). Bỏ qua các thư mục setting engine.
- File .glb chưa import được (cần package glTFast nếu muốn dùng model 3D).

## Unity MCP
- Package `com.coplaydev.unity-mcp` v10.3.0 – ĐÃ CÀI & KẾT NỐI (instance CatPipe, HTTP 8080).
  Mỗi lần mở Unity: Window → MCP for Unity → Start Server.

## Phím debug (nội bộ)
R chơi lại · N màn sau · P màn trước · S tự giải

## XONG: visual gốc (v3) – autotest 8/8 WIN
- Lưu ý: sprite thêm vào Resources/Sprites cần import SAU khi importer có rule (nếu PPU sai: xóa .meta rồi refresh).
- Build bật runInBackground để autotest chạy khi cửa sổ mất focus.
- [x] Copy sprite gốc vào Resources/Sprites: `blocks/b{màu 0-8}_{hình 1-23}` (0 = Trang trung tính, 1 do, 2 vang, 3 Xanh_duong, 4 xanhla, 5 hong, 6 timxanh, 7 cam, 8 xanhngoc; PPU 114 = 1 ô),
  `ropes/r{màu}_{doc|ngang|goc}` (dây color path làm ống; goc = góc N-E), `board_frame`, `board_cell`, `wall`.
- Bảng hình 23 khối (ô chiếm chỗ, gốc trên-trái): 1:1,0 0,1 1,1 2,1 | 2:1,0 0,1 1,1 1,2 | 3:0,0 1,0 2,0 1,1 | 4:0,0 0,1 1,1 0,2 | 5:0,0 1,0 2,0 2,1 | 6:0,0 1,0 2,0 0,1 |
  7:0,0 0,1 1,1 2,1 | 8:2,0 0,1 1,1 2,1 | 9:0,0 1,0 1,1 1,2 | 10:0,0 1,0 0,1 0,2 | 11:0,0 0,1 0,2 1,2 | 12:1,0 1,1 0,2 1,2 | 13:0,0 | 14:2x2 | 15:+ | 16:0,0 0,1 1,1 |
  17:1,0 0,1 1,1 | 18:0,0 1,0 1,1 | 19:0,0 1,0 0,1 | 20:dọc 3 | 21:ngang 3 | 22:ngang 2 | 23:dọc 2
- [x] Editor/CatSpriteBaker.cs (menu CatPipe/Bake Cat Sprites): nướng mèo gốc từ Cat.glb + atlas cat_ingame → Sprites/cats/cat_1..8.png (skin Red,Yellow,Blue,Green,Pink,Purple,Orange,Aqua). ĐÃ CHẠY, ảnh OK.
- [x] Piece.cs vẽ khối bằng blocks/b*, ống bằng ropes (trắng; khi có luồng → dây màu mèo). Ổ mèo = khối 1 ô màu (b{c}_13). Bàn: board_frame + board_cell nâu, tường = wall.
- [x] Build + autotest + xem ảnh.

## Asset gốc còn dùng được (đã xem, chưa dùng)
- bot_bter: thanh gỗ đáy thứ 2 (bg_bot_bar đã dùng cho gợi ý) → thanh booster sau này. topbar (thanh nâu có tai mèo) → nền HUD trên.
- banner_finish (bảng treo), Bg_popup_0 (popup cao), title_popup_color* khác màu, ribon tím, lightray*, effbg.
- Atlas Spine (cat_ingame, level_fail, time_out, ice, curtain, elevator, scissors...) cần spine-unity nếu muốn animation thật.

## Việc tiếp theo (chưa làm)
- Booster (theo kế hoạch của user)
- Âm thanh/FX (asset có sẵn ogg + fx), thêm level, menu/chọn màn

### v3 installed (2026-11-10)
- 100 levels from `Tools/gen_levels.py` (70 min run) installed; check_levels 100/100 OK; Windows autotest 100/100 WIN.
- Min drags: 1-20 avg 2.2 | 21-50 normal 3.4, hard 6-7, super 9 | 51-75 normal 5.2, hard 7-9, super 10 | 76-100 normal 5.8, hard 7-8, super 10.
- Same-colour cats in 31 levels (all hard/super). Not pushed to git (user asked to wait).

## LEVEL DESIGN v4 (installed, 2026-11-10) — designer sketch rules
- Path rules: straights + L blocks = own shape as path, both ways or one-way (`oneway` = side the path exits; per-cell
  outDir walk in Piece.BuildOutDirs / catpipe_logic.out_dirs / DeepSolver). Singles: straight/corner/T/cross/one-way.
  T tetrominoes + 2x2 = crates (no path) drawn with grey `blocks/sh_N` (tinted); any no-path block uses sh_N.
- Mystery = `blocks/hid_N` cover of the same shape (hides path or crate). Generator hides a road block + same-size decoys.
- Cats/holes on board cells: EdgeRef.at "x,y" (occ CatCell -3 until it leaves, HoleCell -4). Intro level 12.
- Tools/DeepSolver (C#, dotnet 9, build: `dotnet build -c Release -o ../bin/deepsolver` in Tools/DeepSolver):
  component BFS from solved layout + exact solver; gen_levels.py calls it per layout. Packed boards (2-5 free cells).
- Bands: 1-2:(1,2) 3-4:(2,3) intros<=20:(2,4) <=12:(3,5) <=20:(4,6) normal 21+: 6..10 +2, hard 20-25, super 28-35.
  Solution must move >= use share of movable blocks (0.6 normal, 0.7 hard).
- Installed: 100 levels in Resources/Levels (check_levels 100/100 OK). Moves: normal 21+ = 9-12, hard (25,35..95) = 20-25,
  super (30,40..100) = 28-31. Generation of 50-100 took ~62 min (gen log: $TEMP/gen4b.log).
- Autotest: 99/100 first run; level 70 failed only because the timed gate was closed when the scripted move landed
  (solver treats gates as open). AutoSolve now waits for gates to be open before each move.
- PUSHED (user asked, 2026-11-10): web demo democatpipe commit 945ef01, live at https://dylananatole.github.io/democatpipe/
  (built before the autotest gate fix, which only affects -autotest, not gameplay).

## MAP + VISUAL PASS (2026-11-11)
- Map: all levels open (MapScreen.Show locked=false, Tap always plays). Pushed web demo e9ebb2f (user asked).
- Paths: dark opaque groove under the dots (Piece.BuildDots, GrooveIdle, sprWhite bars + sprDisc hub); outer ends stop at 0.46
  so they don't poke past the rounded rim; groove takes the cat color along a live route.
- Depth: "Side" copy of the block sprite under it (-0.07, darker) = raised slab; dust puffs under the lower edge on landing.
- Praise: outlined word (8 dark copies + shadow) in the strip above the board; quick successive cats -> "Combo xN!".
- Win popup: 1-3 stars by time left (>=50% 3, >=25% 2), pop in with sparks (StarsIn).
- Layout: side margin 2.6 -> 2.25 (board ~4% bigger on phones); bomb/ice counters on dark disc/pill badges (sprDisc, runtime).
- Intro card label wraps + best-fit (long names like "Mèo trong bàn: mở lối ra hố" no longer overflow).
- Autotest: `-from N -to M` level range. Helper scripts (not in project): $TEMP/mcpcall.sh (direct HTTP MCP call when the
  session's Unity MCP tools are disconnected), $TEMP/shots.sh (build Windows + autotest ranges; SHOT_TIMEOUT env).
- NOT pushed to git (user: "chưa up git vội").

## LOADING + HOME + WIN POPUP (2026-11-11)
- FrontScreens.cs: loading and home over the original splash art (Sprites/ui_splash, cover-cropped).
  Loading: title "CatPipe", purple track + orange fill (ui_load_track / ui_load_fill, 9-sliced in CatPipeSetup),
  a Spine cat (SkeletonGraphic, skin vang, "jump3" loop) hops along the bar as it fills, leaps into its bed (pad)
  and sits ("select_to_sit"); ~3.9 s then home. Home: title + tagline, green Play (ui_btn_play, "Chơi / Màn N" ->
  current level), orange "Bản đồ" -> map. Map has a house button (ui_home) back to home.
- UI Spine material: Resources/Spine/SkeletonGraphicStraight.mat (copy of the package's straight-alpha
  SkeletonGraphic material; cat atlas is straight alpha).
- Win popup: 3 Spine cats (level colors first) hop in a wave (WinCatsJump) instead of the cat image; fireworks
  (FireworkFlash/Spark, behind the popup, Audio/firework.ogg) for 4 s; stars moved onto the ribbon's lower edge.
- Autotest `-front`: screenshots shot_loading / shot_loading_end / shot_home / shot_map then quits.
- NOT pushed to git.

## LEVEL SET v5 + STARS / HEARTS / NEW RESULT SCREENS (2026-11-11)
- Tools/gen_levels.py v5 rebuilt all 100 levels (seed Random(5000+n), ~3.6 h). Levels 1-20 are onboarding,
  one new mechanic every 1-2 levels. From 21 on the levels come in cycles of 5: 3 easy (5-8 moves), 1 medium (10-14),
  then one of hard 18-22 / very hard 23-27 / super 28-35, in turn. Pieces are capped near the move count, so the
  board has no idle blocks. time = min(300, max(60, round10(40+7*moves+10*cats))); par = moves+max(2,ceil(moves/4));
  parTime = round10(time*0.6). difficulty: 1 hard, 2 very hard, 3 super.
- New-mechanic intro card on its first level: 4 cross, 5 dead, 6 junction, 7 special, 9 oneway, 11 rotate,
  13 multicat bed, 15 ice, 17 keylock, 19 inner, 21 frozen, 26 color, 31 fixed, 36 axis, 41 mystery, 46 bomb.
  The card text comes from introTitle / introText / introIcon.
- Gates are replaced by rotating blocks (pieces[].rotate). Tapping a block turns its path 90 degrees, and the block
  can still slide. The level field `rot` holds "pi:q" start turns; "pi:R" in a solution means a tap. DeepSolver and
  catpipe_logic support both.
- Multi-cat beds (cats[].count): one cat sits on the bed with an "xN" badge, and the next cat hops up when it leaves.
  A bed needs N same-colored holes.
- Stars (best saved in CatPipe_Stars_i): 1 for winning, 1 for moves <= par, 1 for time <= parTime.
  The HUD shows "Nước đi: X / par".
- Win screen: 3 jumping cats with fireworks, then 3 stars with labels and "Tiếp tục". Lose screen: broken heart,
  "-1", hearts left, "Chơi lại" and "Về nhà".
- Hearts: 5 max, 1 back every 60 s (test rate). You lose a heart on a fail, and on a retry after a move.
  Every way into a level checks hearts. The widget is on home and map. With no hearts a popup offers
  "Nạp đầy tim (test)".
- NOT pushed to git.
- 2026-11-11: removed the "Nước đi" line from the HUD (moves still count for the star). All levels open on the map.
  PUSHED (user asked): web demo democatpipe commit f277d09.

## SETTINGS + BOOSTERS (2026-11-11)
- Gear button (Catdom Setting_Icon) replaces the HUD retry button. The settings popup pauses the level
  (Time.timeScale 0) and has these buttons: sound on/off (AudioListener.volume, saved in PlayerPrefs CatPipe_Sound),
  Continue, Retry, Level select (map) and Home. Retrying or leaving mid-level after a move costs 1 heart.
  Tapping outside the card resumes the level.
- Boosters on the bottom bar, free while testing (green ∞ tag):
  - "Gợi ý" (magic wand): if the board has left the level's solution, the last moves are undone until it is back
    on it. Then the tutorial hand shows the next move once (hintOnce). It uses the move history
    (OnDrop -> history), startAt/startMask and SolutionProgress().
  - "Về tổ" (cat): arms a pick mode, and the next tap on a cat sends it in one big leap into a free hole of its color
    (Walk fly). This thaws a frozen cat, and the bed queue advances as usual.
- Autotest `-ui [-from N]`: screenshots shot_ui_settings / hint / pick / fly / send (level 22 by default).
- PUSHED (user asked): web demo democatpipe commit 4bebc74.
- Deploy note: GitHub Pages serves index.html with max-age=600. When the old hashed build files are deleted, a browser
  holding the old cached index fails ("both async and sync fetching of the wasm failed") until the cache expires or
  the user hard-reloads. On the next deploys, keep the previous build's files in democatpipe/Build for one more push.
- 2026-11-11: win popup plays one sound at a time: only the win jingle (confetti and firework clips removed),
  and a short click per star instead of the 1.4 s key clip. Settings sound row: speaker icon no longer overlaps
  the label. Pushed democatpipe 8790a9e + the sound fix.
- 2026-11-11 sounds: the old block clips were harsh. The wall bump was Fence (1 s, peak 0.99) and the locked block
  was Lock (1.8 s, hissy). New sounds, all from the original Catdom clips:
  - pick: block_pick (Pick.ogg, 0.04 s, vol 0.5)
  - bump: soft drop at 0.35, at most one every 0.3 s
  - locked: block_locked (lockbox tap, 0.07 s)
  - rotating tap: block_turn (lockbox turn)
  - drop: 0.75 when the block moved, 0.3 when it didn't
- The hint booster is locked for now (grey icon + padlock; a tap shakes it and says "Gợi ý tạm khóa").
  Hint() and the -ui test path are kept for when it comes back.
  PUSHED (user asked): democatpipe 2cee76b.

## PREPARED, NOT RUN: level set v6 with 10 onboarding levels (2026-11-11)
- Tools/gen_levels.py now has ONB_END = 10. The v5 generator is backed up at $TEMP/gen_levels_v5.py.
- Onboarding 1-10: 1-3 basics, then 4 cross, 5 dead, 6 junction, 7 special, 8 rotate, 9 multicat bed, 10 oneway.
- From 11, cycles of 5 (3 easy, 1 medium, then hard / very hard / super in turn): hard levels are 15, 20 (very hard),
  25 (super), 30, ... The other mechanics come on the first easy level of a cycle: 11 ice, 16 keylock, 21 inner,
  26 frozen, 31 color, 36 fixed, 41 axis, 46 mystery, 51 bomb.
- A test run of levels 8-11 into a temp folder passed check_levels (the rotate intro needs a tap). Game levels are
  unchanged. The full run is `python -I Tools/gen_levels.py <out> ` (~3.5 h), then check_levels, copy into
  Resources/Levels and run the autotest.

## LEVEL SET v6 + "MỚI" POPUP WITH LIVE DEMO (2026-11-13)
- Research: YouTube walkthroughs cut the feature popups, so the original assets were used instead. Catdom has
  ui/popups/uinewfeaturepopup ("New Feature!" art, a wood background, a content box) and a dedicated tutorial
  variant of each mechanic's first level (Resources/tut_mechanic/LevelXXXX). Our popup uses its own style:
  - A purple pill with the mechanic name, its icon in a white bubble and a pulsing red "MỚI" tag.
  - The orange Spine cat sits on the card corner, and a soft rotating glow sits behind the card.
  - A demo window in a wooden frame. DemoStage.cs builds a tiny board from real Piece objects and Spine cats at
    world x=1000, filmed by its own camera into a RenderTexture, with one short looping script per mechanic
    (the hand drags/taps, the path lights, the cat hops home).
  - Two very short numbered tips (3-5 words, user asked for brevity) that light up as the demo reaches them
    (DemoStage.OnStep).
  - A "Thử ngay!" button; tapping anywhere after 2.5 s also closes it.
  - After closing, the level's blocks carrying the mechanic flash 3 times (Piece.Has).
  - Level JSON gains `introFeat`. Autotest `-demo` captures shot_demo_<feat>_a/b for all 16 mechanics.
- Generator v6 (gen_levels.py):
  - Hand only on levels 1-3; intros teach through the popup.
  - Hard levels use few blocks that move many times: hard 5x6 ≤10 blocks (18-22 moves), very hard 5x6 ≤11
    (23-27), super 5x6 ≤12 (28-35). There are 3 cats, 2-3 mechanics mixed, and every block must be used (use 1.0,
    relaxing to 0.85). Spare cells become walls, giving an irregular tight board.
  - Up to 60000 attempts per level, with one more block allowed every 15000 failed attempts.
  - Test results: 15 = 10 blocks / 22 moves; 20 = 11 / 26; 25 = 12 / 31.
- 2026-11-13: the user asked for 50 levels, not 100 (gen_levels.LEVELS = 50). The bomb intro moved from 51 to 48,
  so all 16 mechanics appear. The last hard level is 50 (very hard).

## 10-LEVEL DEMO (2026-11-13)
- The project was rejected, so the game now ships only 10 demo levels: no onboarding, no hand, no intro popup,
  each level mixes mechanics. `python Tools/gen_levels.py --demo` writes them from the DEMO dict (ids 101-110,
  saved as level_001..010, named "Màn i"). Every level also has dead crates.
  - 1 easy: rotate. 2 easy: multicat, junction. 3 medium: ice, oneway. 4 easy: keylock.
  - 5 hard: color, rotate, multicat (10 blocks / 18 moves). 6 medium: inner, fixed. 7 easy: mystery, cross.
  - 8 very hard: frozen, axis, ice (11 / 23). 9 medium: bomb, keylock. 10 super: oneway, rotate, color (12 / 35).
- The old 100-level set (v5, with onboarding and tutorials) is archived for reuse in LevelArchive/levels_100_v5/
  (outside Assets, so it is not built) and in the democatpipe repo under level_archive/levels_100_v5/. To use it
  again, copy the files back into Assets/CatPipe/Resources/Levels.
- The intro popup and DemoStage code stay in place; they are unused while no level has introFeat.

## CONSUME TRIAL: ROADS VANISH (2026-11-14)
- New rule, per level (`"consume": true` in the level JSON): once a cat is home, every block it walked over vanishes
  (GameController.ConsumeRoute + Vanish). The cats' roads overlap, so they can't all stand at once: build one road,
  let that cat go, then build the next road from the blocks that are left. The player has to plan the order and keep
  enough of the right blocks. Levels without the flag play exactly as before.
- DeepSolver: Route returns the route's cells, and Deliver removes those blocks (`gone` bit set in the state key).
  Consume levels have no single solved layout. ConsumeMain drops the blocks at random free spots (fixed blocks stay at
  "at"), skips layouts where a cat goes home at once, solves each exactly, and keeps one whose fewest moves fall in
  the band.
- Generator: `python Tools/gen_levels.py <out> --consume` writes the CONSUME set (201-210 -> level_001..010).
  It uses its own road tiling (one road per cat, never shared), roads must overlap (1 cell on easy, 2 otherwise),
  1-2 crates and maybe a decoy, and the same mechanics as before (rotate, ice, keylock, color, oneway, fixed, mystery,
  frozen, axis, bomb). In the file, "at" = "start" (the start layout).
- First set: moves 5/7/11/8/15/9/7/17/12/19 (1 and 10 easiest and hardest).
- Going back: the previous 10 demo levels are in LevelArchive/levels_10_demo_v1 (copy them back into
  Assets/CatPipe/Resources/Levels). The code before this trial is git commit f9107ad5.

## FEEL PASS: RAIL PATHS, BLOCK MOTION, SOUNDS (2026-11-14)
- Path look: the row of dots is gone. Each path cell is a dark groove with a continuous rail inside (straight
  strips plus a round joint, so corners read as one line). Idle rail: thin and faint. On a cat's route: thicker, the
  cat's color, a soft glow, and a gleam running toward the hole (Piece.SetFlow / Update).
- Block motion (Piece.SetDragging / Hold / Land): picked up, the block springs up (grows 7%, longer shadow, warm rim);
  held, it follows the finger with frame-rate-independent smoothing and leans up to 6 degrees with its speed; released,
  it glides into its cell with a small overshoot, then squashes with a puff of dust. Scale and lean pivot on the
  block's center. AutoSolve / hint moves use the same motion (ScriptedDrag).
- Sounds (new clips from the Catdom audio): cell_tick for each cell a dragged block crosses (pitch rises),
  path_grow when a road gets longer, path_done when a road is complete, cat_whoosh at take-off, nest_in on landing,
  vanish for each consumed block (staggered, rising), ice_crack when ice is gone, lock_open when a key opens locks,
  star coin chimes on the win panel. Sounds play through a pool of 8 sources so pitches don't bend each other.
- Autotest `-drag` films one drag of level 1 in phases (shot_drag_lift / mid / land / end).
