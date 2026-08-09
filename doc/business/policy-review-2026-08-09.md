# AgePilot 政策邊界檢查 — 2026-08-09

## 查核來源

- Age of Empires Code of Conduct：<https://www.ageofempires.com/code-of-conduct/>
- Microsoft Services Agreement：<https://www.microsoft.com/en-us/servicesagreement>
- Age of Empires Player Safety／Support：<https://support.ageofempires.com/hc/en-us/p/PlayerSafety>

## 查核結果

Age of Empires Code of Conduct 要求公平遊玩，並把刻意破壞對局或 tampering 列為違反公平精神的例子。Microsoft Services Agreement 禁止繞過技術保護、逆向工程、未授權存取，以及啟用 cheating／tampering 的未授權軟硬體；官方保留偵測與阻止此類工具的權利。

上述公開文件沒有明文列出「只讀螢幕 OCR 教練 Overlay」是否允許，也沒有提供可據以宣稱 AgePilot 已獲官方核准的條款。因此目前只能確認 AgePilot 的設計降低了風險，不能把技術上的只讀邊界解讀為官方授權。

## 必須維持的產品邊界

- 只使用作業系統提供的畫面擷取；不注入遊戲程序。
- 不讀取遊戲記憶體、封包、受保護資料或未公開 API。
- 不發送滑鼠、鍵盤或其他遊戲輸入。
- 不提供自動化操作、敵方隱藏資訊、地圖全開或繞過 Fog of War。
- 不修改遊戲檔案、反作弊、網路流量或 Xbox 服務。
- 不宣稱 Microsoft、Xbox、World's Edge 或 Forgotten Empires 核准／背書。

## 發佈限制

在取得官方 Support 對此類只讀 Overlay 的明確答覆前：

- Public Alpha 應標示為非官方、使用者自負風險。
- 建議限定單人／對 AI／非排名情境測試。
- 不把「未找到明文禁止」寫成「官方允許」。
- 若官方政策、反作弊行為或 Support 答覆改變，立即暫停發佈並重新審查。

Age of Empires Support 的 ban 說明建議對可接受行為有疑問時聯絡 Support；正式公開前仍需由專案負責人提交具體功能描述並保存答覆。這一步需要外部帳號與對外聯絡授權，不能由本機建置流程代替。
