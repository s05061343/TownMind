# AgePilot 品牌資產規範

## 品牌核心

AgePilot 採用「六角老鷹＋向右上箭頭」作為應用程式識別。老鷹代表觀察與判斷，箭頭代表漸進式引導；整體語氣維持冷靜、可靠、低干擾，對應產品標語：

> A calm companion for Age of Empires II.

這是 AgePilot 自有識別，不使用《Age of Empires II》官方徽章、字標或 UI 素材，也不暗示官方背書。

## 資產索引

| 路徑 | 用途 |
|---|---|
| `assets/branding/agepilot-logo-master.png` | 透明背景高解析母版 |
| `assets/branding/agepilot-logo-{16,24,32,48,64,128,256,512}.png` | UI、文件與網站用尺寸 |
| `assets/branding/agepilot.ico` | Windows EXE、視窗與系統匣多尺寸圖示 |
| `assets/branding/agepilot-logo-chroma-source.png` | AI 生成的原始色鍵母版，僅供重新產製 |
| `scripts/build-brand-assets.ps1` | 從色鍵母版重建透明 PNG 與 ICO |

重建指令：

```powershell
& .\scripts\build-brand-assets.ps1
```

## 視覺規則

- 主色：金色 `#C6A15B`、深綠灰 `#20241D`、輔助綠 `#7FA66A`、炭黑 `#151713`。
- 圖標周圍至少保留圖標寬度 10% 的淨空。
- 小於 48 px 時只使用徽章，不搭配字標或標語。
- 不拉伸、不旋轉、不改變長寬比，不另加陰影、描邊或發光。
- 深色與淺色背景均使用透明 PNG；Windows shell 使用多尺寸 ICO。

## 程式整合

- Dashboard：52 px 徽章與產品名稱並列。
- Overlay：20 px 徽章，避免搶占遊戲畫面。
- 系統匣：直接讀取執行檔內嵌 ICO，確保安裝版與開發版一致。
- EXE 與 WPF 視窗：使用 `agepilot.ico`。

## 生成來源

母版以使用者提供的老鷹提案圖作為視覺方向，透過 OpenAI 內建 ImageGen 產生獨立徽章，再由本機腳本完成色鍵去背、多尺寸縮圖與 ICO 封裝。提示詞要求六角、右向老鷹、右上箭頭、金色／深綠灰配色、無文字、無官方遊戲圖樣，並以純洋紅色背景供後製去背。
