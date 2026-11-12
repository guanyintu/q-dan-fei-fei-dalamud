# q弹狒狒

[简体中文](README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md)

會讓所有人變得 Q 彈。作者：**someboy**。**本專案主要由 Codex 生成。**

[下載衛月版](https://github.com/guanyintu/q-dan-fei-fei-dalamud/releases/latest) · [獨立 EXE 版](https://github.com/guanyintu/q-dan-fei-fei-standalone)

衛月版會更流暢一些，已有衛月建議優先使用衛月版。獨立版無需衛月。請勿同時啟用兩個版本的效果。

## 安裝

在衛月設定中新增以下自訂外掛倉庫，再搜尋 `q弹狒狒`：

```text
https://raw.githubusercontent.com/guanyintu/q-dan-fei-fei-dalamud/main/repo.json
```

手動開發載入：解壓縮 `latest.zip`，在 Dev Plugin Locations 加入 `q弹狒狒.dll` 的完整路徑，保留相依 DLL 和 `Assets` 資料夾。輸入 `/qbounce` 並開啟效果；初始狀態為關閉。

版本 **0.1.7** 使用 Dalamud API 15 / .NET 10，本次對照國服 Dalamud 15.0.4.0 與遊戲 2026.11.03.0000.0000。發布內容由官方 DalamudPackager 產生：`latest.zip` 和外掛清單（GitHub 附件下載名為 `manifest.json`）。

## 語言與操作

介面支援简体中文、繁體中文和 English。預設跟隨衛月介面語言，其他語言使用英文；可在視窗頂部手動選擇並儲存。切換語言不會重設效果或重新播放音樂。系統及原生例外的詳細資訊保留原始文字。

預設強度 0.4、週期 0.45 秒。已載入的玩家、NPC、怪物同步 Q 彈，也可啟用隨機相位。F6 開關、F7 切換強度、F8 重設節奏；快速鍵預設停用，不會攔截遊戲原有綁定。

## 音樂

[音樂出處：Bilibili BV1VGYR6bECg](https://www.bilibili.com/video/BV1VGYR6bECg/)。內附 MP3 約 3 分 7 秒、4.50 MB，也可選擇其他本機 MP3 或 WAV。手動指定的路徑及設定目錄中的 `bgm.mp3` 優先於內附音樂。檔案遺失不影響 Q 彈效果。

## 編譯與限制

安裝 .NET 10 SDK 和相符的 API 15 Dalamud，執行 `./build.ps1 -DalamudPath <衛月目錄>`。腳本會編譯、測試及檢查發布包，直接複製 SDK 產物，不會自行重新壓縮 ZIP。詳細內容請見 [驗收報告](docs/VALIDATION.md) 和 [技術說明](docs/TECHNICAL-NOTES.md)。

只修改渲染比例；過場、載入和 GPose 會暫停。若模型仍然變形，請停用效果後切換區域或重新登入。編譯及自動化測試無法取代所有模型、裝備附著與長時間執行的實機驗證。
