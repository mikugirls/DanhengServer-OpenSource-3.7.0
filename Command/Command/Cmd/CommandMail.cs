using EggLink.DanhengServer.Internationalization;
using EggLink.DanhengServer.Database.Inventory;
using EggLink.DanhengServer.Util; 

namespace EggLink.DanhengServer.Command.Command.Cmd;

[CommandInfo("mail", "Game.Command.Mail.Desc", "Game.Command.Mail.Usage", permission: "egglink.manage")]
public class CommandMail : ICommand
{
    [CommandDefault]
    public async ValueTask Mail(CommandArg arg)
    {
        // 1. 基础检查
        if (arg.Target == null)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
            return;
        }

        if (arg.Args.Count < 3) 
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
            return;
        }

        // 2. 解析基础变量
        var sender = arg.Args[0];
        if (!int.TryParse(arg.Args[1], out var templateId)) templateId = 0;
        if (!int.TryParse(arg.Args[2], out var expiredDay)) expiredDay = 30;

        // 3. 预载模板文案
        var title = "";
        var content = "";
        var template = MailTemplate.Get(templateId);
        if (template != null)
        {
            title = template.Value.Title;
            content = template.Value.Content;
        }

        var attachments = new List<ItemData>();
        
        // 4. 定义状态机标志
        var flagTitle = false;
        var flagContent = false;
        var flagAttach = false;

        // 💡 关键修改：直接遍历原始参数列表，不依赖 IndexOf
        // 这样可以避免因为 CommandArg 对参数的预分类导致索引查找失败或偏移
        foreach (var text in arg.Args)
        {
            // 检查是否遇到了旗标
            if (text == "_TITLE")
            {
                flagTitle = true; flagContent = false; flagAttach = false;
                continue;
            }
            if (text == "_CONTENT")
            {
                flagTitle = false; flagContent = true; flagAttach = false;
                continue;
            }
            if (text == "_ATTACH")
            {
                flagTitle = false; flagContent = false; flagAttach = true;
                continue;
            }

            // --- 核心逻辑：只有当旗标后面确实有“实质内容”时，才清空并覆盖模板 ---
            if (flagTitle && !string.IsNullOrWhiteSpace(text))
            {
                // 如果是该旗标下的第一个有效文本段，且有模板，则执行“覆盖”清空
                if (template != null && title == template.Value.Title) title = "";
                title += text + " ";
            }
            
            if (flagContent && !string.IsNullOrWhiteSpace(text))
            {
                if (template != null && content == template.Value.Content) content = "";
                content += text + " ";
            }

            if (flagAttach)
            {
                var parts = text.Split(':');
                if (parts.Length == 2 && uint.TryParse(parts[0], out var id) && uint.TryParse(parts[1], out var count))
                {
                    attachments.Add(new ItemData { ItemId = (int)id, Count = (int)count });
                }
            }
        }

        title = title.Trim();
        content = content.Trim();

        // 5. 最终验证与发送
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(content))
        {
            await arg.SendMsg("错误：邮件标题或内容不能为空！");
            return;
        }

        if (attachments.Count > 0)
            await arg.Target.Player!.MailManager!.SendMail(sender, title, content, templateId, attachments, expiredDay);
        else
            await arg.Target.Player!.MailManager!.SendMail(sender, title, content, templateId, expiredDay);

        await arg.SendMsg(I18NManager.Translate("Game.Command.Mail.MailSent"));
    }
}