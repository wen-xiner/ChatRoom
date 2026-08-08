using System;
using ChatRoom.Core.Auth;

namespace KeyGen
{
    /// <summary>
    /// 注册机（不随软件安装）：输入本机注册码 → 生成可复制的认证码。
    /// 与主程序共享 AuthAlgorithm.cs，算法一致。
    /// </summary>
    internal static class Program
    {
        private static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("========== ChatRoom 注册机 ==========");
            Console.WriteLine();
            Console.WriteLine("把软件【注册】窗口里显示的注册码粘贴到下面，然后回车：");

            var regCode = Console.ReadLine()?.Trim();
            while (string.IsNullOrWhiteSpace(regCode) || regCode.Length != 64)
            {
                Console.WriteLine("注册码应为 64 位十六进制字符（SHA256），请重新输入：");
                regCode = Console.ReadLine()?.Trim();
            }

            var authCode = AuthAlgorithm.ComputeAuthCode(regCode);
            Console.WriteLine();
            Console.WriteLine("生成的认证码（复制到软件注册框粘贴，点注册即可）：");
            Console.WriteLine(authCode);
            Console.WriteLine();
            Console.WriteLine("按回车退出...");
            Console.ReadLine();
        }
    }
}
