// 示例插件自带的工具类（V0.2.315）。
// 生成工程时这个文件会被拷到 cs/Plugins/example/ 下，和模组一起编译、一起打包。
// 插件效果的 emit 模板里可以直接调用它，例如：await CreatureCmd.Heal({self}, PluginDemo.Half({amount}));

public static class PluginDemo
{
    /// <summary>把数值减半（示例用）。</summary>
    public static decimal Half(decimal x) => x / 2m;

    /// <summary>把数值翻倍（示例用）。</summary>
    public static decimal Twice(decimal x) => x * 2m;
}
