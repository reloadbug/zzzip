# 7-Zip 引擎放置说明

zzzip 的 ZIP 格式由 .NET 内置实现处理，**不需要任何额外文件**。
其余 40+ 种格式（7Z / RAR / ISO / TAR / GZIP / BZIP2 / XZ / ZSTD / CAB / ARJ / LZH / WIM / UUE / JAR …）
以及 **AES-256 加密、固实压缩、分卷压缩、自解压 EXE** 全部由官方 7-Zip 驱动。

## 需要放置的文件

把 `7z.exe` 放到 **zzzip.exe 同目录**（或本目录，构建时会被一起拷贝）。

```
7z.exe          必需。7-Zip 命令行主程序
7z.sfx          可选。生成自解压 EXE 需要它；缺失时该功能项自动跳过
```

## 获取方式

从 7-Zip 官网下载安装包后，从安装目录复制：

- 默认安装路径：`C:\Program Files\7-Zip\`
- 官网：https://www.7-zip.org/

> 本仓库**不附带** 7z.exe 二进制文件（避免版权与仓库体积问题），由使用者自行获取。
> 7-Zip 采用 GNU LGPL 许可；zzzip 以独立进程方式调用它，不做静态链接、不修改其代码。

## 查找顺序

zzzip 按下列顺序自动查找 `7z.exe`：

1. 环境变量 `ZZZIP_7Z` 指向的路径
2. `zzzip.exe` 所在目录
3. `zzzip.exe` 目录下的 `7z\` 或 `native\7z\` 子目录
4. 系统 `PATH`
5. `C:\Program Files\7-Zip\` 与 `C:\Program Files (x86)\7-Zip\`

## 找不到时会怎样

不会崩溃。程序进入**降级模式**：

- ZIP 的压缩、解压、浏览、注释仍然完全可用
- 其他格式解压时给出明确中文提示，而不是静默失败
- 「固实压缩 / 分卷压缩 / 加密码 / 自解压」等选项在界面中不可用
- 主界面状态栏会显示 `ZIP(内置) · 未检测到 7-Zip 引擎`
