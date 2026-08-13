namespace AgePilot.Core.Automation;

/// <summary>
/// LLM 可選擇的具名遊戲動作。依 ADR 0015，模型只輸出此列舉與類別化參數，
/// 永遠不輸出任何像素或 normalized 座標；定位一律由 <see cref="GameActionRegistry"/> 的程序負責。
/// </summary>
public enum GameActionKind
{
    Observe,
    Wait,
    QueueVillager,
    AdvanceAge,
    GatherFood,
    GatherWood,
    GatherGold,
    BuildHouse,
}

/// <summary>
/// 動作完成的判定依據。全部可由 OCR 觀測決定，不需要模型參與確認。
///
/// 重要：postcondition 判定的是「動作有沒有被遊戲接受」，不是「策略目標有沒有達成」。
/// 舊版讓 VLM 自填 expectedResult 之所以失敗（三場 session 零次 Confirmed），正是因為它填的是
/// 「升上城堡時代」這類跨數分鐘的策略結果，單一次點擊永遠無法滿足。因此這裡優先採用
/// 下訂當下即可觀測的訊號（資源扣除），而非數十秒後才發生的結果（人口增加）。
/// </summary>
public enum PostconditionKind
{
    /// <summary>Observe／Wait 沒有遊戲側後果，只需等待重新觀察。</summary>
    None,

    /// <summary>指定資源在短時間內下降達門檻，代表遊戲已接受下訂或建造。最即時、最不易被誤判。</summary>
    ResourceSpent,

    PopulationIncrease,
    PopulationCapIncrease,
    AgeAdvanced,
    GatherRateIncrease,
}

/// <summary>postcondition 要觀察的資源。</summary>
public enum TrackedResource { Food, Wood, Gold, Stone }

public sealed record Postcondition(
    PostconditionKind Kind,
    TrackedResource? Resource = null,
    int Amount = 0)
{
    public static readonly Postcondition None = new(PostconditionKind.None);
}

/// <summary>
/// 動作的資源成本門檻。ADR 0006 的靜態遊戲資料（Aoe2InstallationCatalog）已於 commit 05cf39f 刪除，
/// 專案目前沒有任何靜態成本來源，因此 Phase 1 暫用常數。這是已知缺口，不具備文明合法性檢查。
/// </summary>
public sealed record ResourceCost(TrackedResource Resource, int Amount);

/// <summary>模型單輪輸出的動作決定。</summary>
public sealed record GameAction(
    GameActionKind Kind,
    string Reason = "",
    int Quantity = 1);
