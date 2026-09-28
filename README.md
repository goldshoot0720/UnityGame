# 萌友遊戲大廳 MoeGames（Unity 版）

把 [PhaserGame](https://github.com/goldshoot0720/PhaserGame) 的 12 款網頁遊戲，完整移植到 **Unity 6.3 LTS（6000.3.25f1）** 的單一 3D 專案：一個遊戲大廳、12 款遊戲、8 位萌友角色（FBX 3D 模型）。

- 遊戲規則、AI、數值 **1:1 移植**自各遊戲的雲端版原始碼（`GameN/cloud/src`），並附上移植自 `verify.ts` 的單元測試（**77 個 EditMode 測試全部通過**）。
- 3D 呈現、介面、觸控操作、遊戲指南全部重新製作。
- 支援 **網頁（WebGL）、Android、Windows、macOS**；手機網頁與 Android 版有虛擬搖桿與觸控按鈕。

## 12 款遊戲

| # | 遊戲 | 類型 | 內容 |
|---|---|---|---|
| 1 | 萌友棒球對決 | 棒球 | 捕手視角，打擊圈與揮棒時機、六種球種投球、三局制＋延長賽 |
| 2 | 萌友街頭 3x3 | 籃球 | 3 對 3 半場鬥牛，蓄力投籃、傳球、抄截、封蓋、清球規則，先得 21 分 |
| 3 | 萌友卡丁車 GP | 賽車 | 8 車 3 圈、漂移迷你加速、4 種道具、三條賽道（3D 閉合賽道） |
| 4 | 萌友洛克英雄 | 橫向動作 | 7 位頭目＋弱點循環、9 種武器、最終要塞連戰與三段變形守護者 |
| 5 | 萌友格鬥王 | 格鬥 | 60 FPS 逐格判定、指令必殺、超必殺、反擊架式，對電腦或雙人 |
| 6 | 萌友戰棋・八方對決 | 戰棋 | 4 對 4、地形防禦、射程與反擊、敵方 AI |
| 7 | 萌友大亂鬥 | 俯視射擊 | 8 人混戰、8 種武器、道具、Bot AI |
| 8 | 萌友卡牌對決 | 卡牌 | 法力曲線、嘲諷、衝鋒、登場效果、電腦對手 |
| 9 | 萌友大富翁 | 大富翁 | 4 人、買地蓋房、機會卡、破產變賣、角色能力 |
| 10 | 萌友瘋狂坦克 | 回合制砲擊 | 可破壞地形、風、8 種特殊彈、AI 彈道搜尋 |
| 11 | 萌友戰機 2026～2027 | 縱向射擊 | 8 種主砲、兩關、中頭目與三階段頭目、GPU 實例化彈幕 |
| 12 | 萌友水球大作戰 | 水球對戰 | 連鎖爆炸、水泡陷阱、道具、會找退路的 Bot |

每款遊戲都有 **遊戲指南／攻略**：遊戲內按 **G** 或點「遊戲指南」開啟，文字版在 [`Docs/Guides/`](Docs/Guides/)。

## 下載遊玩

到 [Releases](https://github.com/goldshoot0720/UnityGame/releases) 下載：

- **網頁版**：`MoeGames-WebGL.zip`（解壓後以任一靜態網頁伺服器開啟 `index.html`）
- **Android**：`MoeGames.apk`
- **Windows**：`MoeGames-Windows.zip`（解壓後執行 `MoeGames.exe`）
- **macOS**：`MoeGames-macOS.zip`（未簽章：第一次開啟請按右鍵 →「打開」）

## 操作

- 大廳：方向鍵／點擊選遊戲，Enter 開始，數字鍵 1–9 快速進入，G 開啟指南。
- 遊戲中：Esc 返回，G 指南；各遊戲按鍵見指南的「操作方式」。
- 觸控裝置：自動顯示虛擬搖桿與該遊戲的按鈕，右上角「返回」；手機請橫向持握。

## 專案結構

```
Assets/
  Scripts/Core/            共用框架：大廳 App、MiniGame、IMGUI 介面、角色 Chibi、音效合成、觸控、指南、素材登錄表
  Scripts/Games/Game1..12/ 各遊戲：GameNRules（規則/AI，純 C#）、GameNGame（3D 呈現）、GameNGuide（指南）
  Tests/Editor/Game1..12/  各遊戲的 NUnit EditMode 測試（移植自 verify.ts）
  Editor/                  素材登錄表、指南匯出、一鍵建置（MoeGames 選單）
  Characters/              8 位角色 FBX（Humanoid）
  PhaserAssets/            原版 Phaser Game Agent 素材（標誌、背景、道具、原創配樂）
  Generated/               Unity AI 生成素材（角色動畫、音效）
  Resources/Fonts/         Noto Sans TC 子集字型（網頁版需要）
Docs/Guides/               12 款遊戲攻略（Markdown）
Tools/make_font.sh         重新產生字型子集
```

每款遊戲各自一個 Assembly（`MoeGames.GameN`），大廳透過 `[MoeGame(N)]` 反射找到遊戲，彼此不相依。

## 開發

1. 用 Unity Hub 開啟本資料夾（Unity 6000.3.25f1，Built-in Render Pipeline）。
2. 按 Play（任何場景都會自動啟動大廳；主場景為 `Assets/Scenes/Main.unity`）。
3. 測試：Window → General → Test Runner → EditMode → Run All。
4. 建置：選單 **MoeGames → Build → All Platforms**，輸出在 `Builds/`。

## 素材與授權

- 角色模型：專案作者的 8 位萌友角色 FBX。
- 原版美術與配樂：PhaserGame（Phaser Game Agent 產生）。
- 角色動畫、部分音效：Unity AI（Uthana text-to-motion、ElevenLabs、Lyria）。
- 字型：[Noto Sans TC](https://fonts.google.com/noto/specimen/Noto+Sans+TC)，SIL Open Font License 1.1（見 `Assets/Resources/Fonts/MoeFont-OFL.txt`）。
