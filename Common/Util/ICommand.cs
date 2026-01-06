using Kodnix.Character;

namespace EggLink.DanhengServer.Util
{
    public static class IConsole
    {
        public const string PrefixContent = "> ";
        private const string PinkColor = "\e[38;2;255;192;203m";
        private const string RedColor = "\e[38;2;255;0;0m";
        private const string ResetColor = "\e[0m";

        // coloured prefix
        public static string Prefix => $"{(IsCommandValid ? ResetColor : RedColor)}{PrefixContent}{ResetColor}";

        public static bool IsCommandValid { get; private set; } = true;
        private const int HistoryMaxCount = 10;

        public static List<char> Input { get; set; } = [];
        private static int CursorIndex { get; set; }
        private static readonly List<string> InputHistory = [];
        private static int HistoryIndex = -1;

        public static event Action<string>? OnConsoleExcuteCommand;

        public static void InitConsole()
        {
            Console.Title = "Danheng Server";
        }

        public static int GetWidth(string str)
            => str.ToCharArray().Sum(EastAsianWidth.GetLength);

        public static void RedrawInput(List<char> input, bool hasPrefix = true)
            => RedrawInput(new string([.. input]), hasPrefix);

        public static void RedrawInput(string input, bool hasPrefix = true)
        {
            // 1. 检查指令合法性
            UpdateCommandValidity(input);

            // 2. 获取当前行号，准备重绘
            var (_, top) = Console.GetCursorPosition();

            // 3. 回到行首并清空当前行，增加 1 个空格余量防止残留
            Console.SetCursorPosition(0, top);
            Console.Write(new string(' ', Console.BufferWidth - 1));
            Console.SetCursorPosition(0, top);

            // 4. 准备显示内容
            string prefixStr = hasPrefix ? Prefix : "";
            string fullDisplay = prefixStr + input;

            // 5. 写入完整字符串
            Console.Write(fullDisplay);

            // 6. 【关键修复】基于当前逻辑 CursorIndex 重新计算物理光标位置
            int prefixWidth = hasPrefix ? GetWidth(PrefixContent) : 0;
            
            // 截取光标之前的文本段落，计算其实际物理宽度
            string textBeforeCursor = input.Substring(0, Math.Min(CursorIndex, input.Length));
            int physicalCursorLeft = prefixWidth + GetWidth(textBeforeCursor);

            // 7. 设置物理光标，确保不超出缓冲区
            Console.SetCursorPosition(Math.Min(physicalCursorLeft, Console.BufferWidth - 1), top);
        }

        private static void UpdateCommandValidity(string input)
        {
            IsCommandValid = CheckCommandValid(input);
        }

        #region Handlers

        public static void HandleEnter()
        {
            var input = new string([.. Input]);
            if (string.IsNullOrWhiteSpace(input)) return;

            Console.WriteLine();
            Input = [];
            CursorIndex = 0;
            if (InputHistory.Count >= HistoryMaxCount)
                InputHistory.RemoveAt(0);
            InputHistory.Add(input);
            HistoryIndex = InputHistory.Count;

            if (input.StartsWith('/')) input = input[1..].Trim();
            OnConsoleExcuteCommand?.Invoke(input);

            IsCommandValid = true;
        }

        public static void HandleBackspace()
        {
            // 严格检查索引和内容
            if (CursorIndex <= 0 || Input.Count == 0) return;
            
            CursorIndex--;
            // 移除字符前记录其宽度
            Input.RemoveAt(CursorIndex);

            // 移除后强制重绘，它会自动处理坐标和残留清除
            RedrawInput(Input);
        }

        public static void HandleUpArrow()
        {
            if (InputHistory.Count == 0) return;
            if (HistoryIndex <= 0) return;

            HistoryIndex--;
            var history = InputHistory[HistoryIndex];
            Input = [.. history];
            CursorIndex = Input.Count;

            UpdateCommandValidity(history);
            RedrawInput(Input);
        }

        public static void HandleDownArrow()
        {
            if (HistoryIndex >= InputHistory.Count) return;

            HistoryIndex++;
            if (HistoryIndex >= InputHistory.Count)
            {
                HistoryIndex = InputHistory.Count;
                Input = [];
                CursorIndex = 0;
                IsCommandValid = true;
            }
            else
            {
                var history = InputHistory[HistoryIndex];
                Input = [.. history];
                CursorIndex = Input.Count;
                UpdateCommandValidity(history);
            }
            RedrawInput(Input);
        }

        public static void HandleLeftArrow()
        {
            // 1. 边界防御：如果已经在最左边，绝对不执行后续逻辑
            if (CursorIndex <= 0) return;

            // 2. 更新逻辑索引
            CursorIndex--;

            // 3. 强制触发重绘，由重绘逻辑计算准确的物理光标位置
            // 这种方式比手动移动 SetCursorPosition 更稳健，能完美处理中文字符
            RedrawInput(Input);
        }

        public static void HandleRightArrow()
        {
            // 1. 边界防御：如果已经在最右边，不执行
            if (CursorIndex >= Input.Count) return;

            // 2. 更新逻辑索引
            CursorIndex++;

            // 3. 强制重绘
            RedrawInput(Input);
        }

        public static void HandleInput(ConsoleKeyInfo keyInfo)
        {
            if (char.IsControl(keyInfo.KeyChar)) return;
            var newWidth = GetWidth(new string([.. Input])) + GetWidth(keyInfo.KeyChar.ToString());
            if (newWidth >= (Console.BufferWidth - GetWidth(PrefixContent))) return;
            HandleInput(keyInfo.KeyChar);
        }

        public static void HandleInput(char keyChar)
        {
            Input.Insert(CursorIndex, keyChar);
            CursorIndex++;

            // 输入后重绘
            RedrawInput(Input);
        }

        #endregion

        public static string ListenConsole()
        {
            while (true)
            {
                ConsoleKeyInfo keyInfo;
                try { keyInfo = Console.ReadKey(true); }
                catch (InvalidOperationException) { continue; }

                switch (keyInfo.Key)
                {
                    case ConsoleKey.Enter:
                        HandleEnter();
                        break;
                    case ConsoleKey.Backspace:
                        HandleBackspace();
                        break;
                    case ConsoleKey.LeftArrow:
                        HandleLeftArrow();
                        break;
                    case ConsoleKey.RightArrow:
                        HandleRightArrow();
                        break;
                    case ConsoleKey.UpArrow:
                        HandleUpArrow();
                        break;
                    case ConsoleKey.DownArrow:
                        HandleDownArrow();
                        break;
                    default:
                        HandleInput(keyInfo);
                        break;
                }
            }
        }

        private static bool CheckCommandValid(string input)
        {
            if (string.IsNullOrEmpty(input))
                return true;

            var invalidChars = new[] { '@', '#', '$', '%', '&', '*' };
            return !invalidChars.Any(c => input.Contains(c));
        }
    }
}