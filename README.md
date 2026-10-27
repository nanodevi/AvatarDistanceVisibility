# 材质距离显示工具

配合 `com.zhuozhi.liltoon-distance-visibility`（lilToon 距离显示）使用的批量管理工具（https://github.com/zhuozhi233/lilToon-Distance-Visibility）。

## 打开方式

菜单 `Tools > 材质距离显示工具 > 打开面板`，或者：

- `Tools > 材质距离显示工具 > 对选中的 Avatar 一键添加距离显示`
- `Tools > 材质距离显示工具 > 对选中的 Avatar 一键移除距离显示`

## 面板用法

1. 把 Avatar 根节点（带 `VRC Avatar Descriptor` 的那个物体）**拖进「Avatar 根节点」框**，或选中它后点「使用当前选中」。
2. 面板会列出这个 Avatar 下所有 Renderer 用到的材质，并标注状态：
   - `可添加`：是 lilToon 材质，还没加距离显示
   - `已添加`：已经是 DistanceVisibility 系列 Shader
   - `不支持`：其它 Shader（Standard、粒子等）
3. 用每行左边的勾选框挑选要处理的材质。上面两排是快捷勾选：
   - **全选 / 全不选 / 反选**
   - **只选已添加**：只勾选已经加了距离显示的材质（批量改属性最常用）
   - **只选已启用 / 只选已关闭**：按 `_DV_Enabled` 当前状态筛选
   - **只选可添加**：只勾选还能加距离显示的 lilToon 材质
   - **「快捷勾选：自动只勾选「已添加」的材质」** 勾上后，每次扫描/添加/移除后都会自动只勾选已添加的材质，省得反复点。
   - 顶部状态栏会显示：已添加 N 个（已启用 X / 已关闭 Y）、可添加 M 个、当前勾选 K 个。
4. 三个按钮：
   - **添加距离显示**：把勾选的可添加材质转成 DistanceVisibility Shader（等价于右键菜单「添加距离显示」）
   - **移除距离显示**：还原成原 lilToon Shader
   - **恢复默认属性**：把 `_DV_*` 恢复默认（关闭、近端 2 米、远端 5 米）
5. 「批量属性设置」每项是一个三选下拉：
   - `不修改`：保持材质原值（不会写任何东西）
   - `启用（写 1）` / `关闭（写 0）`：才会真的写进材质

   可设置的项：启用距离显示 / 近端限制 / 远端限制 / 使用网格中心，外加「写入近端距离·过渡」「写入远端距离·过渡」两个勾选（勾上才写数值，与开关状态独立）。
   点「从第一个材质读取」可以把第一个已添加材质的当前值读进面板。

   ⚠️ `_DV_Enabled`（启用距离显示）是总开关，默认是**关**的。只改距离参数而不把它设成「启用」，画面不会有任何变化。如果生成过 MA 开关，运行时这个值由 MA 动画接管。
6. 「Modular Avatar 开关」：
   - 参数名：MA 参数名，默认 `DV_Visible`
   - 菜单显示名：表情菜单里显示的文字
   - 默认开启：开关初始状态
   - 参数保存（saved）：默认关闭；关掉后重进世界会回到默认状态
   - 生成 / 更新 MA 开关：在 Avatar 下新建一个物体，挂上
     - **MA Merge Animator**（生成的 Animator 控制器：On / Off 两个状态，动画 `material._DV_Enabled`）
     - **MA Menu Item**（Toggle）
     - **MA Parameters**（注册同名参数：`Bool`、**已同步**、`saved` 按上面的勾选）——这个必须有，否则参数不会进
       Expression Parameters，别人看不到你开关的效果
     - **MA Menu Installer**
   - 移除 MA 开关：删掉那个物体

## 生成的文件

`Assets/@幻梦幻不梦蝶/材质距离显示工具/Generated/<Avatar 名>/`

- `DistanceVisibility_On.anim` / `DistanceVisibility_Off.anim`
- `DistanceVisibility.controller`

重新生成会覆盖这几个文件。

## 注意

- 需要项目里有 **Modular Avatar** 和 **VRChat SDK3 Avatars**（脚本直接引用了它们的类型），只做添加/移除的话也需要 `com.zhuozhi.liltoon-distance-visibility`。
- Unity 的材质属性动画按 **Renderer** 生效，所以同一个 Renderer 上其它已添加距离显示的材质，也会被这个开关一起控制。
- MA 开关控制的是 `_DV_Enabled`（总开关）。材质本身的距离参数仍然是你在 Inspector 里设的值。
- 面板里所有操作都可以 Ctrl+Z 撤销（材质属性）；生成的资产需要手动删。
