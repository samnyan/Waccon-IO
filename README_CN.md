# Waccon IO

[English](README.md) | 中文

Waccon IO 是一个多功能的 WACCA 控制器。
因为原本的类似项目基本上都是基于串口模拟，而 com0com 这种虚拟串口工具基本上在Win11已经不可用。所以基于 segatools 的 mercuryio 重新实现，不需要串口支持，并且补全LED灯效支持。

目前支持输入方式：

- wintouch触摸屏、鼠标输入
- 网页端虚拟手台

## 使用说明

### 基础准备

把 `waccon_io.dll` 复制到 segatools 相同目录，并编辑`segatools.ini`，在 `[mercuryio]` 项目当中填入 `path=waccon_io.dll`

最终结果：

```ini
[mercuryio]
path=waccon_io.dll
```

### 使用触屏、鼠标输入

在 `segatools.ini` 中添加 `[waccon]` 项，并参照下方配置：

```ini
[waccon]
; 显示鼠标光标
cursor=1
; 开启Wintouch触摸支持
wintouch=1
; 开启鼠标输入支持
mouse=1

; 搜索窗口标题栏名称
windowTitle=Mercury

; 圆心坐标（屏幕比例，默认正中央偏上方一点点是合适的）
centerX=0.50
centerY=0.47
; 外圈半径（以窗口的宽为100%，1就是全部宽度）
radius=1
; 内圈半径（输入范围是一个环，也就是60%到100%屏幕的一个环）
innerRadius=0.60
startAngle=-90
reverse=0
```

### 虚拟手台

目前支持网页端虚拟手台。运行 `Waccon.Server.exe` ，访问屏幕上显示的地址即可进入虚拟手台。
可以在 `appsettings.json` 中修改监听端口等数据。

### FAQ

- Q: 启动时LED项目是Error
  - A: 缺少ftd2xx.dll，需要放置到`WindowsNoEditor\Mercury\Binaries\Win64`目录里面

## 项目说明

- `waccon-io/` — mercuryio 模块，提供触摸输入、鼠标输入等基础功能，并通过共享内存对外开放IO。
- `Waccon-Server/` — C#编写的虚拟手台服务端，和 waccon-io 通过共享内存通讯，转换为WebSocket、UDP等协议用于外部的虚拟手台。

## 感谢清单

开发过程离不开下面这些项目的参考

- [toucca](https://github.com/BlueGlassBlock/toucca) by BlueGlassBlock
- Any2WACCAi by Raymonf
- Any2WACCA_with_WACCAVCon by Mishe.W#7250
- [Brokenithm-iOS](https://github.com/esterTion/Brokenithm-iOS) by esterTion

**注意：这个项目使用AI辅助开发。**
