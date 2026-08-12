# ADR 0009：GamePlan 自動發展執行器

## 狀態

Accepted — 2026-08-12。

## 決策

自動操作只能消費 ADR 0008 的 Qwen3-VL `GamePlan.VisualDecision`，不得重新啟用 `AutomationPolicy`、`GenericEconomicPlanner` 或建築／科技轉譯規則作為第二策略來源。模型直接看畫面並選擇一個通用原子工具；程式只驗證工具、按鍵、座標、前景視窗、頻率及使用者授權。

- 原子工具限 `Observe`、`Wait`、`KeySequence`、`LeftClick`、`RightClick`、`Drag`；鍵碼與 normalized 座標通過白名單與安全邊界後才可執行。
- 一次只執行最高優先的一項動作，結果確認或逾時後回饋規劃器並強制重新規劃。
- 預設 Preview。使用者可直接點 Overlay「啟用」授權本局；Dashboard 開關只控制全域開始熱鍵是否可 Armed。停止熱鍵、焦點／生命週期／觀測 Gate 失效立即停用。
- 需要座標的建築與採集只接受 `Verified` 且跨幀穩定的目標。現有 `VisualCandidate` 不得驅動輸入。
- 靜態遊戲資料負責成本、前置條件與文明合法性；缺少可靠解析結果時不得由模型記憶補值。

## 目前限制

目前 runtime 與執行器已支援多模態觀測和單步原子操作；真實圖片理解仍須主模型與配對 mmproj 到位後完成實機 smoke。VLM 低信心、逾時或連續失敗時停止，不由舊規則接管。
