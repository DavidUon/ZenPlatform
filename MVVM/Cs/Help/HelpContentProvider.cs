using System;
using System.Collections.Generic;

namespace ZenPlatform.MVVM.Cs.Help;

public static class HelpContentProvider
{
    private static readonly Dictionary<string, HelpContent> Content = new(StringComparer.OrdinalIgnoreCase)
    {
        ["order.trend_mode"] = new HelpContent
        {
            Title = "趨勢判定",
            Body = "決定新任務方向「要不要偏向做多或做空」功能。\n"
                 + "這是唯一自動建立新任務的設定。\n\n"
                 + "無方向判定：不先限制方向，依觸發條件建立任務。(多空都做)\n"
                 + "均線判定：依均線方向決定偏多或偏空，可自行設定均線。(只做一個方向)\n"
                 + "強制判定：你手動指定只做多或只做空。(只做一個方向)\n"
                 + "系統自動判定：由系統依目前規則自動選方向。(只做一個方向)"
        },
        ["order.same_direction_block"] = new HelpContent
        {
            Title = "新任務阻擋",
            Body = "這個功能會避免在短時間、接近價格重複建立同方向任務。\n\n"
                 + "規則是：\n"
                 + "在你設定的 N 分鐘內，若目前價格仍在上一筆同方向任務成交價的上下 R 點範圍內，\n"
                 + "系統就會取消這次新任務。\n\n"
                 + "例如：N=30、R=100，上一筆多單成交在 10000，\n"
                 + "那 30 分鐘內只要價格還在 9900~10100，就不會再開新的多單任務。"
        },
        ["order.create_time_window"] = new HelpContent
        {
            Title = "允許建立任務時間",
            Body = "只有在你設定的日盤/夜盤時間內，才允許「新任務建立」。\n\n"
                 + "超出這個時段時，就算偵測到進場條件，也會取消新任務。\n"
                 + "已經在場內的任務，不會因為這個設定直接被關掉。"
        },
        ["order.auto_rollover_when_holding"] = new HelpContent
        {
            Title = "結算日自動換月",
            Body = "這個功能是給長線持倉用的。\n\n"
                 + "在每個月第三個星期三 13:45 結算前的你設定時間，如果手上有單，\n"
                 + "系統會先平倉本月合約，再在次月合約建立相同方向與口數的部位。\n\n"
                 + "目的就是讓部位可以跨月連續持有，不會因為結算中斷策略。\n"
                 + "價差與損益會由系統自動調整。\n\n"
                 + "你可以完全放心把你的交易交給系統來交易，當系統交易發生問題或是電腦當機/網路斷線，"
                 + "服務器會在第一時間發送 email 或 line 訊息立刻通知你。"
        },
        ["order.close_before_session_end"] = new HelpContent
        {
            Title = "收盤前平倉",
            Body = "這個功能適用於當沖交易。\n\n"
                 + "啟用後，系統到你設定的早盤/晚盤時間，會把手上部位無條件平倉，避免留倉到收盤後。"
        },
        ["order.close_before_long_holiday"] = new HelpContent
        {
            Title = "連續假日前平倉",
            Body = "當系統判斷即將遇到 2 天以上連續休市（例如週六週日或國定假日連休）時，\n"
                 + "會在你設定的時間自動平倉。\n\n"
                 + "時間可設定範圍為 00:00~04:59。"
        },
        ["tp.total_points"] = new HelpContent
        {
            Title = "任務總停利點數",
            Body = "這個功能是看「任務總損益」。\n\n"
                 + "任務總損益 = 浮動損益 + 已平倉損益。\n"
                 + "當任務總損益 >= 你設定的點數，就平倉結束任務。"
        },
        ["tp.floating_points"] = new HelpContent
        {
            Title = "固定浮動損益停利",
            Body = "這個功能只看「浮動損益」。\n\n"
                 + "當目前浮動損益 >= 你設定的點數，就平倉結束任務。\n"
                 + "不把已平倉損益算進來。"
        },
        ["tp.cover_loss"] = new HelpContent
        {
            Title = "損失超過 N 點後，獲利補足平倉",
            Body = "先看任務總損益（浮動 + 已平倉）有沒有跌到你設定的門檻。\n\n"
                 + "例如 N=100：\n"
                 + "當任務總損益先掉到 -120，系統會顯示「損失已超過門檻，等待獲利補足後平倉...」。\n"
                 + "接下來不會馬上平倉，而是等待任務總損益回到不虧（>= 0）再平倉。\n\n"
                 + "若中途發生停損反手也一樣：\n"
                 + "例如先做多停損 -150，反手做空後慢慢賺回，當整筆任務總損益從 -150 回到 0 或以上時，就會觸發平倉結束。\n\n"
                 + "注意：若同時啟用「固定浮動損益停利」，可能會先被該條件觸發平倉，讓本功能來不及生效。"
        },
        ["tp.rise_from_worst"] = new HelpContent
        {
            Title = "任務總損益低於後，拉回平倉",
            Body = "這個功能有兩個門檻：先低於，再拉回。\n\n"
                 + "例如設定「低於 100、拉回 300」：\n"
                 + "任務總損益（浮動+已平倉）必須先到 -100 以下，規則才會啟動。\n"
                 + "啟動後會持續記住最低點；當從該最低點反彈滿 300 點，就平倉。\n\n"
                 + "簡單說：先確認真的有回撤，再用反彈幅度出場。"
        },
        ["tp.total_profit_drop_after_trigger"] = new HelpContent
        {
            Title = "任務總損益超過後，低於點平倉",
            Body = "這個功能是兩段式條件：先超過，再回落。\n\n"
                 + "任務總損益 = 浮動損益 + 已平倉損益。\n"
                 + "先達到「超過 N 點」後，規則才啟動，並開始追蹤啟動後的最高任務總損益。\n"
                 + "當目前任務總損益從最高點回落 X 點以上時，就平倉。\n\n"
                 + "也可寫成：目前值 <= 最高值 - X（前提是已先達到 N）。\n\n"
                 + "例如 N=100、X=30：先漲到 100 就啟動；若後續最高到 120，之後回落到 90（自最高回落 30），就觸發平倉。"
        },
        ["tp.retrace_exit"] = new HelpContent
        {
            Title = "獲利超過後碰均線平倉",
            Body = "先達到你設定的獲利門檻。\n\n"
                 + "達標後，只要價格碰到你設定的 MA 均線，就立刻平倉結束任務。"
        },
        ["sl.fixed_points"] = new HelpContent
        {
            Title = "固定點數停損",
            Body = "這個功能就是「任務浮動損益虧到你設定的點數就出場」。\n\n"
                 + "例如你設 150 點，只要這筆任務的浮動損益到 -150 點，就會觸發停損。\n\n"
                 + "如果有勾「停損後反手下單」，系統會停損後改做反方向；"
                 + "沒勾的話，就直接平倉並結束這筆任務。"
        },
        ["sl.auto_mode"] = new HelpContent
        {
            Title = "自動判定停損",
            Body = "這是系統依目前策略規則自動判斷停損時機的模式。\n\n"
                 + "如果你要完全用固定點數控制停損，請改用「固定點數停損」。"
        },
        ["sl.reverse_after_stop"] = new HelpContent
        {
            Title = "停損後反手下單",
            Body = "啟用後，停損觸發時不只平倉，還會用反方向再下單。\n\n"
                 + "例如原本做多停損，就改做空；原本做空停損，就改做多。\n"
                 + "反手是否繼續執行，會受「最大反手次數」限制。"
        },
        ["sl.max_reverse"] = new HelpContent
        {
            Title = "最大反手次數",
            Body = "限制一筆任務最多可以連續反手幾次。\n\n"
                 + "到達上限後，再觸發停損就不再反手，改為直接平倉結束任務。"
        },
        ["sl.absolute_stop"] = new HelpContent
        {
            Title = "絕對停損",
            Body = "這是任務的最後防線。\n\n"
                 + "當任務總損益（浮動 + 已平倉）虧損超過你設定的點數，會立刻平倉並停止這筆任務。\n"
                 + "適合用來防止單一任務持續擴大虧損。"
        },
        ["sl.retrace_exit"] = new HelpContent
        {
            Title = "損失超過後碰均線平倉",
            Body = "先達到你設定的虧損門檻。\n\n"
                 + "達標後，只要價格碰到你設定的 MA 均線，就立刻平倉結束任務。"
        }
    };

    public static HelpContent Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return new HelpContent
            {
                Title = "說明",
                Body = "沒有指定說明內容。"
            };
        }

        return Content.TryGetValue(key, out var content)
            ? content
            : new HelpContent
            {
                Title = "說明",
                Body = $"尚未建立此項說明：{key}"
            };
    }
}
