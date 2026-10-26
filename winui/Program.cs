using Microsoft.UI.Xaml;

// WinUI 3 unpackaged 应用的标准入口。
//
// 必须自己写 Main：
//   1) 初始化 Windows App SDK 的 DispatcherQueue
//   2) 创建 Application 实例
//   3) 调 Application.Start 进入消息循环
// 少了这个文件，XAML 编译器找不到入口点，会以 exit code 1 静默失败。
namespace DouyinQualityHelper
{
    public static class Program
    {
        [System.STAThread]
        static void Main(string[] args)
        {
            global::Microsoft.UI.Xaml.Application.Start((p) =>
            {
                var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());

                System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
    }
}
