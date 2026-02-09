using EggLink.DanhengServer.Enums; // 根据你的项目路径引用属性枚举
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace EggLink.DanhengServer.Data.Excel;

// 指定对应的 JSON 文件路径
[ResourceEntity("BoxingBreakBuffSelectConfig.json")]
public class BoxingBreakBuffSelectExcel : ExcelResource
{
    // 对应 JSON 中的 BoxingClubBuffID，作为该资源的主键 ID
    public int BoxingClubBuffID { get; set; }

    // 对应 JSON 中的 BoxingClubNatureType (例如: Ice, Fire, Thunder)
    public string BoxingClubNatureType { get; set; } = string.Empty;

    // 对应 JSON 中的 ExtraEffectIDList，这是激活战斗机制内核的关键
    public List<int> ExtraEffectIDList { get; set; } = new();

    public override int GetId()
    {
        // 必须返回主键 ID，供 DataManager 映射
        return BoxingClubBuffID;
    }

    public override void Loaded()
    {
        // 加载完成后，将自身注册到 GameData 的全局字典中
        if (!GameData.BoxingBreakBuffSelectData.ContainsKey(BoxingClubBuffID))
        {
            GameData.BoxingBreakBuffSelectData.Add(BoxingClubBuffID, this);
        }
    }
}
